using System.Text.Json.Serialization;

namespace EasyBI.Wpf.Dashboard;

/// <summary>How a dashboard band lays out its items.</summary>
public enum BandMode
{
    /// <summary>
    /// A grid filling the width: lines stacked top to bottom, each split into items side by side or stacked (see
    /// <see cref="LayoutNode"/>). Items are greedy: without a constraint they share the room left, so there is no gap.
    /// </summary>
    Fit,

    /// <summary>Items side by side on one line, greedy down to a minimum width, below which the band scrolls horizontally.</summary>
    Scroll,

    /// <summary>Items side by side, greedy down to a minimum width, below which they wrap onto new lines.</summary>
    Wrap,
}

/// <summary>Where an item goes relative to an existing one.</summary>
public enum ItemSide { Right, Below, Left, Above }

/// <summary>How a container splits its room between its children.</summary>
public enum SplitDirection
{
    /// <summary>Children side by side, sharing the width.</summary>
    Row,

    /// <summary>Children stacked, sharing the height.</summary>
    Column,
}

/// <summary>
/// Serializable arrangement of a <see cref="DashboardPanel"/>: a stack of bands, each with its own
/// <see cref="BandMode"/>. It belongs to the WPF layer: the report (visuals) never contains it, items only
/// reference visuals by id. Widths in grid bands are counted in <see cref="Columns"/>, heights in rows of <see cref="RowHeight"/>.
/// </summary>
public sealed record DashboardLayoutDefinition
{
    public const int DefaultColumns = 12;
    public const double DefaultRowHeight = 110;

    /// <summary>Number of columns of <see cref="BandMode.Fit"/> bands: fixed widths are proportions of the band width.</summary>
    public int Columns { get; init; } = DefaultColumns;

    /// <summary>Height of one row, in device-independent pixels.</summary>
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

    /// <summary>
    /// In a <see cref="BandMode.Fit"/> band, its lines from top to bottom (an item, or a container splitting the line);
    /// in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, its items in display order.
    /// </summary>
    public IReadOnlyList<LayoutNode> Items { get; init; } = [];
}

/// <summary>
/// A node of a band: an item (showing the visual <see cref="VisualId"/>, or nothing for an empty item), or a
/// container (<see cref="Split"/>) sharing its room between its <see cref="Children"/>. Sizes are constraints: null
/// means greedy (the node shares what its siblings leave), a value fixes the node's share.
/// </summary>
public sealed record LayoutNode
{
    /// <summary>Height of an unconstrained item, in rows, when nothing next to it sets the height of its line.</summary>
    public const int DefaultRowSpan = 3;

    /// <summary>Smallest height of an unconstrained item sharing the height of a container, in rows.</summary>
    public const int MinRowSpan = 1;

    /// <summary>Identity during layout operations; not saved.</summary>
    [JsonIgnore]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Id of the shown <see cref="EasyBI.Abstractions.VisualDefinition"/>; null for an empty item or a container.</summary>
    public string? VisualId { get; init; }

    /// <summary>Null for an item; how a container splits its room.</summary>
    public SplitDirection? Split { get; init; }

    /// <summary>Children of a container.</summary>
    public IReadOnlyList<LayoutNode> Children { get; init; } = [];

    /// <summary>
    /// Fixed width in grid columns, for a node side by side with others (or alone on its line); null takes the
    /// columns its siblings leave. Inside a stack, the stack's width counts.
    /// </summary>
    public int? ColumnSpan { get; init; }

    /// <summary>
    /// Fixed height in rows: the share of a node in a stack, or the height of an item on its line (which then sets the
    /// line's height; shorter items next to it stretch). Null follows the line, or shares what its siblings leave.
    /// </summary>
    public int? RowSpan { get; init; }

    /// <summary>Fixed width in <see cref="BandMode.Scroll"/> and <see cref="BandMode.Wrap"/> bands, in device-independent pixels; null is greedy.</summary>
    public double? Width { get; init; }

    [JsonIgnore]
    public bool IsItem => Split is null;

    /// <summary>An item, empty or showing a visual.</summary>
    public static LayoutNode Item(string? visualId = null) => new() { VisualId = visualId };

    /// <summary>Items side by side.</summary>
    public static LayoutNode Row(params LayoutNode[] children) => new() { Split = SplitDirection.Row, Children = children };

    /// <summary>Items stacked.</summary>
    public static LayoutNode Column(params LayoutNode[] children) => new() { Split = SplitDirection.Column, Children = children };

    /// <summary>The items of this node, in reading order.</summary>
    public IEnumerable<LayoutNode> Items() => IsItem ? [this] : Children.SelectMany(c => c.Items());

    /// <summary>This node and all the nodes under it.</summary>
    public IEnumerable<LayoutNode> Descendants() => [this, .. Children.SelectMany(c => c.Descendants())];
}
