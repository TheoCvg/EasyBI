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
        // Filters on measures keep groups (HAVING), the others keep rows (WHERE)
        var measureFilters = (query.Filters ?? []).Where(f => NumericFilters.IsOnMeasure(f, metadata, query.Calculations)).ToList();
        var rowFilters = (query.Filters ?? []).Except(measureFilters);
        var filtered = ApplyFilters(Cancellable(source, cancellationToken), metadata, selectors, rowFilters, parameter);
        var columns = new List<ResultColumn>();

        // Grouping keys
        var keyBodies = new List<Expression>();
        foreach (var fieldRef in query.GroupBy)
        {
            var field = RequireKind(metadata.GetField(fieldRef.Field), FieldKind.Dimension);
            // Grouped by a set: true for its values, false for the others
            var body = fieldRef.Set is not null
                ? Predicate(metadata, selectors, new FilterSpec(field.Name, FilterOperator.In, fieldRef.SetMembers ?? [], fieldRef.Grain), parameter)
                : ApplyGrain(field, Rebind(selectors[field.Name], parameter), fieldRef.Grain);
            var dataType = (fieldRef.Set, fieldRef.Grain) switch
            {
                ({ }, _) => DataType.Boolean,
                (_, DateGrain.None) => field.DataType,
                (_, DateGrain.Day) => DataType.Date,
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

        var values = new GroupValues(group, metadata, selectors, query.Calculations ?? []);
        foreach (var measureRef in query.Measures)
        {
            var calculation = query.Calculations?.FirstOrDefault(c => c.Name == measureRef.Measure);
            var declared = metadata.Fields.FirstOrDefault(f => f.Name == measureRef.Measure);
            if (calculation is not null && (declared is null || !MeasureFormula.IsOnly(calculation.Formula, declared.Name)))
            {
                // The end user's formula: the model's measures aggregated as declared, functions over the group's rows
                cells.Add(values.Formula(calculation));
                var named = calculation.Caption is { Length: > 0 } own ? own : declared?.Caption ?? calculation.Name;
                columns.Add(new ResultColumn(measureRef.Key, named, FieldKind.Measure, DataType.Number, calculation.Format));
                continue;
            }
            // A measure of the model, possibly renamed and reformatted by the report
            var field = RequireKind(declared ?? metadata.GetField(measureRef.Measure), FieldKind.Measure);
            if (calculation is not null)
                field = field with { Caption = calculation.Caption is { Length: > 0 } own ? own : field.Caption, Format = calculation.Format };
            // A calculated measure is computed from the group's rows by the model: an aggregation saved with the visual does not apply
            var aggregation = field.IsCalculated ? null : measureRef.Aggregation;
            cells.Add(values.Declared(field.Name, aggregation));
            var caption = aggregation is { } chosen ? $"{field.Caption} ({Aggregations.Caption(chosen)})" : field.Caption;
            columns.Add(new ResultColumn(measureRef.Key, caption, FieldKind.Measure, DataType.Number, field.Format));
        }

        var grouped = CallQueryable(filtered, nameof(Queryable.GroupBy), [typeof(T), keyType], keySelector);
        foreach (var filter in measureFilters)
        {
            var cell = values.Measure(filter.Field);
            grouped = CallQueryable(grouped, nameof(Queryable.Where), [groupType], Expression.Lambda(Comparison(cell, filter), group));
        }
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

    public static Task<DistinctValues> DistinctValuesAsync(
        IQueryable<T> source, LambdaExpression selector, DistinctValuesRequest request, CancellationToken cancellationToken) =>
        (Task<DistinctValues>)Invoke(nameof(DistinctAsync), [selector.ReturnType], source, selector, request, cancellationToken);

    private static async Task<DistinctValues> DistinctAsync<TValue>(
        IQueryable<T> source, Expression<Func<T, TValue>> selector, DistinctValuesRequest request, CancellationToken cancellationToken)
    {
        var values = Cancellable(source, cancellationToken).Select(selector);
        if (typeof(TValue) == typeof(string) && request.Search?.Trim() is { Length: > 0 } search)
            values = values.Where(Containing<TValue>(search));
        var limit = Math.Max(0, request.Limit);
        // One more than the limit tells whether there are more
        var found = await ToListAsync(values.Distinct().OrderBy(v => v).Take(limit + 1), cancellationToken).ConfigureAwait(false);
        return new DistinctValues(found.Take(limit).Select(v => (object?)v).ToList(), found.Count > limit);
    }

    // value != null && value.ToLower().Contains("search"): translated by providers (LOWER and LIKE or INSTR in SQL)
    private static Expression<Func<TValue, bool>> Containing<TValue>(string search)
    {
        var value = Expression.Parameter(typeof(TValue), "value");
        var lowered = Expression.Call(value, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        var contains = Expression.Call(lowered, typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(search.ToLowerInvariant()));
        return Expression.Lambda<Func<TValue, bool>>(
            Expression.AndAlso(Expression.NotEqual(value, Expression.Constant(null, typeof(TValue))), contains), value);
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
        // The values of a set come before the others
        var sorts = query.Sort is { Count: > 0 } ? query.Sort : query.GroupBy.Select(g => new SortSpec(g.Key, Descending: g.Set is not null)).ToList();
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
            source = source.Where(Expression.Lambda<Func<T, bool>>(Predicate(metadata, selectors, filter, parameter), parameter));
        return source;
    }

    private static Expression Predicate(
        ModelMetadata metadata,
        IReadOnlyDictionary<string, LambdaExpression> selectors,
        FilterSpec filter,
        ParameterExpression parameter)
    {
        if (filter.Operator == FilterOperator.AnyOf)
        {
            // An empty group matches every row, no group matches none
            var groups = (filter.Alternatives ?? [])
                .Select(group => group.Count == 0
                    ? Expression.Constant(true)
                    : group.Select(f => Predicate(metadata, selectors, f, parameter)).Aggregate(Expression.AndAlso))
                .ToList();
            return groups.Count == 0 ? Expression.Constant(false) : groups.Aggregate(Expression.OrElse);
        }

        var field = RequireKind(metadata.GetField(filter.Field), FieldKind.Dimension);
        if (filter.Operator.IsPeriod())
            return InPeriod(field, Rebind(selectors[field.Name], parameter), filter);
        if (filter.Operator.IsComparison())
        {
            if (field.DataType != DataType.Number)
                throw new InvalidOperationException($"A comparison requires a number dimension ('{field.Name}').");
            return Comparison(Rebind(selectors[field.Name], parameter), filter);
        }

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
        return filter.Operator == FilterOperator.NotIn ? Expression.Not(body) : body;
    }

    /// <summary>
    /// The value compared with the filter's numbers, converted to its own type; a missing value never matches, and an
    /// unreadable number leaves that side open.
    /// </summary>
    private static Expression Comparison(Expression value, FilterSpec filter)
    {
        // An integer is compared as a decimal number, so that "> 2.5" does not become "> 2"
        var type = Nullable.GetUnderlyingType(value.Type) ?? value.Type;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            value = Expression.Convert(value, value.Type == type ? typeof(double) : typeof(double?));
        var (first, second) = NumericFilters.Bounds(filter);
        Expression? Side(double? bound, Func<Expression, Expression, BinaryExpression> compare) =>
            bound is { } number ? compare(value, Number(number, value.Type)) : null;
        var sides = filter.Operator switch
        {
            FilterOperator.GreaterThan => [Side(first, Expression.GreaterThan)],
            FilterOperator.GreaterThanOrEqual => [Side(first, Expression.GreaterThanOrEqual)],
            FilterOperator.LessThan => [Side(first, Expression.LessThan)],
            FilterOperator.LessThanOrEqual => [Side(first, Expression.LessThanOrEqual)],
            _ => new[] { Side(first, Expression.GreaterThanOrEqual), Side(second, Expression.LessThanOrEqual) },
        };
        return sides.OfType<Expression>().DefaultIfEmpty(Expression.Constant(true)).Aggregate(Expression.AndAlso);
    }

    private static ConstantExpression Number(double number, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return Expression.Constant(Convert.ChangeType(number, target, System.Globalization.CultureInfo.InvariantCulture), type);
    }

    /// <summary>
    /// date &gt;= start AND date &lt; end, resolved today. The date itself is compared, not a period key, so that a
    /// database can use an index on it.
    /// </summary>
    private static Expression InPeriod(FieldDescriptor field, Expression date, FilterSpec filter)
    {
        if (field.DataType != DataType.Date)
            throw new InvalidOperationException($"A period filter requires a date dimension ('{field.Name}').");
        var (start, end) = PeriodFilters.Range(filter, DateOnly.FromDateTime(DateTime.Today));
        Expression? body = null;
        if (start is { } from) body = Expression.GreaterThanOrEqual(date, Bound(from, date.Type));
        if (end is { } to)
        {
            var before = Expression.LessThan(date, Bound(to, date.Type));
            body = body is null ? before : Expression.AndAlso(body, before);
        }
        return body ?? Expression.Constant(true);
    }

    private static ConstantExpression Bound(DateTime day, Type dateType)
    {
        var type = Nullable.GetUnderlyingType(dateType) ?? dateType;
        // Boxed one by one: a conditional would turn the DateTime into a DateTimeOffset
        object value = type == typeof(DateOnly) ? (object)DateOnly.FromDateTime(day)
            : type == typeof(DateTimeOffset) ? (object)new DateTimeOffset(day)
            : day;
        return Expression.Constant(value, dateType);
    }

    private static Expression ApplyGrain(FieldDescriptor field, Expression body, DateGrain grain)
    {
        if (grain == DateGrain.None) return body;
        if (field.DataType != DataType.Date)
            throw new InvalidOperationException($"Granularity '{grain}' requires a date dimension ('{field.Name}').");
        return DateGrains.Apply(body, grain);
    }

    /// <summary>
    /// The value of a measure for a group, as the report defines it: the report's calculated measures and redefinitions
    /// are computed from their formulas, which may use other measures of the report. Under its own name, a measure being
    /// computed is the model's one (the redefinition of <c>[Amount]</c> reads the model's <c>[Amount]</c>), and the
    /// model's formulas read the report's measures too.
    /// </summary>
    private sealed class GroupValues(
        ParameterExpression group,
        ModelMetadata metadata,
        IReadOnlyDictionary<string, LambdaExpression> selectors,
        IReadOnlyList<CalculatedMeasure> calculations)
    {
        // The measures being computed, to read a measure's own name as the model's one and to stop a loop
        private readonly List<string> _computing = [];

        public Expression Measure(string name)
        {
            var calculation = calculations.FirstOrDefault(c => c.Name == name);
            var declared = metadata.Fields.Any(f => f.Name == name);
            if (calculation is null || (declared && (_computing.Contains(name) || MeasureFormula.IsOnly(calculation.Formula, name))))
                return Declared(name, null);
            if (_computing.Contains(name)) throw new InvalidOperationException($"The measure '{name}' uses itself.");
            return Formula(calculation);
        }

        public Expression Formula(CalculatedMeasure calculation) => Computing(calculation.Name, calculation.Formula);

        /// <summary>The model's measure: aggregated, or computed by the model.</summary>
        public Expression Declared(string name, Aggregation? aggregation)
        {
            var field = RequireKind(metadata.GetField(name), FieldKind.Measure);
            if (field.Formula is { } formula) return Computing(field.Name, formula);
            var selector = selectors[field.Name];
            return field.IsCalculated
                ? new ParameterReplacer(selector.Parameters[0], group).Visit(selector.Body)
                : Aggregate(group, selector, aggregation ?? field.DefaultAggregation ?? Aggregation.Sum);
        }

        private Expression Computing(string name, string formula)
        {
            _computing.Add(name);
            try
            {
                return MeasureFormula.Build(formula, group, typeof(T), Measure, row => selectors[metadata.GetField(row).Name]);
            }
            finally
            {
                _computing.RemoveAt(_computing.Count - 1);
            }
        }
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
            if (grain is DateGrain.None or DateGrain.Day || groupBy[i].Set is not null) continue;
            foreach (var row in rows)
                row[i] = row[i] is int key ? new DatePeriod(grain, key) : null;
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
