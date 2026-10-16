namespace EasyBI.Abstractions;

public enum MarkType { Bar, Line, Pie, Kpi, Table }

/// <summary>
/// Definition of a visual, independent of any charting library.
/// This is what the user builds in the designer and what is saved as JSON.
/// </summary>
public sealed record VisualDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; init; } = "";

    /// <summary>Name of the queried semantic model.</summary>
    public required string Model { get; init; }

    public MarkType Mark { get; init; } = MarkType.Bar;

    /// <summary>Categories (X axis of a chart, rows of a table).</summary>
    public IReadOnlyList<FieldRef> Axis { get; init; } = [];

    /// <summary>Dimension that splits the values into several series.</summary>
    public FieldRef? Legend { get; init; }

    public IReadOnlyList<MeasureRef> Values { get; init; } = [];

    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    public SortSpec? Sort { get; init; }

    public int? Top { get; init; }

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

    public string Title { get; init; } = "Report";

    public IReadOnlyList<VisualDefinition> Visuals { get; init; } = [];

    /// <summary>Global filters (slicers) applied to every visual of the same model.</summary>
    public IReadOnlyList<ReportFilter> Filters { get; init; } = [];

    /// <summary>Dashboard arrangement of the visuals; null when the report has none (a default one is derived).</summary>
    public DashboardLayout? Layout { get; init; }
}
