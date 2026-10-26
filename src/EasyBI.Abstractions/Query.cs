using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>Reference to a dimension, with an optional granularity for dates.</summary>
public sealed record FieldRef(string Field, DateGrain Grain = DateGrain.None)
{
    /// <summary>Name of the column produced in the <see cref="ResultSet"/>.</summary>
    public string Key => Grain == DateGrain.None ? Field : $"{Field}.{Grain}";

    /// <summary>
    /// Texts the visual shows instead of some values of the field ("Île-de-France" shown as "IDF"), keyed by the
    /// value's invariant text. Only what is shown changes: filters and selections keep the values themselves.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    // Labels compare by content: two references to the same field with the same texts are the same field
    public bool Equals(FieldRef? other) =>
        other is not null && Field == other.Field && Grain == other.Grain
        && (Labels?.Count ?? 0) == (other.Labels?.Count ?? 0)
        && (Labels ?? new Dictionary<string, string>()).All(l => other.Labels!.TryGetValue(l.Key, out var text) && text == l.Value);

    public override int GetHashCode() => HashCode.Combine(Field, Grain);
}

/// <summary>
/// A measure seen through time: its value, its value a year earlier, how it changed since, or its total since the start
/// of the year along a date axis.
/// </summary>
public enum TimeCalculation { None, PreviousYear, ChangeFromPreviousYear, PercentChangeFromPreviousYear, YearToDate }

/// <summary>Reference to a measure; the model's default aggregation can be overridden.</summary>
public sealed record MeasureRef(string Measure, Aggregation? Aggregation = null)
{
    public string Key => (Aggregation is null ? Measure : $"{Measure}.{Aggregation}") + (Time == TimeCalculation.None ? "" : $".{Time}");

    /// <summary>
    /// How the measure is seen through time (see <see cref="TimeCalculation"/>). A year earlier means the same groups and
    /// filters shifted by one year: it needs a date on the axis or a date filter, and is empty otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TimeCalculation Time { get; init; }

    /// <summary>
    /// Chart type of this measure's series in a cartesian chart (bars, stacked bars, line, area, points); null follows
    /// the visual's <see cref="VisualDefinition.Type"/>. Display only: it does not change the query.
    /// </summary>
    public VisualType? SeriesType { get; init; }

    /// <summary>Draws this measure's series against a second value axis, on the right. Display only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] // reports only mention it when it is used
    public bool SecondaryAxis { get; init; }

    /// <summary>How its values look in a table, a matrix or a KPI (fixed style and conditional formats). Display only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueFormat? Format { get; init; }
}

/// <summary>
/// How a filter compares. <see cref="In"/> and <see cref="NotIn"/> list values. The others filter a date dimension on
/// a period: <see cref="Between"/> two dates (values: from, to, as yyyy-MM-dd, inclusive, null for an open end), or a
/// period relative to today whose unit is the filter's granularity: the <see cref="Current"/> one, the <see cref="Last"/>
/// n ending with the current one (value: n), the n complete periods <see cref="Previous"/> to the current one (value: n).
/// <see cref="AnyOf"/> keeps the rows matching at least one of its <see cref="FilterSpec.Alternatives"/>.
/// The comparisons filter a number dimension, or a measure of a visual (after aggregation, for each group): greater or
/// less than a value (value: the number, invariant), or <see cref="Range"/> between two numbers (values: minimum and
/// maximum, inclusive, null for an open end).
/// </summary>
public enum FilterOperator { In, NotIn, Between, Current, Last, Previous, AnyOf, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Range }

/// <summary>
/// Filter on a dimension, or with a comparison on a measure of a visual. Values are stored in invariant form so they stay serializable
/// (<c>null</c> = missing value). With <see cref="FilterOperator.In"/> or <see cref="FilterOperator.NotIn"/> and a
/// granularity, the date is first reduced to its period (<see cref="DatePeriod.Key"/>: 2024, 20241, 202403...) before
/// the comparison; for a relative period, the granularity is its unit (12 last months: Last, ["12"], Month).
/// </summary>
public sealed record FilterSpec(string Field, FilterOperator Operator, IReadOnlyList<string?> Values, DateGrain Grain = DateGrain.None)
{
    /// <summary>
    /// For <see cref="FilterOperator.AnyOf"/>: groups of filters, a row is kept when it matches every filter of one of
    /// them, such as (Region = North and Channel = Web) or (Region = South). <see cref="Field"/> and
    /// <see cref="Values"/> are then unused (empty).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyList<FilterSpec>>? Alternatives { get; init; }

    /// <summary>A filter keeping the rows that match every filter of at least one group.</summary>
    public static FilterSpec AnyOf(IReadOnlyList<IReadOnlyList<FilterSpec>> alternatives) =>
        new("", FilterOperator.AnyOf, []) { Alternatives = alternatives };
}

public sealed record SortSpec(string Key, bool Descending = false);

/// <summary>
/// Abstract analytic query: "group by ... and aggregate ...".
/// It knows neither the data source nor the rendering. <paramref name="Calculations"/> defines the end user's
/// calculated measures that <paramref name="Measures"/> references.
/// </summary>
public sealed record AnalyticQuery(
    IReadOnlyList<FieldRef> GroupBy,
    IReadOnlyList<MeasureRef> Measures,
    IReadOnlyList<FilterSpec>? Filters = null,
    IReadOnlyList<SortSpec>? Sort = null,
    int? Top = null,
    IReadOnlyList<CalculatedMeasure>? Calculations = null);
