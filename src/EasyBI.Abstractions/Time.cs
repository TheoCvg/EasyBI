using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>A unit of time an offset counts in.</summary>
public enum TimeUnit { Day, Week, Month, Quarter, Year }

/// <summary>The direction in which a change of a measure is good: up for revenue, down for costs.</summary>
public enum Favorable { Higher, Lower }

/// <summary>A move in time: months, then days (negative to go back).</summary>
public readonly record struct TimeShift(int Months, int Days = 0)
{
    /// <summary><paramref name="count"/> units (negative to go back): weeks are 7 days, quarters 3 months, years 12.</summary>
    public static TimeShift Of(int count, TimeUnit unit) => unit switch
    {
        TimeUnit.Day => new(0, count),
        TimeUnit.Week => new(0, 7 * count),
        TimeUnit.Month => new(count),
        TimeUnit.Quarter => new(3 * count),
        _ => new(12 * count),
    };

    public DateTime Apply(DateTime date) => date.AddMonths(Months).AddDays(Days);
}

/// <summary>
/// How a measure is seen through time (<see cref="MeasureRef.Time"/>): added up along the dates of the visual
/// (<see cref="Accumulation"/>), compared with another period (<see cref="Period"/>, shown as <see cref="Show"/>), or
/// both, the totals of both periods then being compared. Each part is the id of an entry of the application's catalog of
/// periods, presentations and accumulations (EasyBI.Core's <c>TimeCatalog</c>), so that an application adds its own.
/// </summary>
[JsonConverter(typeof(MeasureTimeJsonConverter))]
public sealed record MeasureTime
{
    /// <summary>The id of the accumulation: the values added up since the start of each period; null for none.</summary>
    public string? Accumulation { get; init; }

    /// <summary>The id of the period the measure is compared with; null for no comparison.</summary>
    public string? Period { get; init; }

    /// <summary>How many <see cref="Unit"/>s back, for a period that takes an offset (<c>offset</c>).</summary>
    public int Count { get; init; } = 1;

    /// <inheritdoc cref="Count"/>
    public TimeUnit Unit { get; init; } = TimeUnit.Year;

    /// <summary>The id of what the comparison shows: the compared value, the change, the change in %...</summary>
    public string Show { get; init; } = TimeIds.Value;

    /// <summary>Identifies the calculation in the keys of result columns.</summary>
    public string Key => string.Join(".", new[]
    {
        Accumulation,
        Period is null ? null : Count == 1 && Unit == TimeUnit.Year ? Period : $"{Period}({Count}{Unit})",
        Period is null ? null : Show,
    }.Where(p => p is not null));

    public static MeasureTime PreviousYear { get; } = new() { Period = TimeIds.LastYear };

    public static MeasureTime ChangeFromPreviousYear { get; } = new() { Period = TimeIds.LastYear, Show = TimeIds.Change };

    public static MeasureTime PercentChangeFromPreviousYear { get; } = new() { Period = TimeIds.LastYear, Show = TimeIds.PercentChange };

    public static MeasureTime YearToDate { get; } = new() { Accumulation = TimeIds.YearToDate };
}

/// <summary>The ids of EasyBI's own periods, presentations and accumulations (an application's ids have a prefix).</summary>
public static class TimeIds
{
    /// <summary>The period just before, as long as the visual's (a month before each month, the 30 days before the last 30).</summary>
    public const string PreviousPeriod = "previous";

    /// <summary>The same period a year earlier.</summary>
    public const string LastYear = "lastYear";

    /// <summary><see cref="MeasureTime.Count"/> <see cref="MeasureTime.Unit"/>s earlier.</summary>
    public const string Offset = "offset";

    public const string Value = "value";
    public const string Change = "change";
    public const string PercentChange = "percentChange";

    public const string YearToDate = "yearToDate";
    public const string QuarterToDate = "quarterToDate";
    public const string MonthToDate = "monthToDate";
    public const string Running = "running";
}

/// <summary>
/// What a period of comparison is worked out from: the granularity of the date on the visual's axis
/// (<see cref="DateGrain.None"/> without one) and the days the visual's date filters keep (null ends are open).
/// </summary>
public sealed record TimeComparisonContext(DateGrain AxisGrain, DateTime? From, DateTime? To, MeasureTime Time);

/// <summary>
/// A period a measure can be compared with, such as the same period a year earlier: how far its dates move back
/// (<see cref="Shift"/>, null when it cannot tell, the comparison then being empty).
/// </summary>
/// <param name="Id">Stored in the report: keep it stable. An application's has a prefix (<c>"retail.week364"</c>).</param>
/// <param name="Caption">Shown in the designer, read each time so that it follows the EasyBI language.</param>
/// <param name="Shift">How far back the period is, for a visual (null when it cannot tell).</param>
public sealed record ComparisonPeriod(string Id, Func<string> Caption, Func<TimeComparisonContext, TimeShift?> Shift)
{
    /// <summary>The period in a column's caption ("Y-1", "M-1"); <see cref="Caption"/> when null.</summary>
    public Func<TimeComparisonContext, string>? ShortCaption { get; init; }

    /// <summary>True when the user picks how far back (<see cref="MeasureTime.Count"/> and <see cref="MeasureTime.Unit"/>).</summary>
    public bool HasOffset { get; init; }
}

/// <summary>What a comparison shows from the value and the compared one, such as the change in %.</summary>
/// <param name="Id">Stored in the report: keep it stable. An application's has a prefix (<c>"acme.index"</c>).</param>
/// <param name="Caption">Shown in the designer, read each time so that it follows the EasyBI language.</param>
/// <param name="Compute">From the value and the compared one (null when missing).</param>
public sealed record ComparisonOutput(string Id, Func<string> Caption, Func<double?, double?, double?> Compute)
{
    /// <summary>
    /// The caption of the column: <c>{0}</c> the measure's, <c>{1}</c> the period's short caption ("{0} Δ% vs {1}");
    /// "{0} {1}" when null.
    /// </summary>
    public Func<string>? ColumnCaption { get; init; }

    /// <summary>.NET format of the values ("P1"); null keeps the measure's.</summary>
    public string? Format { get; init; }

    /// <summary>True when the values are changes: a KPI shows their trend, in a color telling whether it is good.</summary>
    public bool IsChange { get; init; }

    /// <summary>What stands for it on a chip of the designer, before the period ("Δ%"); nothing when null.</summary>
    public string? Symbol { get; init; }
}

/// <summary>Values added up along the dates of the visual, starting again at <see cref="Start"/> of each period.</summary>
/// <param name="Id">Stored in the report: keep it stable. An application's has a prefix (<c>"acme.fiscalYear"</c>).</param>
/// <param name="Caption">Shown in the designer, read each time so that it follows the EasyBI language.</param>
/// <param name="Start">The start of the period a date belongs to (<see cref="DateTime.MinValue"/> never starts again).</param>
public sealed record Accumulation(string Id, Func<string> Caption, Func<DateTime, DateTime> Start)
{
    /// <summary>The caption of the column, <c>{0}</c> the measure's ("{0} YTD"); "{0}, Caption" when null.</summary>
    public Func<string>? ColumnCaption { get; init; }
}

/// <summary>
/// Reads a <see cref="MeasureTime"/> as an object, or as the name of a calculation of the reports written before
/// time calculations were made of catalogs (<c>"time": "PreviousYear"</c>).
/// </summary>
public sealed class MeasureTimeJsonConverter : JsonConverter<MeasureTime>
{
    public override MeasureTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() switch
            {
                "PreviousYear" => MeasureTime.PreviousYear,
                "ChangeFromPreviousYear" => MeasureTime.ChangeFromPreviousYear,
                "PercentChangeFromPreviousYear" => MeasureTime.PercentChangeFromPreviousYear,
                "YearToDate" => MeasureTime.YearToDate,
                _ => null,
            };
        var data = JsonSerializer.Deserialize<Data>(ref reader, options) ?? new();
        return new MeasureTime
        {
            Accumulation = data.Accumulation,
            Period = data.Period,
            Count = data.Count ?? 1,
            Unit = data.Unit ?? TimeUnit.Year,
            Show = data.Show ?? TimeIds.Value,
        };
    }

    public override void Write(Utf8JsonWriter writer, MeasureTime value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Data
        {
            Accumulation = value.Accumulation,
            Period = value.Period,
            // Only what the calculation uses
            Count = value.Period is not null && (value.Count != 1 || value.Unit != TimeUnit.Year) ? value.Count : null,
            Unit = value.Period is not null && (value.Count != 1 || value.Unit != TimeUnit.Year) ? value.Unit : null,
            Show = value.Period is not null && value.Show != TimeIds.Value ? value.Show : null,
        }, options);

    private sealed class Data
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Accumulation { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Period { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Count { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeUnit? Unit { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Show { get; set; }
    }
}
