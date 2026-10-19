namespace EasyBI.Abstractions;

/// <summary>
/// What a visual shows. <see cref="Text"/> and <see cref="Image"/> carry their own content
/// (<see cref="VisualDefinition.Text"/>, <see cref="VisualDefinition.Image"/>) and query no model.
/// The cartesian marks (<see cref="Bar"/>, <see cref="StackedBar"/>, <see cref="Line"/>, <see cref="Area"/>,
/// <see cref="Scatter"/>) can be mixed in one chart, one per measure (<see cref="MeasureRef.Mark"/>).
/// </summary>
public enum MarkType { Bar, Line, Pie, Kpi, Table, Text, Image, Area, Scatter, StackedBar }

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
}
