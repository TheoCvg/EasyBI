namespace EasyBI.Abstractions;

/// <summary>How a dashboard row lays out slots that do not fit its width.</summary>
public enum RowOverflow
{
    /// <summary>Slots share the row width in proportion to their <see cref="LayoutSlot.Width"/>.</summary>
    Fit,

    /// <summary>Slots keep their width; the row scrolls horizontally.</summary>
    Scroll,

    /// <summary>Slots keep their width and wrap onto additional lines.</summary>
    Wrap,
}

/// <summary>Arrangement of a report's visuals on a dashboard: rows of slots.</summary>
public sealed record DashboardLayout
{
    public IReadOnlyList<LayoutRow> Rows { get; init; } = [];
}

/// <summary>A dashboard row.</summary>
public sealed record LayoutRow
{
    public const double DefaultHeight = 320;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Height of the row (of each line when it wraps), in device-independent pixels.</summary>
    public double Height { get; init; } = DefaultHeight;

    public RowOverflow Overflow { get; init; } = RowOverflow.Fit;

    public IReadOnlyList<LayoutSlot> Slots { get; init; } = [];
}

/// <summary>A place for one visual in a row; empty while <see cref="VisualId"/> is null.</summary>
public sealed record LayoutSlot
{
    public const double DefaultWidth = 480;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Id of the displayed <see cref="VisualDefinition"/>, or null for an empty slot.</summary>
    public string? VisualId { get; init; }

    /// <summary>
    /// Preferred width in device-independent pixels. <see cref="RowOverflow.Fit"/> rows use it as a
    /// proportion of the row width; <see cref="RowOverflow.Scroll"/> and <see cref="RowOverflow.Wrap"/> rows as is.
    /// </summary>
    public double Width { get; init; } = DefaultWidth;
}
