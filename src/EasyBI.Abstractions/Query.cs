using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>Reference to a dimension, with an optional granularity for dates.</summary>
public sealed record FieldRef(string Field, DateGrain Grain = DateGrain.None)
{
    /// <summary>Name of the column produced in the <see cref="ResultSet"/>.</summary>
    public string Key => Grain == DateGrain.None ? Field : $"{Field}.{Grain}";
}

/// <summary>Reference to a measure; the model's default aggregation can be overridden.</summary>
public sealed record MeasureRef(string Measure, Aggregation? Aggregation = null)
{
    public string Key => Aggregation is null ? Measure : $"{Measure}.{Aggregation}";

    /// <summary>
    /// Chart type of this measure's series in a cartesian chart (bars, stacked bars, line, area, points); null follows
    /// the visual's <see cref="VisualDefinition.Mark"/>. Display only: it does not change the query.
    /// </summary>
    public MarkType? Mark { get; init; }

    /// <summary>Draws this measure's series against a second value axis, on the right. Display only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] // reports only mention it when it is used
    public bool SecondaryAxis { get; init; }
}

public enum FilterOperator { In, NotIn }

/// <summary>
/// Filter on a dimension. Values are stored in invariant form so they stay serializable
/// (<c>null</c> = missing value). With a granularity, the date is first reduced to its period
/// (<see cref="DatePeriod.Key"/>: 2024, 20241, 202403...) before the comparison.
/// </summary>
public sealed record FilterSpec(string Field, FilterOperator Operator, IReadOnlyList<string?> Values, DateGrain Grain = DateGrain.None);

public sealed record SortSpec(string Key, bool Descending = false);

/// <summary>
/// Abstract analytic query: "group by ... and aggregate ...".
/// It knows neither the data source nor the rendering.
/// </summary>
public sealed record AnalyticQuery(
    IReadOnlyList<FieldRef> GroupBy,
    IReadOnlyList<MeasureRef> Measures,
    IReadOnlyList<FilterSpec>? Filters = null,
    IReadOnlyList<SortSpec>? Sort = null,
    int? Top = null);
