using System.Windows;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Where the bands, lines, children and free spaces of a <see cref="DashboardPanel"/> were at one arrange, and the hit
/// tests on them. <see cref="DashboardPanel.CaptureGeometry"/> freezes it: a drag computes its target on the layout
/// from before the drag, so that items making room under the pointer do not change the target back and forth.
/// </summary>
public sealed class DashboardPanelGeometry
{
    private readonly IReadOnlyList<Rect> _bands;
    private readonly IReadOnlyList<IReadOnlyList<Rect>> _lines;
    private readonly IReadOnlyList<DashboardFreeSpace> _free;
    private readonly IReadOnlyList<BandMode> _modes;
    private readonly IReadOnlyList<double> _offsets;
    private readonly IReadOnlyList<IReadOnlyList<(UIElement Element, Rect Bounds)>> _children;

    internal DashboardPanelGeometry(
        DashboardPanel panel, IReadOnlyList<Rect> bands, IReadOnlyList<IReadOnlyList<Rect>> lines,
        IReadOnlyList<DashboardFreeSpace> free, Func<UIElement, Rect> boundsOf)
    {
        _bands = [.. bands];
        _lines = [.. lines];
        _free = [.. free];
        _modes = Enumerable.Range(0, panel.BandCount).Select(panel.ModeOf).ToArray();
        _offsets = Enumerable.Range(0, panel.BandCount).Select(b => b < panel.Bands.Count ? panel.Bands[b].HorizontalOffset : 0).ToArray();
        _children = Enumerable.Range(0, panel.BandCount)
            .Select(b => (IReadOnlyList<(UIElement, Rect)>)panel.ChildrenOf(b)
                .Select(c => (c, c.Visibility == Visibility.Visible ? boundsOf(c) : Rect.Empty)).ToArray())
            .ToArray();
        Gap = panel.Gap;
        BandSpacing = panel.BandSpacing;
    }

    public double Gap { get; }

    public double BandSpacing { get; }

    public BandMode ModeOf(int band) => band >= 0 && band < _modes.Count ? _modes[band] : BandMode.Fit;

    /// <summary>Horizontal scroll position of a band when the geometry was taken.</summary>
    public double HorizontalOffsetOf(int band) => band >= 0 && band < _offsets.Count ? _offsets[band] : 0;

    public Rect GetBandBounds(int band) => band >= 0 && band < _bands.Count ? _bands[band] : Rect.Empty;

    /// <summary>Children of a band in reading order, with their bounds.</summary>
    public IReadOnlyList<(UIElement Element, Rect Bounds)> ChildrenOf(int band) => band >= 0 && band < _children.Count ? _children[band] : [];

    public Rect GetItemBounds(UIElement child) =>
        _children.SelectMany(c => c).FirstOrDefault(c => c.Element == child) is { Element: not null } found ? found.Bounds : Rect.Empty;

    /// <summary>The band under a vertical position: the band whose bounds (with the spacing below) contain it, or the nearest.</summary>
    public int BandAt(double y)
    {
        for (var i = 0; i < _bands.Count; i++)
            if (y < _bands[i].Bottom + BandSpacing / 2) return i;
        return Math.Max(0, _bands.Count - 1);
    }

    /// <summary>
    /// Position (in <see cref="ChildrenOf"/> order) before which an item dropped at <paramref name="point"/> goes in a
    /// scrolling or wrapping band: before the first other item whose centre comes after the point, reading line by line.
    /// </summary>
    public int FlowIndexAt(int band, Point point, UIElement? excluding = null)
    {
        var bounds = ChildrenOf(band).Select(c => c.Element == excluding ? Rect.Empty : c.Bounds).ToArray();
        return InsertionIndex.At(bounds, point, singleLine: ModeOf(band) == BandMode.Scroll);
    }

    /// <summary>
    /// The edge of a grid band item that <paramref name="point"/> is near, to insert next to it: the item (other than
    /// <paramref name="excluding"/>) whose bounds, widened by half the gap, contain the point, and the side whose zone
    /// (see <see cref="DashboardPanel.SideAt"/>) the point is in. Null in the middle of an item, away from items, or in
    /// scrolling and wrapping bands (use <see cref="FlowIndexAt"/> there).
    /// </summary>
    public (UIElement Anchor, ItemSide Side)? EdgeAt(Point point, double edgeZone, UIElement? excluding = null)
    {
        for (var band = 0; band < _children.Count; band++)
        {
            if (ModeOf(band) != BandMode.Fit) continue;
            foreach (var (child, bounds) in _children[band])
            {
                if (child == excluding || bounds.IsEmpty) continue;
                var widened = bounds;
                widened.Inflate(Gap / 2, Gap / 2);
                if (widened.Contains(point)) return DashboardPanel.SideAt(bounds, point, edgeZone) is { } side ? (child, side) : null;
            }
        }
        return null;
    }

    /// <summary>The free space under a point, if any.</summary>
    public DashboardFreeSpace? FreeSpaceAt(Point point) => _free.FirstOrDefault(f => f.Bounds.Contains(point));

    /// <summary>
    /// In a grid band, the boundary between two lines (or above the first, below the last) that a point is on:
    /// the index of the line a new line would be inserted before. The point must be outside every item.
    /// </summary>
    public (int Band, int Index)? LineBoundaryAt(Point point)
    {
        for (var band = 0; band < _lines.Count; band++)
        {
            if (ModeOf(band) != BandMode.Fit || _lines[band].Count == 0) continue;
            var lines = _lines[band];
            var bounds = GetBandBounds(band);
            if (point.Y < bounds.Top - BandSpacing / 2 || point.Y > bounds.Bottom + BandSpacing / 2) continue;
            if (_children[band].Any(c => c.Bounds.Contains(point))) return null;
            // Above the first line, between two lines, or below the last one
            for (var i = 0; i <= lines.Count; i++)
            {
                var top = i == 0 ? bounds.Top - BandSpacing / 2 : lines[i - 1].Bottom;
                var bottom = i == lines.Count ? bounds.Bottom + BandSpacing / 2 : lines[i].Top;
                if (i == 0) bottom = Math.Max(bottom, top);
                if (point.Y >= top && point.Y <= bottom) return (band, i);
            }
        }
        return null;
    }
}
