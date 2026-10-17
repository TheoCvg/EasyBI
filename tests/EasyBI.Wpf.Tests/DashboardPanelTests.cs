using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>The panel showing a layout, its hit tests, and <see cref="DashboardLayout"/> operations on its children.</summary>
public class DashboardPanelTests
{
    // 12 columns of 90 px with gaps of 10 px: a column starts every 100 px; n rows are 110n - 10 high
    private const double Width = 1190;

    private static LayoutNode I(string id, int? columns = null, int? rows = null, double? width = null) =>
        new() { Id = id, VisualId = id, ColumnSpan = columns, RowSpan = rows, Width = width };

    private static (DashboardPanel Panel, Dictionary<string, Border> Children) Panel(params BandLayout[] bands)
    {
        var panel = new DashboardPanel { Gap = 10, BandSpacing = 20 };
        var children = new Dictionary<string, Border>();
        DashboardLayout.SetLayout(panel, new DashboardLayoutDefinition { RowHeight = 100, Bands = bands }, node =>
            children[node.VisualId!] = new Border { Tag = node.VisualId });
        Arrange(panel);
        return (panel, children);
    }

    private static BandLayout Fit(params LayoutNode[] lines) => new() { Items = lines };

    private static void Arrange(DashboardPanel panel)
    {
        panel.Measure(new Size(Width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, Width, panel.DesiredSize.Height));
    }

    [Fact]
    public void Children_are_placed_where_the_layout_puts_their_items() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(I("title", rows: 1), LayoutNode.Row(I("a"), I("b", columns: 4))));

        Assert.Equal(new Rect(0, 0, Width, 100), panel.GetItemBounds(c["title"]));
        Assert.Equal(new Rect(0, 110, 790, 320), panel.GetItemBounds(c["a"]));
        Assert.Equal(new Rect(800, 110, 390, 320), panel.GetItemBounds(c["b"]));
        Assert.Equal(430, panel.DesiredSize.Height);
    });

    [Fact]
    public void Bands_are_stacked_with_spacing_and_free_spaces_are_in_panel_coordinates() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(I("a", rows: 1)), new BandLayout { Mode = BandMode.Wrap, Items = [I("b", width: 300)] });

        Assert.Equal(new Rect(0, 120, 300, 320), panel.GetItemBounds(c["b"]));
        Assert.Equal(new Rect(0, 120, Width, 320), panel.GetBandBounds(1));
        var space = Assert.Single(panel.FreeSpaces);
        Assert.Equal((1, new Rect(310, 120, 880, 320), ItemSide.Right), (space.Band, space.Bounds, space.Side));
        Assert.Equal(DashboardPanel.GetItemId(c["b"]), space.AnchorId);
    });

    [Fact]
    public void A_scrolling_band_shifts_and_clips_its_children() => Sta.Run(() =>
    {
        var (panel, c) = Panel(new BandLayout { Mode = BandMode.Scroll, Items = [I("a", width: 600), I("b", width: 600), I("c", width: 600)] });
        var band = Assert.Single(panel.Bands);
        Assert.Equal(1820, band.ExtentWidth);
        Assert.True(band.CanScroll);

        band.HorizontalOffset = 5000; // clamped to the extent
        Arrange(panel);

        Assert.Equal(1820 - Width, band.HorizontalOffset);
        Assert.Equal(-(1820 - Width), panel.GetItemBounds(c["a"]).Left);
        Assert.NotNull(c["a"].Clip);
        Assert.Null(c["c"].Clip); // fully visible
    });

    [Fact]
    public void Children_without_an_item_are_not_shown() => Sta.Run(() =>
    {
        var (panel, _) = Panel(Fit(I("a")));
        var stray = new Border();
        panel.Children.Add(stray);
        Arrange(panel);

        Assert.Equal(Rect.Empty, panel.GetItemBounds(stray));
        Assert.Equal(-1, panel.BandIndexOf(stray));
    });

    [Fact]
    public void Edges_lines_free_spaces_and_flow_positions_are_found_from_points() => Sta.Run(() =>
    {
        var (panel, c) = Panel(
            Fit(LayoutNode.Row(I("a"), I("b")), LayoutNode.Row(I("x", columns: 6))),
            new BandLayout { Mode = BandMode.Wrap, Items = [I("p"), I("q")] });
        var geometry = panel.CaptureGeometry();

        Assert.Equal((c["a"], ItemSide.Right), geometry.EdgeAt(new Point(588, 100), 40));
        Assert.Equal((c["b"], ItemSide.Left), geometry.EdgeAt(new Point(602, 100), 40));
        Assert.Null(geometry.EdgeAt(new Point(300, 100), 40)); // middle of a
        Assert.Equal((0, 1), geometry.LineBoundaryAt(new Point(300, 325))); // in the gap between the two lines
        Assert.Equal(DashboardPanel.GetItemId(c["x"]), geometry.FreeSpaceAt(new Point(900, 400))!.AnchorId);
        var top = panel.GetBandBounds(1).Top;
        Assert.Equal(1, geometry.FlowIndexAt(1, new Point(700, top + 50)));
        Assert.Null(geometry.EdgeAt(new Point(2, top + 50), 40)); // edges are a grid thing
    });

    [Fact]
    public void A_captured_geometry_keeps_answering_on_the_layout_it_was_taken_from() => Sta.Run(() =>
    {
        // The dragged item leaves its line: the items below go up under the pointer. Hit tests must not follow
        // them, or the target would flip between two places for as long as the drag lasts.
        var (panel, c) = Panel(Fit(I("x"), LayoutNode.Row(I("a"), I("b"))));
        var gapBetween = new Point(594, 400);
        var before = panel.CaptureGeometry();
        Assert.Equal((c["a"], ItemSide.Right), before.EdgeAt(gapBetween, 40, excluding: c["x"]));

        DashboardLayout.MoveTo(c["x"], 0, 2);
        Arrange(panel);

        Assert.Equal((c["a"], ItemSide.Right), before.EdgeAt(gapBetween, 40, excluding: c["x"]));
        Assert.NotEqual(before.GetItemBounds(c["a"]), panel.GetItemBounds(c["a"]));
    });

    [Fact]
    public void Operations_add_and_remove_children() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(I("a")));
        var right = new Border();
        var below = new Border();

        DashboardLayout.AddNextTo(c["a"], right, ItemSide.Right);
        DashboardLayout.AddNextTo(right, below, ItemSide.Below);
        Arrange(panel);
        Assert.Equal([c["a"], right, below], panel.Children.Cast<UIElement>());
        Assert.Equal(new Rect(600, 0, 590, 320), panel.GetItemBounds(right));
        Assert.Equal(330, panel.GetItemBounds(below).Top);

        DashboardLayout.Remove(right);
        Assert.Equal([c["a"], below], panel.Children.Cast<UIElement>());
    });

    [Fact]
    public void A_drag_keeps_the_band_it_empties_until_the_end() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(I("a")), Fit(I("b")));
        var second = panel.Bands[1];

        DashboardLayout.MoveNextTo(c["a"], c["b"], ItemSide.Right, keepEmptyBands: true);
        Assert.Equal(2, panel.Bands.Count);

        DashboardLayout.RemoveEmptyBands(panel);
        Assert.Same(second, Assert.Single(panel.Bands)); // the band state (and its scroll position) is kept
        Assert.Equal([c["b"], c["a"]], panel.ChildrenOf(0));
    });

    [Fact]
    public void A_snapshot_puts_the_layout_and_the_children_back() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(LayoutNode.Row(I("a"), I("b"))));
        var snapshot = DashboardLayout.Capture(panel);
        var layout = panel.Layout;

        DashboardLayout.Remove(c["b"]);
        DashboardLayout.Resize(c["a"], 4, 2);
        snapshot.Restore();

        Assert.Same(layout, panel.Layout);
        Assert.Contains(c["b"], panel.Children.Cast<UIElement>());
    });

    [Fact]
    public void A_layout_round_trips_and_keeps_existing_children() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(LayoutNode.Row(I("a", columns: 8), I("b"))), new BandLayout { Mode = BandMode.Scroll, Items = [I("s", width: 420)] });

        var saved = DashboardLayout.GetLayout(panel, e => (string?)((FrameworkElement)e).Tag);
        var created = new List<UIElement>();
        DashboardLayout.SetLayout(panel, saved, item =>
        {
            if (item.VisualId == "a") return c["a"];
            var element = new Border { Tag = item.VisualId };
            created.Add(element);
            return element;
        });
        Arrange(panel);

        Assert.Equal([BandMode.Fit, BandMode.Scroll], panel.Bands.Select(x => x.Mode));
        Assert.Contains(c["a"], panel.Children.Cast<UIElement>());
        Assert.Equal(790, panel.GetItemBounds(c["a"]).Width);
        Assert.Equal(["b", "s"], created.Select(e => (string)((FrameworkElement)e).Tag));
        Assert.Equal(420, panel.GetItemBounds(created[1]).Width);
    });

    [Fact]
    public void Every_operation_raises_arrangement_changed() => Sta.Run(() =>
    {
        var (panel, c) = Panel(Fit(LayoutNode.Row(I("a"), I("b"))));
        var raised = 0;
        panel.ArrangementChanged += (_, _) => raised++;

        DashboardLayout.Resize(c["a"], 4, 2);
        DashboardLayout.SetAuto(c["a"]);
        DashboardLayout.Update(panel, (layout, idOf) => DashboardLayoutEngine.Remove(layout, idOf(c["b"])));

        Assert.Equal(3, raised);
        Assert.False(DashboardLayout.IsConstrained(c["a"]));
        Assert.Single(panel.Children);
    });
}
