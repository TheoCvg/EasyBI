using System.Windows;

namespace EasyBI.Wpf.Dashboard;

/// <summary>How an item dropped on a dashboard is placed.</summary>
public enum DashboardDropKind
{
    /// <summary>Next to a layout node (an item's edge, or the anchor of a free space).</summary>
    NextTo,

    /// <summary>As a new line of a grid band, before line <see cref="DashboardDropTarget.Index"/>.</summary>
    Line,

    /// <summary>At position <see cref="DashboardDropTarget.Index"/> of a scrolling or wrapping band.</summary>
    Flow,
}

/// <summary>
/// Where an item dropped on a <see cref="DashboardPanel"/> goes: found from a point by <see cref="Find"/>, then applied
/// to an item already in the panel (<see cref="Move"/>) or to a new one (<see cref="Add"/>). Targets are values: two
/// equal targets place the item at the same place.
/// </summary>
public sealed record DashboardDropTarget(DashboardDropKind Kind, int Band, int Index, string? AnchorId = null, ItemSide Side = ItemSide.Right)
{
    /// <summary>
    /// The target under a point, hit-tested on <paramref name="geometry"/> (capture it before the drag changes the
    /// layout): a free space, the boundary between two lines of a grid band, an item's edge, a position in a scrolling
    /// or wrapping band. Null in the middle of a grid item. <paramref name="excluding"/> is the dragged item, when it is
    /// in the panel: its own edges are not targets.
    /// </summary>
    public static DashboardDropTarget? Find(DashboardPanelGeometry geometry, DashboardPanel panel, Point point, double edgeZone, UIElement? excluding = null)
    {
        if (geometry.FreeSpaceAt(point) is { } space)
            return new(DashboardDropKind.NextTo, space.Band, 0, space.AnchorId, space.Side);
        if (geometry.LineBoundaryAt(point) is var (band, line))
            return new(DashboardDropKind.Line, band, line);
        if (geometry.EdgeAt(point, edgeZone, excluding) is var (anchor, side) && DashboardPanel.GetItemId(anchor) is { } anchorId)
            return new(DashboardDropKind.NextTo, geometry.BandAt(point.Y), 0, anchorId, side);

        var flowBand = geometry.BandAt(point.Y);
        if (geometry.ModeOf(flowBand) == BandMode.Fit) return null;
        // A scrolling band may have scrolled since: compare in the geometry's own scroll position
        var offset = flowBand < panel.Bands.Count ? panel.Bands[flowBand].HorizontalOffset - geometry.HorizontalOffsetOf(flowBand) : 0;
        return new(DashboardDropKind.Flow, flowBand, geometry.FlowIndexAt(flowBand, new Point(point.X + offset, point.Y), excluding));
    }

    /// <summary>Moves a child of the panel there (indexes count the child at its place before the move).</summary>
    public void Move(UIElement item, bool keepEmptyBands = false)
    {
        if (Kind == DashboardDropKind.NextTo) DashboardLayout.MoveNextTo(item, AnchorId!, Side, keepEmptyBands);
        else DashboardLayout.MoveTo(item, Band, Index, keepEmptyBands);
    }

    /// <summary>Adds a new child there.</summary>
    public void Add(DashboardPanel panel, UIElement item)
    {
        if (Kind == DashboardDropKind.NextTo) DashboardLayout.AddNextTo(panel, AnchorId!, item, Side);
        else DashboardLayout.InsertAt(panel, item, Band, Index);
    }
}
