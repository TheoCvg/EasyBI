using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>
/// What a visual shows: one of EasyBI's types, or a type an application declares, identified by its <see cref="Id"/>.
/// <see cref="Text"/> and <see cref="Image"/> carry their own content (<see cref="VisualDefinition.Text"/>,
/// <see cref="VisualDefinition.Image"/>) and query no model. The cartesian types (<see cref="Bar"/>,
/// <see cref="StackedBar"/>, <see cref="Line"/>, <see cref="Area"/>, <see cref="Scatter"/>) can be mixed in one chart, one
/// per measure (<see cref="MeasureRef.SeriesType"/>). A <see cref="Matrix"/> crosses its axis (rows) with its legend
/// (columns), with totals. A <see cref="Pie"/> is a full disc, a <see cref="Donut"/> a ring with the total in its center.
/// A <see cref="Gauge"/> shows one value on an arc between a minimum and a maximum, with an optional target
/// (<see cref="VisualDefinition.Gauge"/>). A <see cref="Waterfall"/> stacks each category's value on the running total,
/// up or down, and ends with the total.
/// </summary>
[JsonConverter(typeof(VisualTypeJsonConverter))]
public sealed record VisualType
{
    public VisualType(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>What identifies the type, in code and in the report's JSON.</summary>
    public string Id { get; }

    public static VisualType Bar { get; } = new("Bar");
    public static VisualType Line { get; } = new("Line");
    public static VisualType Pie { get; } = new("Pie");
    public static VisualType Kpi { get; } = new("Kpi");
    public static VisualType Table { get; } = new("Table");
    public static VisualType Text { get; } = new("Text");
    public static VisualType Image { get; } = new("Image");
    public static VisualType Area { get; } = new("Area");
    public static VisualType Scatter { get; } = new("Scatter");
    public static VisualType StackedBar { get; } = new("StackedBar");
    public static VisualType Matrix { get; } = new("Matrix");
    public static VisualType Donut { get; } = new("Donut");
    public static VisualType Gauge { get; } = new("Gauge");
    public static VisualType Waterfall { get; } = new("Waterfall");

    /// <summary>EasyBI's own types.</summary>
    public static IReadOnlyList<VisualType> BuiltIn { get; } =
        [Bar, Line, Pie, Kpi, Table, Text, Image, Area, Scatter, StackedBar, Matrix, Donut, Gauge, Waterfall];

    public bool IsBuiltIn => BuiltIn.Contains(this);

    /// <summary>The type with this id: EasyBI's own whatever the case (as the enum it replaces read it), otherwise a declared one.</summary>
    public static VisualType Parse(string id) =>
        BuiltIn.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new VisualType(id);

    public override string ToString() => Id;
}

/// <summary>Writes a <see cref="VisualType"/> as its id.</summary>
public sealed class VisualTypeJsonConverter : JsonConverter<VisualType>
{
    public override VisualType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && reader.GetString() is { Length: > 0 } id
            ? VisualType.Parse(id)
            : throw new JsonException("A visual type is written as a non-empty string.");

    public override void Write(Utf8JsonWriter writer, VisualType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Id);
}

/// <summary>
/// The scale of a <see cref="VisualType.Gauge"/>: its <see cref="Minimum"/> (null: 0), its <see cref="Maximum"/> (null: a
/// round number a little above the value and the target) and a <see cref="Target"/> marked on the arc.
/// </summary>
public sealed record GaugeSettings
{
    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Target { get; init; }
}

/// <summary>
/// Definition of a visual, independent of any charting library.
/// This is what the user builds in the designer and what is saved as JSON.
/// </summary>
public sealed record VisualDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; init; } = "";

    /// <summary>Name of the queried semantic model; empty for a <see cref="VisualType.Text"/> or <see cref="VisualType.Image"/> visual.</summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// Type of the visual. For a cartesian chart, it is also the type of the measures that do not set their own
    /// <see cref="MeasureRef.SeriesType"/>.
    /// </summary>
    public VisualType Type { get; init; } = VisualType.Bar;

    /// <summary>Categories (X axis of a chart, rows of a table).</summary>
    public IReadOnlyList<FieldRef> Axis { get; init; } = [];

    /// <summary>Dimension that splits the values into several series.</summary>
    public FieldRef? Legend { get; init; }

    public IReadOnlyList<MeasureRef> Values { get; init; } = [];

    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    public SortSpec? Sort { get; init; }

    public int? Top { get; init; }

    /// <summary>Content of a <see cref="VisualType.Text"/> visual.</summary>
    public TextContent? Text { get; init; }

    /// <summary>Content of a <see cref="VisualType.Image"/> visual.</summary>
    public ImageContent? Image { get; init; }

    /// <summary>Where the legend of a chart goes; <see cref="ChartLegend.Auto"/> (the default) follows the chart's size.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ChartLegend LegendPosition { get; init; }

    /// <summary>What the tooltips of a chart show; null for the defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TooltipSettings? Tooltip { get; init; }

    /// <summary>The scale and the target of a <see cref="VisualType.Gauge"/>; null for the other types or the defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GaugeSettings? Gauge { get; init; }

    /// <summary>Options specific to a given renderer (key = renderer name), so the contract does not limit them.</summary>
    public IReadOnlyDictionary<string, string>? Extensions { get; init; }
}

/// <summary>Report filter: applies to every visual querying the model <paramref name="Model"/>.</summary>
public sealed record ReportFilter(string Model, FilterSpec Filter);

/// <summary>A report = a set of visuals and global filters. The schema is versioned.</summary>
public sealed record ReportDefinition
{
    public const string CurrentSchema = "easybi/v1";

    public string Schema { get; init; } = CurrentSchema;

    public string Title { get; init; } = "Rapport";

    public IReadOnlyList<VisualDefinition> Visuals { get; init; } = [];

    /// <summary>Global filters (slicers) applied to every visual of the same model.</summary>
    public IReadOnlyList<ReportFilter> Filters { get; init; } = [];

    /// <summary>Measures the end user defined with a formula; any visual of their model can show them.</summary>
    public IReadOnlyList<CalculatedMeasure> Measures { get; init; } = [];
}

/// <summary>
/// A measure the end user defines with a formula over the fields of a model, such as <c>[Margin] / [Amount]</c> or
/// <c>SUM([Amount]; [Region] = "North")</c>: brackets hold field names, the operators are + - * / with parentheses and
/// numbers (with a decimal point). A measure on its own is aggregated as the model declares it; aggregation functions
/// (SUM, AVERAGE, MIN, MAX, COUNT, DISTINCTCOUNT) read measures and dimensions row by row, optionally on the rows
/// matching a condition. The formula is computed for each group; a division by zero gives an empty value. Visuals
/// reference it by <see cref="Name"/>, like a field of the model. See EasyBI.Core's <c>MeasureFormula</c>.
/// <para>
/// A measure named like a measure of its model redefines it in the report: its formula replaces the model's (where the
/// measure's own name still means the model's definition), and an empty <see cref="Caption"/> keeps the model's caption.
/// A formula that is only the measure itself, such as <c>[Amount]</c> for <c>Amount</c>, keeps the model's computation
/// and the aggregation chosen by each visual, and only changes the caption and the format.
/// </para>
/// </summary>
public sealed record CalculatedMeasure
{
    public string Name { get; init; } = "";

    public string Caption { get; init; } = "";

    public string Model { get; init; } = "";

    public string Formula { get; init; } = "";

    /// <summary>.NET format of the values ("P1", "N0"...); null for the default number format.</summary>
    public string? Format { get; init; }
}
