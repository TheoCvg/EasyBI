namespace EasyBI.Abstractions;

/// <summary>Analytical role of a field: an analysis axis or an aggregated value.</summary>
public enum FieldKind { Dimension, Measure }

public enum DataType { Text, Number, Date, Boolean }

public enum Aggregation { Sum, Count, Average, Min, Max }

/// <summary>Granularity applied to a date dimension.</summary>
public enum DateGrain { None, Year, Quarter, Month, Day }

/// <summary>
/// Description of a field exposed to the end user. A calculated measure (a ratio...) is computed from the rows of each
/// group by the model: it has no aggregation to choose. <paramref name="Formula"/> is the formula of a measure the
/// developer declared as one (with field names, as in <see cref="CalculatedMeasure.Formula"/>), which the end user can
/// read and rewrite.
/// </summary>
public sealed record FieldDescriptor(
    string Name,
    string Caption,
    FieldKind Kind,
    DataType DataType,
    Aggregation? DefaultAggregation = null,
    string? Format = null,
    bool IsCalculated = false,
    string? Formula = null);

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
/// A data source of visuals, supplied by the developer: what can be analyzed (<see cref="Metadata"/>) and, for a
/// visual, its data. Where the data comes from and how it travels is the implementation's business: an
/// <c>IQueryable</c> or EF Core in the application (EasyBI.Core's <c>QueryableSemanticModel</c>), a server answering
/// with the visual's result, an OLAP cube... EasyBI.Core's <c>QuerySemanticModel</c> computes a visual's data from
/// <c>AnalyticQuery</c> results, for sources that only run queries.
/// </summary>
public interface ISemanticModel
{
    ModelMetadata Metadata { get; }

    /// <summary>The data of a visual: its result and, for a matrix, its pivoted cells and totals.</summary>
    Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default);

    /// <summary>Distinct values of a dimension, used by filters.</summary>
    Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default);
}

/// <summary>
/// What a visual asks its <see cref="ISemanticModel"/> for. It only holds data that serializes, so that it can travel
/// to a server: the visual with the filters of its page applied, and what its result depends on beside it.
/// </summary>
/// <param name="Visual">The visual, with the filters of its page and the selections of other visuals applied.</param>
public sealed record VisualDataRequest(VisualDefinition Visual)
{
    /// <summary>The end user's calculated measures the visual uses, with those they use.</summary>
    public IReadOnlyList<CalculatedMeasure> Measures { get; init; } = [];

    /// <summary>
    /// Whether the visual's type shows tooltips, whose measures are then queried with it. Its type may be one of the
    /// application's, which the source does not know.
    /// </summary>
    public bool HasTooltips { get; init; }

    /// <summary>
    /// The user's date of today, for relative periods (this year, last 12 months) and comparisons with a year earlier:
    /// only its date counts (a <see cref="DateTime"/> keeps this package free of a <c>DateOnly</c> polyfill).
    /// </summary>
    public DateTime Today { get; init; } = DateTime.Today;

    /// <summary>Name of the culture of the captions in the result ("fr-FR"); null for the source's own.</summary>
    public string? UICulture { get; init; }

    /// <summary>Name of the culture of the texts of values in the result (a matrix's members); null for the source's own.</summary>
    public string? FormatCulture { get; init; }
}

/// <summary>The data of a visual: its <see cref="ResultSet"/>, and the pivoted <see cref="MatrixData"/> of a matrix.</summary>
public sealed record VisualData(ResultSet Result)
{
    /// <summary>The pivoted data of a <see cref="VisualType.Matrix"/>; null for the other types.</summary>
    public MatrixData? Matrix { get; init; }
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
