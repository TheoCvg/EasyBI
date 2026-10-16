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

    private QueryableSemanticModel<T> Add(FieldDescriptor field, LambdaExpression selector)
    {
        if (_selectors.ContainsKey(field.Name))
            throw new InvalidOperationException($"Field '{field.Name}' is already declared.");
        _fields.Add(field);
        _selectors[field.Name] = selector;
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
