using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Inserting next to an item, by the "+" buttons (add) and by drag and drop (move).</summary>
public class DashboardInsertionTests
{
    private static ItemLayout Item(string id, int column, int row, int columnSpan, int rowSpan = 2) =>
        new() { Id = id, VisualId = id, Column = column, Row = row, ColumnSpan = columnSpan, RowSpan = rowSpan };

    private static DashboardLayoutDefinition Layout(params ItemLayout[] items) => new() { Bands = [new BandLayout { Items = items }] };

    private static (int Column, int Row, int ColumnSpan, int RowSpan) Box(DashboardLayoutDefinition layout, string id) =>
        DashboardLayoutEngine.Find(layout, id) is { } i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default;

    [Fact]
    public void Adding_between_two_items_of_a_full_line_shares_the_width()
    {
        var layout = Layout(Item("a", 0, 0, 6), Item("b", 6, 0, 6));

        layout = DashboardLayoutEngine.AddNextTo(layout, "a", ItemSide.Right, new ItemLayout { Id = "n" });

        Assert.Equal((0, 0, 4, 2), Box(layout, "a"));
        Assert.Equal((4, 0, 4, 2), Box(layout, "n"));
        Assert.Equal((8, 0, 4, 2), Box(layout, "b"));
    }

    [Fact]
    public void Adding_to_the_left_moves_the_line_aside_when_there_is_room()
    {
        var layout = Layout(Item("a", 0, 0, 4), Item("b", 4, 0, 4));

        layout = DashboardLayoutEngine.AddNextTo(layout, "b", ItemSide.Left, new ItemLayout { Id = "n" });

        // 4 + 4 + 6 > 12: shared equally
        Assert.Equal([(0, 4), (4, 4), (8, 4)], new[] { "a", "n", "b" }.Select(id => (Box(layout, id).Column, Box(layout, id).ColumnSpan)));

        layout = DashboardLayoutEngine.AddNextTo(Layout(Item("a", 0, 0, 3), Item("b", 6, 0, 3)), "b", ItemSide.Left, new ItemLayout { Id = "n" });

        // 3 + 6 + 3 fits: b moves right just enough, a stays
        Assert.Equal([(0, 3), (3, 6), (9, 3)], new[] { "a", "n", "b" }.Select(id => (Box(layout, id).Column, Box(layout, id).ColumnSpan)));
    }

    [Fact]
    public void Adding_above_takes_the_anchors_place_and_pushes_it_down()
    {
        var layout = Layout(Item("a", 3, 0, 6), Item("b", 3, 2, 6));

        layout = DashboardLayoutEngine.AddNextTo(layout, "b", ItemSide.Above, new ItemLayout { Id = "n" });

        Assert.Equal((3, 0, 6, 2), Box(layout, "a"));
        Assert.Equal((3, 2, 6, 3), Box(layout, "n"));
        Assert.Equal((3, 5, 6, 2), Box(layout, "b"));
    }

    [Fact]
    public void A_line_with_more_items_than_columns_sends_the_new_one_below()
    {
        var layout = new DashboardLayoutDefinition { Columns = 2, Bands = [new BandLayout { Items = [Item("a", 0, 0, 1), Item("b", 1, 0, 1)] }] };

        layout = DashboardLayoutEngine.AddNextTo(layout, "a", ItemSide.Right, new ItemLayout { Id = "n" });

        Assert.Equal((0, 2, 2, 2), Box(layout, "n"));
    }

    [Fact]
    public void Moving_between_two_items_keeps_its_size_when_there_is_room()
    {
        // a | gap | b on the first line, x on its own line below
        var layout = Layout(Item("a", 0, 0, 3), Item("b", 9, 0, 3), Item("x", 0, 2, 4, 3));

        layout = DashboardLayoutEngine.MoveNextTo(layout, "x", "a", ItemSide.Right);

        Assert.Equal((0, 0, 3, 2), Box(layout, "a"));
        Assert.Equal((3, 0, 4, 3), Box(layout, "x"));
        Assert.Equal((9, 0, 3, 2), Box(layout, "b"));
    }

    [Fact]
    public void Moving_within_its_own_line_reorders_it()
    {
        var layout = Layout(Item("a", 0, 0, 4), Item("x", 4, 0, 4), Item("b", 8, 0, 4));

        layout = DashboardLayoutEngine.MoveNextTo(layout, "x", "b", ItemSide.Right);

        Assert.Equal([0, 4, 8], new[] { "a", "b", "x" }.Select(id => Box(layout, id).Column));
    }

    [Fact]
    public void Moving_next_to_an_item_of_another_band_removes_the_band_it_empties()
    {
        var layout = new DashboardLayoutDefinition
        {
            Bands =
            [
                new BandLayout { Id = "top", Items = [Item("x", 0, 0, 6)] },
                new BandLayout { Id = "bottom", Items = [Item("a", 0, 0, 6)] },
            ],
        };

        layout = DashboardLayoutEngine.MoveNextTo(layout, "x", "a", ItemSide.Below);

        var band = Assert.Single(layout.Bands);
        Assert.Equal("bottom", band.Id);
        Assert.Equal((0, 2, 6, 2), Box(layout, "x"));
    }

    [Fact]
    public void Moving_next_to_itself_changes_nothing()
    {
        var layout = DashboardLayoutEngine.Normalize(Layout(Item("a", 0, 0, 6)));

        Assert.Equal(Box(layout, "a"), Box(DashboardLayoutEngine.MoveNextTo(layout, "a", "a", ItemSide.Right), "a"));
    }

    [Theory]
    [InlineData(5, 50, ItemSide.Left)]
    [InlineData(195, 50, ItemSide.Right)]
    [InlineData(100, 3, ItemSide.Above)]
    [InlineData(100, 97, ItemSide.Below)]
    [InlineData(205, 50, ItemSide.Right)] // in the gap after the item
    [InlineData(2, 3, ItemSide.Left)]     // corner: the nearest edge
    [InlineData(100, 50, null)]           // middle
    [InlineData(45, 50, null)]            // beyond the zone
    public void The_side_of_an_item_is_found_near_its_edges(double x, double y, ItemSide? expected) =>
        Assert.Equal(expected, DashboardPanel.SideAt(new Rect(0, 0, 200, 100), new Point(x, y), edgeZone: 40));

    [Fact]
    public void A_small_item_keeps_its_middle()
    {
        // Zones are at most a quarter of the item
        Assert.Null(DashboardPanel.SideAt(new Rect(0, 0, 100, 100), new Point(30, 50), edgeZone: 40));
        Assert.Equal(ItemSide.Left, DashboardPanel.SideAt(new Rect(0, 0, 100, 100), new Point(20, 50), edgeZone: 40));
    }

    [Fact]
    public void Edges_are_found_in_grid_bands_only() => Sta.Run(() =>
    {
        var panel = new DashboardPanel { Gap = 10, RowHeight = 100, BandSpacing = 20, Bands = { new DashboardBand(), new DashboardBand { Mode = BandMode.Wrap } } };
        var a = Add(panel, 0, 0, 6);
        var b = Add(panel, 0, 6, 6);
        var flowing = Add(panel, 1, 0, 6);
        panel.Measure(new Size(1190, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1190, panel.DesiredSize.Height));

        Assert.Equal((a, ItemSide.Right), panel.EdgeAt(new Point(588, 100), 40));
        Assert.Equal((b, ItemSide.Left), panel.EdgeAt(new Point(602, 100), 40));
        Assert.Equal((a, ItemSide.Right), panel.EdgeAt(new Point(594, 100), 40));
        Assert.Null(panel.EdgeAt(new Point(594, 100), 40, excluding: a));
        Assert.Null(panel.EdgeAt(new Point(300, 100), 40)); // middle of a
        var bottom = panel.GetItemBounds(flowing);
        Assert.Null(panel.EdgeAt(new Point(bottom.Left + 2, bottom.Top + 50), 40));
    });

    [Fact]
    public void A_panel_child_moves_between_two_others() => Sta.Run(() =>
    {
        var panel = new DashboardPanel();
        var a = Add(panel, 0, 0, 6);
        var b = Add(panel, 0, 6, 6);
        var x = Add(panel, 0, 0, 12, row: 3);

        DashboardLayout.MoveNextTo(x, a, ItemSide.Right);

        Assert.Equal([(0, 4), (4, 4), (8, 4)], new[] { a, x, b }.Select(c => (DashboardPanel.GetColumn(c), DashboardPanel.GetColumnSpan(c))));
        Assert.All(new[] { a, x, b }, c => Assert.Equal(0, DashboardPanel.GetRow(c)));
    });

    private static Border Add(DashboardPanel panel, int band, int column, int columnSpan, int row = 0)
    {
        var child = new Border();
        DashboardPanel.SetBand(child, band);
        DashboardPanel.SetColumn(child, column);
        DashboardPanel.SetRow(child, row);
        DashboardPanel.SetColumnSpan(child, columnSpan);
        DashboardPanel.SetRowSpan(child, 3);
        DashboardPanel.SetItemWidth(child, 300);
        panel.Children.Add(child);
        return child;
    }
}
