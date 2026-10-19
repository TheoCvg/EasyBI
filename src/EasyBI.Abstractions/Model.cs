namespace EasyBI.Abstractions;

/// <summary>Analytical role of a field: an analysis axis or an aggregated value.</summary>
public enum FieldKind { Dimension, Measure }

public enum DataType { Text, Number, Date, Boolean }

public enum Aggregation { Sum, Count, Average, Min, Max }

/// <summary>Granularity applied to a date dimension.</summary>
public enum DateGrain { None, Year, Quarter, Month, Day }

/// <summary>
/// Description of a field exposed to the end user. A calculated measure (a ratio...) is computed from the rows of each
/// group by the model: it has no aggregation to choose.
/// </summary>
public sealed record FieldDescriptor(
    string Name,
    string Caption,
    FieldKind Kind,
    DataType DataType,
    Aggregation? DefaultAggregation = null,
    string? Format = null,
    bool IsCalculated = false);

/// <summary>Metadata of a semantic model: what the user is allowed to analyze.</summary>
public sealed record ModelMetadata(string Name, string Caption, IReadOnlyList<FieldDescriptor> Fields)
{
    public FieldDescriptor GetField(string name) =>
        Fields.FirstOrDefault(f => f.Name == name)
        ?? throw new KeyNotFoundException($"Unknown field '{name}' in model '{Name}'.");

    public IEnumerable<FieldDescriptor> Dimensions => Fields.Where(f => f.Kind == FieldKind.Dimension);

    public IEnumerable<FieldDescriptor> Measures => Fields.Where(f => f.Kind == FieldKind.Measure);
}

/// <summary>
/// Entry point supplied by a developer: metadata + the ability to execute an analytic query.
/// An implementation may target IQueryable, raw SQL, a REST API, an OLAP cube...
/// </summary>
public interface ISemanticModel
{
    ModelMetadata Metadata { get; }

    Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default);

    /// <summary>Distinct values of a dimension, used by filters.</summary>
    Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default);
}

/// <summary>
/// Period produced by a date granularity (year, quarter, month). The integer key is sortable
/// and used by filters; <see cref="ToString"/> returns a culture-neutral label ("2024 Q1"),
/// the localized label comes from EasyBI.Core's ValueFormatter.
/// </summary>
public readonly record struct DatePeriod(DateGrain Grain, int Key) : IComparable<DatePeriod>, IComparable
{
    public int CompareTo(DatePeriod other) => Key.CompareTo(other.Key);

    public int CompareTo(object? obj) => obj is DatePeriod other ? CompareTo(other) : 1;

    public override string ToString() => Grain switch
    {
        DateGrain.Quarter => $"{Key / 10} Q{Key % 10}",
        DateGrain.Month => $"{Key / 100}-{Key % 100:00}",
        _ => Key.ToString(),
    };
}
