using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Traduit une <see cref="AnalyticQuery"/> en arbre d'expressions LINQ :
/// source.Where(filtres).GroupBy(clé).Select(g => new object[] { clés..., agrégats... }).
/// Le GroupBy et les agrégats restent dans l'IQueryable : un provider comme EF Core
/// peut donc les pousser en SQL.
/// </summary>
internal static class QueryEngine<T>
{
    private const int MaxGroupFields = 4;
    private const int MaxDistinctValues = 1000;

    public static ResultSet Execute(
        IQueryable<T> source,
        ModelMetadata metadata,
        IReadOnlyDictionary<string, LambdaExpression> selectors,
        AnalyticQuery query)
    {
        if (query.GroupBy.Count > MaxGroupFields)
            throw new NotSupportedException($"Au plus {MaxGroupFields} dimensions de regroupement sont supportées.");

        var parameter = Expression.Parameter(typeof(T), "x");
        var filtered = ApplyFilters(source, metadata, selectors, query.Filters ?? [], parameter);
        var columns = new List<ResultColumn>();

        // Clés de regroupement
        var keyBodies = new List<Expression>();
        foreach (var fieldRef in query.GroupBy)
        {
            var field = RequireKind(metadata.GetField(fieldRef.Field), FieldKind.Dimension);
            var body = Rebind(selectors[field.Name], parameter);
            var dataType = field.DataType;
            if (fieldRef.Grain != DateGrain.None)
            {
                if (field.DataType != DataType.Date)
                    throw new InvalidOperationException($"La granularité '{fieldRef.Grain}' exige une dimension date ('{field.Name}').");
                body = DateGrains.Apply(body, fieldRef.Grain);
                dataType = fieldRef.Grain == DateGrain.Day ? DataType.Date : DataType.Text;
            }
            keyBodies.Add(body);
            var caption = fieldRef.Grain == DateGrain.None ? field.Caption : $"{field.Caption} ({DateGrains.Caption(fieldRef.Grain)})";
            columns.Add(new ResultColumn(fieldRef.Key, caption, FieldKind.Dimension, dataType));
        }

        var slotTypes = keyBodies.Select(b => b.Type)
            .Concat(Enumerable.Repeat(typeof(int), MaxGroupFields - keyBodies.Count))
            .ToArray();
        var keyType = typeof(GroupKey<,,,>).MakeGenericType(slotTypes);
        var bindings = Enumerable.Range(0, MaxGroupFields).Select(i => Expression.Bind(
            keyType.GetProperty($"K{i + 1}")!,
            i < keyBodies.Count ? keyBodies[i] : Expression.Constant(0)));
        var keySelector = Expression.Lambda(Expression.MemberInit(Expression.New(keyType), bindings), parameter);

        // Projection : clés puis agrégats
        var group = Expression.Parameter(typeof(IGrouping<,>).MakeGenericType(keyType, typeof(T)), "g");
        var key = Expression.Property(group, "Key");
        var cells = new List<Expression>();
        for (var i = 0; i < keyBodies.Count; i++)
            cells.Add(Expression.Convert(Expression.Property(key, $"K{i + 1}"), typeof(object)));

        foreach (var measureRef in query.Measures)
        {
            var field = RequireKind(metadata.GetField(measureRef.Measure), FieldKind.Measure);
            var aggregation = measureRef.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum;
            cells.Add(Expression.Convert(Aggregate(group, selectors[field.Name], aggregation), typeof(object)));
            var caption = measureRef.Aggregation is null ? field.Caption : $"{field.Caption} ({aggregation})";
            columns.Add(new ResultColumn(measureRef.Key, caption, FieldKind.Measure, DataType.Number, field.Format));
        }
        var rowSelector = Expression.Lambda(Expression.NewArrayInit(typeof(object), cells), group);

        var rows = (List<object?[]>)Invoke(nameof(Run), [keyType], filtered, keySelector, rowSelector);

        var ordered = Order(rows, columns, query);
        if (query.Top is { } top) ordered = ordered.Take(top).ToList();
        FormatGrainLabels(ordered, query.GroupBy);
        return new ResultSet(columns, ordered);
    }

    public static IReadOnlyList<object?> DistinctValues(IQueryable<T> source, LambdaExpression selector) =>
        (IReadOnlyList<object?>)Invoke(nameof(Distinct), [selector.ReturnType], source, selector);

    private static List<object?[]> Run<TKey>(
        IQueryable<T> source,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<IGrouping<TKey, T>, object?[]>> rowSelector) =>
        source.GroupBy(keySelector).Select(rowSelector).ToList();

    private static IReadOnlyList<object?> Distinct<TValue>(IQueryable<T> source, Expression<Func<T, TValue>> selector) =>
        source.Select(selector).Distinct().OrderBy(v => v).Take(MaxDistinctValues)
            .AsEnumerable().Select(v => (object?)v).ToList();

    private static IQueryable<T> ApplyFilters(
        IQueryable<T> source,
        ModelMetadata metadata,
        IReadOnlyDictionary<string, LambdaExpression> selectors,
        IEnumerable<FilterSpec> filters,
        ParameterExpression parameter)
    {
        foreach (var filter in filters)
        {
            var field = RequireKind(metadata.GetField(filter.Field), FieldKind.Dimension);
            var selector = selectors[field.Name];
            var valueType = selector.ReturnType;
            var values = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(valueType))!;
            foreach (var text in filter.Values)
                values.Add(InvariantValue.Parse(text, valueType));

            Expression body = Expression.Call(
                typeof(Enumerable), nameof(Enumerable.Contains), [valueType],
                Expression.Constant(values), Rebind(selector, parameter));
            if (filter.Operator == FilterOperator.NotIn)
                body = Expression.Not(body);
            source = source.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
        }
        return source;
    }

    private static Expression Aggregate(Expression group, LambdaExpression selector, Aggregation aggregation)
    {
        if (aggregation == Aggregation.Count)
            return Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [typeof(T)], group);

        var method = aggregation switch
        {
            Aggregation.Sum => nameof(Enumerable.Sum),
            Aggregation.Average => nameof(Enumerable.Average),
            Aggregation.Min => nameof(Enumerable.Min),
            Aggregation.Max => nameof(Enumerable.Max),
            _ => throw new NotSupportedException(aggregation.ToString()),
        };
        try
        {
            // Surcharges numériques spécialisées (Sum(Func<T, decimal>), ...)
            return Expression.Call(typeof(Enumerable), method, [typeof(T)], group, selector);
        }
        catch (InvalidOperationException) when (aggregation is Aggregation.Min or Aggregation.Max)
        {
            // Min/Max génériques (dates, etc.)
            return Expression.Call(typeof(Enumerable), method, [typeof(T), selector.ReturnType], group, selector);
        }
    }

    private static List<object?[]> Order(List<object?[]> rows, List<ResultColumn> columns, AnalyticQuery query)
    {
        var sorts = query.Sort is { Count: > 0 } ? query.Sort : query.GroupBy.Select(g => new SortSpec(g.Key)).ToList();
        IOrderedEnumerable<object?[]>? ordered = null;
        foreach (var sort in sorts)
        {
            var index = columns.FindIndex(c => c.Key == sort.Key);
            if (index < 0) throw new KeyNotFoundException($"Tri sur une colonne absente : '{sort.Key}'.");
            Func<object?[], object?> keyOf = row => row[index];
            ordered = (ordered, sort.Descending) switch
            {
                (null, false) => rows.OrderBy(keyOf, Comparer<object?>.Default),
                (null, true) => rows.OrderByDescending(keyOf, Comparer<object?>.Default),
                (_, false) => ordered.ThenBy(keyOf, Comparer<object?>.Default),
                (_, true) => ordered.ThenByDescending(keyOf, Comparer<object?>.Default),
            };
        }
        return ordered?.ToList() ?? rows;
    }

    private static void FormatGrainLabels(List<object?[]> rows, IReadOnlyList<FieldRef> groupBy)
    {
        for (var i = 0; i < groupBy.Count; i++)
        {
            var grain = groupBy[i].Grain;
            if (grain is DateGrain.None or DateGrain.Day) continue;
            foreach (var row in rows)
                row[i] = DateGrains.Label((int)row[i]!, grain);
        }
    }

    private static FieldDescriptor RequireKind(FieldDescriptor field, FieldKind kind) =>
        field.Kind == kind ? field : throw new InvalidOperationException($"'{field.Name}' n'est pas une {(kind == FieldKind.Dimension ? "dimension" : "mesure")}.");

    private static Expression Rebind(LambdaExpression lambda, ParameterExpression parameter) =>
        new ParameterReplacer(lambda.Parameters[0], parameter).Visit(lambda.Body);

    private static object Invoke(string method, Type[] typeArguments, params object[] arguments)
    {
        try
        {
            return typeof(QueryEngine<T>)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeArguments)
                .Invoke(null, arguments)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

/// <summary>Clé de regroupement composite (les emplacements inutilisés contiennent une constante).</summary>
internal sealed class GroupKey<T1, T2, T3, T4> : IEquatable<GroupKey<T1, T2, T3, T4>>
{
    public T1 K1 { get; set; } = default!;
    public T2 K2 { get; set; } = default!;
    public T3 K3 { get; set; } = default!;
    public T4 K4 { get; set; } = default!;

    public bool Equals(GroupKey<T1, T2, T3, T4>? other) =>
        other is not null
        && EqualityComparer<T1>.Default.Equals(K1, other.K1)
        && EqualityComparer<T2>.Default.Equals(K2, other.K2)
        && EqualityComparer<T3>.Default.Equals(K3, other.K3)
        && EqualityComparer<T4>.Default.Equals(K4, other.K4);

    public override bool Equals(object? obj) => Equals(obj as GroupKey<T1, T2, T3, T4>);

    public override int GetHashCode() => HashCode.Combine(K1, K2, K3, K4);
}
