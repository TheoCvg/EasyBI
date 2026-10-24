using System.Globalization;
using System.Linq.Expressions;
using System.Resources;
using EasyBI.Abstractions;

namespace EasyBI.Core;

public static class SemanticModel
{
    /// <summary>
    /// Creates a model over a shared IQueryable source. Except for LINQ to objects, queries run
    /// one after another: an EF Core DbContext does not support concurrent operations.
    /// </summary>
    public static QueryableSemanticModel<T> For<T>(IQueryable<T> source, string name, string? caption = null) =>
        new(() => new SourceLease<T>(source, null), serialize: source is not EnumerableQuery, name, caption ?? name);

    public static QueryableSemanticModel<T> For<T>(IEnumerable<T> source, string name, string? caption = null) =>
        For(source.AsQueryable(), name, caption);

    /// <summary>
    /// Creates a model that opens a dedicated context for each query (typically
    /// <c>IDbContextFactory.CreateDbContext</c>): the visuals of a dashboard run in parallel.
    /// </summary>
    public static QueryableSemanticModel<T> For<TContext, T>(
        Func<TContext> createContext,
        Func<TContext, IQueryable<T>> query,
        string name,
        string? caption = null)
        where TContext : IDisposable =>
        new(() =>
        {
            var context = createContext();
            return new SourceLease<T>(query(context), context);
        }, serialize: false, name, caption ?? name);
}

/// <summary>Source lent for the duration of a query, with the context to dispose afterwards.</summary>
internal readonly record struct SourceLease<T>(IQueryable<T> Source, IDisposable? Owner) : IDisposable
{
    public void Dispose() => Owner?.Dispose();
}

/// <summary>
/// Semantic model declared in code by the developer: it exposes dimensions and measures
/// as expressions, which the engine then composes into GroupBy/Select.
/// </summary>
public sealed class QueryableSemanticModel<T> : ISemanticModel
{
    private readonly Func<SourceLease<T>> _acquire;
    private readonly SemaphoreSlim? _gate;
    private readonly string _name;
    private readonly string _caption;
    private readonly List<FieldDescriptor> _fields = [];
    private readonly Dictionary<string, LambdaExpression> _selectors = [];
    private ResourceManager? _resources;
    private LocalizedMetadata? _metadata;

    internal QueryableSemanticModel(Func<SourceLease<T>> acquire, bool serialize, string name, string caption)
    {
        _acquire = acquire;
        _gate = serialize ? new SemaphoreSlim(1, 1) : null;
        _name = name;
        _caption = caption;
    }

    /// <summary>Metadata in the current EasyBI language (captions are translated when <see cref="Localize"/> was called).</summary>
    public ModelMetadata Metadata
    {
        get
        {
            var culture = EasyBILocalization.UICulture;
            if (_metadata is { } cached && (_resources is null || cached.Culture.Equals(culture)))
                return cached.Metadata;
            var metadata = new ModelMetadata(
                _name,
                Translate(_caption, culture),
                _fields.Select(f => f with { Caption = Translate(f.Caption, culture) }).ToArray());
            _metadata = new LocalizedMetadata(culture, metadata);
            return metadata;
        }
    }

    /// <summary>
    /// Translates the model's captions: every caption given to <see cref="SemanticModel.For{T}(IQueryable{T}, string, string?)"/>,
    /// <see cref="Dimension{TValue}"/>, <see cref="Measure{TValue}"/> and <see cref="Count"/> becomes a key looked up
    /// in <paramref name="resources"/> for the current EasyBI language; a missing key falls back to the caption itself.
    /// </summary>
    public QueryableSemanticModel<T> Localize(ResourceManager resources)
    {
        _resources = resources;
        _metadata = null;
        return this;
    }

    public QueryableSemanticModel<T> Dimension<TValue>(Expression<Func<T, TValue>> selector, string caption, string? name = null)
    {
        var dataType = TypeMapping.ToDataType(typeof(TValue));
        return Add(new FieldDescriptor(name ?? NameOf(selector), caption, FieldKind.Dimension, dataType), selector);
    }

    public QueryableSemanticModel<T> Measure<TValue>(
        Expression<Func<T, TValue>> selector,
        string caption,
        Aggregation aggregation = Aggregation.Sum,
        string? format = null,
        string? name = null)
    {
        return Add(new FieldDescriptor(name ?? NameOf(selector), caption, FieldKind.Measure, DataType.Number, aggregation, format), selector);
    }

    /// <summary>
    /// Measure computed from the rows of each group, such as <c>g => g.Sum(s => s.Margin) - g.Sum(s => s.Cost)</c>. The
    /// expression runs inside the query (in SQL with EF Core), so sorting and Top apply to it; the end user cannot change
    /// its aggregation. Guard divisions (or use <see cref="Ratio{TNumerator, TDenominator}"/>): some databases fail on a
    /// division by zero.
    /// </summary>
    public QueryableSemanticModel<T> Calculated<TValue>(
        Expression<Func<IEnumerable<T>, TValue>> aggregate,
        string caption,
        string name,
        string? format = null) =>
        Add(new FieldDescriptor(name, caption, FieldKind.Measure, DataType.Number, Format: format, IsCalculated: true), aggregate);

    /// <summary>
    /// Ratio of two sums for each group: margin rate (margin / revenue), average basket (revenue / <c>_ => 1</c>)...
    /// Summing before dividing gives the right ratio of a group, unlike averaging the rows' ratios. Empty when the
    /// denominator sums to zero.
    /// </summary>
    public QueryableSemanticModel<T> Ratio<TNumerator, TDenominator>(
        Expression<Func<T, TNumerator>> numerator,
        Expression<Func<T, TDenominator>> denominator,
        string caption,
        string name,
        string? format = null)
    {
        var rows = Expression.Parameter(typeof(IEnumerable<T>), "g");
        Expression Sum(LambdaExpression selector) => Expression.Convert(
            Expression.Call(typeof(Enumerable), nameof(Enumerable.Sum), [typeof(T)], rows, selector), typeof(double?));
        var (top, bottom) = (Sum(numerator), Sum(denominator));
        var ratio = Expression.Condition(
            Expression.Equal(bottom, Expression.Constant(0d, typeof(double?))),
            Expression.Constant(null, typeof(double?)),
            Expression.Divide(top, bottom));
        return Calculated(Expression.Lambda<Func<IEnumerable<T>, double?>>(ratio, rows), caption, name, format);
    }

    /// <summary>
    /// Measure computed by a formula, written as the end user writes them (<see cref="MeasureFormula"/>) with the names
    /// of the fields declared before it: <c>"[Margin] / [Amount]"</c>, <c>"SUM([Amount]; [Channel] = \"Web\")"</c>. It
    /// runs inside the query like <see cref="Calculated{TValue}"/>, and the end user can read and rewrite it in the
    /// application (the rewritten formula is saved with the report).
    /// </summary>
    /// <exception cref="ArgumentException">The formula is not valid for the fields declared so far.</exception>
    public QueryableSemanticModel<T> Formula(string formula, string caption, string name, string? format = null)
    {
        if (MeasureFormula.Validate(formula, new ModelMetadata(_name, _caption, _fields)) is { } problem)
            throw new ArgumentException($"Invalid formula for '{name}': {problem}", nameof(formula));
        return Add(new FieldDescriptor(name, caption, FieldKind.Measure, DataType.Number, Format: format, IsCalculated: true, Formula: formula), null);
    }

    /// <summary>Measure that counts rows.</summary>
    public QueryableSemanticModel<T> Count(string caption, string name = "Count") =>
        Add(new FieldDescriptor(name, caption, FieldKind.Measure, DataType.Number, Aggregation.Count, "N0"),
            (Expression<Func<T, int>>)(_ => 1));

    public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default) =>
        RunAsync(source => QueryEngine<T>.ExecuteAsync(source, Metadata, _selectors, query, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
        RunAsync(source => QueryEngine<T>.DistinctValuesAsync(source, _selectors[Metadata.GetField(field).Name], cancellationToken), cancellationToken);

    private Task<TResult> RunAsync<TResult>(Func<IQueryable<T>, Task<TResult>> run, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            if (_gate is not null) await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var lease = _acquire();
                return await run(lease.Source).ConfigureAwait(false);
            }
            finally
            {
                _gate?.Release();
            }
        }, cancellationToken);

    // A formula measure has no selector: the engine builds it from its formula
    private QueryableSemanticModel<T> Add(FieldDescriptor field, LambdaExpression? selector)
    {
        if (_fields.Any(f => f.Name == field.Name))
            throw new InvalidOperationException($"Field '{field.Name}' is already declared.");
        _fields.Add(field);
        if (selector is not null) _selectors[field.Name] = selector;
        _metadata = null;
        return this;
    }

    private string Translate(string caption, CultureInfo culture) =>
        _resources?.GetString(caption, culture) ?? caption;

    private sealed record LocalizedMetadata(CultureInfo Culture, ModelMetadata Metadata);

    private static string NameOf(LambdaExpression selector)
    {
        var parts = new Stack<string>();
        var current = selector.Body;
        while (current is MemberExpression member)
        {
            parts.Push(member.Member.Name);
            current = member.Expression;
        }
        if (current is not ParameterExpression || parts.Count == 0)
            throw new ArgumentException("Cannot infer the field name: specify the 'name' parameter.", nameof(selector));
        return string.Join('.', parts);
    }
}
