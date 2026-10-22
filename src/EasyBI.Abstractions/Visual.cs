namespace EasyBI.Abstractions;

/// <summary>
/// What a visual shows. <see cref="Text"/> and <see cref="Image"/> carry their own content
/// (<see cref="VisualDefinition.Text"/>, <see cref="VisualDefinition.Image"/>) and query no model.
/// The cartesian marks (<see cref="Bar"/>, <see cref="StackedBar"/>, <see cref="Line"/>, <see cref="Area"/>,
/// <see cref="Scatter"/>) can be mixed in one chart, one per measure (<see cref="MeasureRef.Mark"/>).
/// A <see cref="Matrix"/> crosses its axis (rows) with its legend (columns), with totals. A <see cref="Pie"/> is a full
/// disc, a <see cref="Donut"/> a ring with the total in its center. A <see cref="Gauge"/> shows one value on an arc
/// between a minimum and a maximum, with an optional target (<see cref="VisualDefinition.Gauge"/>). A
/// <see cref="Waterfall"/> stacks each category's value on the running total, up or down, and ends with the total.
/// </summary>
public enum MarkType { Bar, Line, Pie, Kpi, Table, Text, Image, Area, Scatter, StackedBar, Matrix, Donut, Gauge, Waterfall }

/// <summary>
/// The scale of a <see cref="MarkType.Gauge"/>: its <see cref="Minimum"/> (null: 0), its <see cref="Maximum"/> (null: a
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

    /// <summary>Name of the queried semantic model; empty for a <see cref="MarkType.Text"/> or <see cref="MarkType.Image"/> visual.</summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// Type of the visual. For a cartesian chart, it is also the type of the measures that do not set their own
    /// <see cref="MeasureRef.Mark"/>.
    /// </summary>
    public MarkType Mark { get; init; } = MarkType.Bar;

    /// <summary>Categories (X axis of a chart, rows of a table).</summary>
    public IReadOnlyList<FieldRef> Axis { get; init; } = [];

    /// <summary>Dimension that splits the values into several series.</summary>
    public FieldRef? Legend { get; init; }

    public IReadOnlyList<MeasureRef> Values { get; init; } = [];

    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    public SortSpec? Sort { get; init; }

    public int? Top { get; init; }

    /// <summary>Content of a <see cref="MarkType.Text"/> visual.</summary>
    public TextContent? Text { get; init; }

    /// <summary>Content of a <see cref="MarkType.Image"/> visual.</summary>
    public ImageContent? Image { get; init; }

    /// <summary>What the tooltips of a chart show; null for the defaults.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TooltipSettings? Tooltip { get; init; }

    /// <summary>The scale and the target of a <see cref="MarkType.Gauge"/>; null for the other types or the defaults.</summary>
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
/// A measure the end user defines with a formula over the measures of a model, such as <c>[Margin] / [Amount]</c>:
/// brackets hold field names, the operators are + - * / with parentheses and numbers (with a decimal point). Each
/// measure is aggregated as the model declares it, then the formula is computed for each group; a division by zero
/// gives an empty value. Visuals reference it by <see cref="Name"/>, like a field of the model.
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
