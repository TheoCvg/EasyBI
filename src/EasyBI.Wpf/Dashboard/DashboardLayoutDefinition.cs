using System.Text.Json.Serialization;

namespace EasyBI.Wpf.Dashboard;

/// <summary>How a dashboard band lays out its items.</summary>
public enum BandMode
{
    /// <summary>A grid of columns that fits the width; items can span several rows and columns.</summary>
    Fit,

    /// <summary>Items keep their width side by side; the band scrolls horizontally.</summary>
    Scroll,

    /// <summary>Items keep their width and wrap onto new lines.</summary>
    Wrap,
}

/// <summary>Where a new item goes relative to an existing one.</summary>
public enum ItemSide { Right, Below }

/// <summary>
/// Serializable arrangement of a <see cref="DashboardPanel"/>: a stack of bands, each with its own
/// <see cref="BandMode"/>. It belongs to the WPF layer: the report (visuals) never contains it, items only
/// reference visuals by id. Heights are counted in grid rows of <see cref="RowHeight"/>.
/// </summary>
public sealed record DashboardLayoutDefinition
{
    public const int DefaultColumns = 12;
    public const double DefaultRowHeight = 110;

    /// <summary>Number of columns of <see cref="BandMode.Fit"/> bands.</summary>
    public int Columns { get; init; } = DefaultColumns;

    /// <summary>Height of one grid row, in device-independent pixels.</summary>
    public double RowHeight { get; init; } = DefaultRowHeight;

    public IReadOnlyList<BandLayout> Bands { get; init; } = [];
}

/// <summary>A horizontal band of the dashboard.</summary>
public sealed record BandLayout
{
    /// <summary>Identity during layout operations; not saved.</summary>
    [JsonIgnore]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public BandMode Mode { get; init; } = BandMode.Fit;

    /// <summary>Items of the band; in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, in display order.</summary>
    public IReadOnlyList<ItemLayout> Items { get; init; } = [];
}

/// <summary>Place of one item. It shows the visual <see cref="VisualId"/>, or nothing (an empty placeholder).</summary>
public sealed record ItemLayout
{
    public const int DefaultColumnSpan = 6;
    public const int DefaultRowSpan = 3;
    public const double DefaultWidth = 360;

    /// <summary>Identity during layout operations; not saved.</summary>
    [JsonIgnore]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Id of the shown <see cref="EasyBI.Abstractions.VisualDefinition"/>; null for an empty item.</summary>
    public string? VisualId { get; init; }

    /// <summary>First column in a <see cref="BandMode.Fit"/> band (0-based).</summary>
    public int Column { get; init; }

    /// <summary>First row in a <see cref="BandMode.Fit"/> band (0-based).</summary>
    public int Row { get; init; }

    /// <summary>Columns covered in a <see cref="BandMode.Fit"/> band.</summary>
    public int ColumnSpan { get; init; } = DefaultColumnSpan;

    /// <summary>Height, in grid rows (every band mode).</summary>
    public int RowSpan { get; init; } = DefaultRowSpan;

    /// <summary>Width in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, in device-independent pixels.</summary>
    public double Width { get; init; } = DefaultWidth;

    /// <summary>The item takes the whole width of its band, alone on its line.</summary>
    public bool IsFullRow { get; init; }
}
