using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Translates an <see cref="AnalyticQuery"/> into a LINQ expression tree:
/// source.Where(filters).GroupBy(key).OrderBy(sort).Take(top).Select(g => new object[] { keys..., aggregates... }).
/// Everything stays inside the IQueryable, so a provider such as EF Core can push
/// grouping, aggregates, sorting and Top N down to SQL.
/// </summary>
internal static class QueryEngine<T>
{
    private const int MaxGroupFields = 4;
    private const int MaxDistinctValues = 1000;

    private static readonly Type[] GroupKeyTypes = [typeof(GroupKey<>), typeof(GroupKey<,>), typeof(GroupKey<,,>), typeof(GroupKey<,,,>)];

    public static async Task<ResultSet> ExecuteAsync(
        IQueryable<T> source,
        ModelMetadata metadata,
        IReadOnlyDictionary<string, LambdaExpression> selectors,
        AnalyticQuery query,
        CancellationToken cancellationToken)
    {
        if (query.GroupBy.Count > MaxGroupFields)
            throw new NotSupportedException($"At most {MaxGroupFields} grouping dimensions are supported.");

        var parameter = Expression.Parameter(typeof(T), "x");
        var filtered = ApplyFilters(Cancellable(source, cancellationToken), metadata, selectors, query.Filters ?? [], parameter);
        var columns = new List<ResultColumn>();

        // Grouping keys
        var keyBodies = new List<Expression>();
        foreach (var fieldRef in query.GroupBy)
        {
            var field = RequireKind(metadata.GetField(fieldRef.Field), FieldKind.Dimension);
            var body = ApplyGrain(field, Rebind(selectors[field.Name], parameter), fieldRef.Grain);
            var dataType = fieldRef.Grain switch
            {
                DateGrain.None => field.DataType,
                DateGrain.Day => DataType.Date,
                _ => DataType.Text,
            };
            keyBodies.Add(body);
            var caption = DateGrains.Caption(field.Caption, fieldRef.Grain);
            columns.Add(new ResultColumn(fieldRef.Key, caption, FieldKind.Dimension, dataType));
        }

        // One slot per grouped field: a constant slot would end up in SQL's GROUP BY, behind a subquery, and slow the
        // database down. Without grouping (KPIs), one constant puts every row in the same group.
        Expression[] slots = keyBodies.Count > 0 ? [.. keyBodies] : [Expression.Constant(0)];
        var keyType = GroupKeyTypes[slots.Length - 1].MakeGenericType(slots.Select(s => s.Type).ToArray());
        var bindings = slots.Select((slot, i) => Expression.Bind(keyType.GetProperty($"K{i + 1}")!, slot));
        var keySelector = Expression.Lambda(Expression.MemberInit(Expression.New(keyType), bindings), parameter);

        // Projection: keys then aggregates. The same expressions are reused for sorting.
        var groupType = typeof(IGrouping<,>).MakeGenericType(keyType, typeof(T));
        var group = Expression.Parameter(groupType, "g");
        var key = Expression.Property(group, "Key");
        var cells = new List<Expression>();
        for (var i = 0; i < keyBodies.Count; i++)
            cells.Add(Expression.Property(key, $"K{i + 1}"));

        foreach (var measureRef in query.Measures)
        {
            var field = RequireKind(metadata.GetField(measureRef.Measure), FieldKind.Measure);
            var aggregation = measureRef.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum;
            cells.Add(Aggregate(group, selectors[field.Name], aggregation));
            var caption = measureRef.Aggregation is null ? field.Caption : $"{field.Caption} ({Aggregations.Caption(aggregation)})";
            columns.Add(new ResultColumn(measureRef.Key, caption, FieldKind.Measure, DataType.Number, field.Format));
        }

        var grouped = CallQueryable(filtered, nameof(Queryable.GroupBy), [typeof(T), keyType], keySelector);
        var ordered = ApplySort(grouped, groupType, group, columns, cells, query);
        if (query.Top is { } top)
        {
            ordered = ordered.Provider.CreateQuery(Expression.Call(
                typeof(Queryable), nameof(Queryable.Take), [groupType], ordered.Expression, Expression.Constant(top)));
        }
        var rowSelector = Expression.Lambda(
            Expression.NewArrayInit(typeof(object), cells.Select(c => Expression.Convert(c, typeof(object)))), group);
        var projected = (IQueryable<object?[]>)CallQueryable(ordered, nameof(Queryable.Select), [groupType, typeof(object[])], rowSelector);

        var rows = await ToListAsync(projected, cancellationToken).ConfigureAwait(false);
        ToDatePeriods(rows, query.GroupBy);
        return new ResultSet(columns, rows);
    }

    public static Task<IReadOnlyList<object?>> DistinctValuesAsync(
        IQueryable<T> source, LambdaExpression selector, CancellationToken cancellationToken) =>
        (Task<IReadOnlyList<object?>>)Invoke(nameof(DistinctAsync), [selector.ReturnType], source, selector, cancellationToken);

    private static async Task<IReadOnlyList<object?>> DistinctAsync<TValue>(
        IQueryable<T> source, Expression<Func<T, TValue>> selector, CancellationToken cancellationToken)
    {
        var values = Cancellable(source, cancellationToken).Select(selector).Distinct().OrderBy(v => v).Take(MaxDistinctValues);
        return (await ToListAsync(values, cancellationToken).ConfigureAwait(false)).Select(v => (object?)v).ToList();
    }

    /// <summary>
    /// Materializes the query: asynchronously when the provider supports it (EF Core implements
    /// IAsyncEnumerable), otherwise checking for cancellation on every row.
    /// </summary>
    private static async Task<List<TRow>> ToListAsync<TRow>(IQueryable<TRow> query, CancellationToken cancellationToken)
    {
        var rows = new List<TRow>();
        if (query is IAsyncEnumerable<TRow> asyncRows)
        {
            await foreach (var row in asyncRows.WithCancellation(cancellationToken).ConfigureAwait(false))
                rows.Add(row);
            return rows;
        }
        foreach (var row in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>In memory, GroupBy consumes the whole source at once: inject a cancellation check into it.</summary>
    private static IQueryable<T> Cancellable(IQueryable<T> source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return source is EnumerableQuery && cancellationToken.CanBeCanceled
            ? WithCancellation(source, cancellationToken).AsQueryable()
            : source;
    }

    private static IEnumerable<T> WithCancellation(IEnumerable<T> source, CancellationToken cancellationToken)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private static IQueryable ApplySort(
        IQueryable grouped,
        Type groupType,
        ParameterExpression group,
        List<ResultColumn> columns,
        List<Expression> cells,
        AnalyticQuery query)
    {
        var sorts = query.Sort is { Count: > 0 } ? query.Sort : query.GroupBy.Select(g => new SortSpec(g.Key)).ToList();
        var result = grouped;
        var first = true;
        foreach (var sort in sorts)
        {
            var index = columns.FindIndex(c => c.Key == sort.Key);
            if (index < 0) throw new KeyNotFoundException($"Sort on a missing column: '{sort.Key}'.");
            var method = (first, sort.Descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending),
            };
            result = CallQueryable(result, method, [groupType, cells[index].Type], Expression.Lambda(cells[index], group));
            first = false;
        }
        return result;
    }

    private static IQueryable CallQueryable(IQueryable source, string method, Type[] typeArguments, LambdaExpression lambda) =>
        source.Provider.CreateQuery(Expression.Call(
            typeof(Queryable), method, typeArguments, source.Expression, Expression.Quote(lambda)));

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
            var selector = ApplyGrain(field, Rebind(selectors[field.Name], parameter), filter.Grain);
            var valueType = selector.Type;
            var acceptsNull = !valueType.IsValueType || Nullable.GetUnderlyingType(valueType) is not null;
            var values = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(valueType))!;
            foreach (var text in filter.Values)
            {
                var value = InvariantValue.Parse(text, valueType);
                if (value is not null || acceptsNull) values.Add(value);
            }

            Expression body = Expression.Call(
                typeof(Enumerable), nameof(Enumerable.Contains), [valueType],
                Expression.Constant(values), selector);
            if (filter.Operator == FilterOperator.NotIn)
                body = Expression.Not(body);
            source = source.Where(Expression.Lambda<Func<T, bool>>(body, parameter));
        }
        return source;
    }

    private static Expression ApplyGrain(FieldDescriptor field, Expression body, DateGrain grain)
    {
        if (grain == DateGrain.None) return body;
        if (field.DataType != DataType.Date)
            throw new InvalidOperationException($"Granularity '{grain}' requires a date dimension ('{field.Name}').");
        return DateGrains.Apply(body, grain);
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
            // Specialized numeric overloads (Sum(Func<T, decimal>), ...)
            return Expression.Call(typeof(Enumerable), method, [typeof(T)], group, selector);
        }
        catch (InvalidOperationException) when (aggregation is Aggregation.Min or Aggregation.Max)
        {
            // Generic Min/Max (dates, etc.)
            return Expression.Call(typeof(Enumerable), method, [typeof(T), selector.ReturnType], group, selector);
        }
    }

    /// <summary>Integer granularity keys become <see cref="DatePeriod"/> values (label + filter key).</summary>
    private static void ToDatePeriods(List<object?[]> rows, IReadOnlyList<FieldRef> groupBy)
    {
        for (var i = 0; i < groupBy.Count; i++)
        {
            var grain = groupBy[i].Grain;
            if (grain is DateGrain.None or DateGrain.Day) continue;
            foreach (var row in rows)
                row[i] = new DatePeriod(grain, (int)row[i]!);
        }
    }

    private static FieldDescriptor RequireKind(FieldDescriptor field, FieldKind kind) =>
        field.Kind == kind ? field : throw new InvalidOperationException($"'{field.Name}' is not a {(kind == FieldKind.Dimension ? "dimension" : "measure")}.");

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

// Grouping keys of 1 to 4 fields. Classes initialized by member, which providers such as EF Core translate into
// GROUP BY columns; LINQ to objects compares them by value.

internal sealed class GroupKey<T1> : IEquatable<GroupKey<T1>>
{
    public T1 K1 { get; set; } = default!;

    public bool Equals(GroupKey<T1>? other) => other is not null && EqualityComparer<T1>.Default.Equals(K1, other.K1);

    public override bool Equals(object? obj) => Equals(obj as GroupKey<T1>);

    public override int GetHashCode() => HashCode.Combine(K1);
}

internal sealed class GroupKey<T1, T2> : IEquatable<GroupKey<T1, T2>>
{
    public T1 K1 { get; set; } = default!;
    public T2 K2 { get; set; } = default!;

    public bool Equals(GroupKey<T1, T2>? other) =>
        other is not null
        && EqualityComparer<T1>.Default.Equals(K1, other.K1)
        && EqualityComparer<T2>.Default.Equals(K2, other.K2);

    public override bool Equals(object? obj) => Equals(obj as GroupKey<T1, T2>);

    public override int GetHashCode() => HashCode.Combine(K1, K2);
}

internal sealed class GroupKey<T1, T2, T3> : IEquatable<GroupKey<T1, T2, T3>>
{
    public T1 K1 { get; set; } = default!;
    public T2 K2 { get; set; } = default!;
    public T3 K3 { get; set; } = default!;

    public bool Equals(GroupKey<T1, T2, T3>? other) =>
        other is not null
        && EqualityComparer<T1>.Default.Equals(K1, other.K1)
        && EqualityComparer<T2>.Default.Equals(K2, other.K2)
        && EqualityComparer<T3>.Default.Equals(K3, other.K3);

    public override bool Equals(object? obj) => Equals(obj as GroupKey<T1, T2, T3>);

    public override int GetHashCode() => HashCode.Combine(K1, K2, K3);
}

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
