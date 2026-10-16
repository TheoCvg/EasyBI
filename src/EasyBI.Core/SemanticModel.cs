using System.Linq.Expressions;
using EasyBI.Abstractions;

namespace EasyBI.Core;

public static class SemanticModel
{
    /// <summary>Crée un modèle sur une source IQueryable (EF Core, LINQ to objects...).</summary>
    public static QueryableSemanticModel<T> For<T>(IQueryable<T> source, string name, string? caption = null) =>
        new(source, name, caption ?? name);

    public static QueryableSemanticModel<T> For<T>(IEnumerable<T> source, string name, string? caption = null) =>
        For(source.AsQueryable(), name, caption);
}

/// <summary>
/// Modèle sémantique déclaré en code par le développeur : il expose des dimensions et des mesures
/// sous forme d'expressions, que le moteur compose ensuite en GroupBy/Select.
/// </summary>
public sealed class QueryableSemanticModel<T> : ISemanticModel
{
    private readonly IQueryable<T> _source;
    private readonly string _name;
    private readonly string _caption;
    private readonly List<FieldDescriptor> _fields = [];
    private readonly Dictionary<string, LambdaExpression> _selectors = [];
    private ModelMetadata? _metadata;

    internal QueryableSemanticModel(IQueryable<T> source, string name, string caption)
    {
        _source = source;
        _name = name;
        _caption = caption;
    }

    public ModelMetadata Metadata => _metadata ??= new ModelMetadata(_name, _caption, _fields.ToArray());

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

    /// <summary>Mesure qui compte les lignes.</summary>
    public QueryableSemanticModel<T> Count(string caption, string name = "Count") =>
        Add(new FieldDescriptor(name, caption, FieldKind.Measure, DataType.Number, Aggregation.Count, "N0"),
            (Expression<Func<T, int>>)(_ => 1));

    public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default) =>
        Task.Run(() => QueryEngine<T>.Execute(_source, Metadata, _selectors, query), cancellationToken);

    public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
        Task.Run(() => QueryEngine<T>.DistinctValues(_source, _selectors[Metadata.GetField(field).Name]), cancellationToken);

    private QueryableSemanticModel<T> Add(FieldDescriptor field, LambdaExpression selector)
    {
        if (_selectors.ContainsKey(field.Name))
            throw new InvalidOperationException($"Le champ '{field.Name}' est déjà déclaré.");
        _fields.Add(field);
        _selectors[field.Name] = selector;
        _metadata = null;
        return this;
    }

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
            throw new ArgumentException("Impossible de déduire le nom du champ : précisez le paramètre 'name'.", nameof(selector));
        return string.Join('.', parts);
    }
}
