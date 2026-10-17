using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardLayoutEngineTests
{
    private static ItemLayout Item(string id, int column, int row, int columnSpan, int rowSpan) =>
        new() { Id = id, VisualId = id, Column = column, Row = row, ColumnSpan = columnSpan, RowSpan = rowSpan };

    private static DashboardLayoutDefinition Layout(params BandLayout[] bands) => new() { Bands = bands };

    private static BandLayout Grid(params ItemLayout[] items) => new() { Items = items };

    private static BandLayout Strip(BandMode mode, params string[] items) =>
        new() { Mode = mode, Items = items.Select(i => new ItemLayout { Id = i, VisualId = i }).ToArray() };

    private static (int Column, int Row, int ColumnSpan, int RowSpan) Box(DashboardLayoutDefinition layout, string id) =>
        DashboardLayoutEngine.Find(layout, id) is { } i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default;

    private static string[][] Order(DashboardLayoutDefinition layout) =>
        layout.Bands.Select(b => b.Items.Select(i => i.Id).ToArray()).ToArray();

    // --- Placing visuals ---

    [Fact]
    public void Placing_nothing_gives_one_band_with_one_empty_item()
    {
        var band = Assert.Single(DashboardLayoutEngine.Place(null, []).Bands);

        Assert.Equal(BandMode.Fit, band.Mode);
        Assert.Null(Assert.Single(band.Items).VisualId);
    }

    [Fact]
    public void Visuals_without_a_layout_are_placed_two_per_line()
    {
        var layout = DashboardLayoutEngine.Place(null, ["a", "b", "c"]);

        Assert.Equal([(0, 0, 6, 3), (6, 0, 6, 3), (0, 3, 6, 3)], new[] { "a", "b", "c" }.Select(id =>
            layout.Bands[0].Items.Single(i => i.VisualId == id) is var i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default));
    }

    [Fact]
    public void Placing_repairs_overlaps_bounds_references_and_empty_bands()
    {
        var layout = DashboardLayoutEngine.Place(
            Layout(Grid(Item("a", 0, 5, 6, 2), Item("ghost", 2, 5, 6, 2), Item("c", 10, 0, 6, 1)), Grid()),
            ["a", "c", "d"]);

        Assert.Equal((0, 0, 6, 2), Box(layout, "a"));      // floated up
        Assert.Equal((2, 2, 6, 2), Box(layout, "ghost"));  // pushed under a
        Assert.Equal((6, 0, 6, 1), Box(layout, "c"));      // kept inside the 12 columns
        Assert.Null(DashboardLayoutEngine.Find(layout, "ghost")!.VisualId); // unknown visual: the item becomes empty
        Assert.Contains(layout.Bands[0].Items, i => i.VisualId == "d");    // unplaced visual added
        Assert.Single(layout.Bands);                                         // empty band dropped
    }

    [Fact]
    public void A_visual_placed_twice_is_shown_once()
    {
        var layout = DashboardLayoutEngine.Place(Layout(Grid(Item("a", 0, 0, 6, 3), Item("copy", 6, 0, 6, 3) with { VisualId = "a" })), ["a"]);

        Assert.Equal(["a", null], layout.Bands[0].Items.Select(i => i.VisualId));
    }

    // --- Grid bands ---

    [Fact]
    public void An_item_can_span_several_rows_and_columns()
    {
        var layout = DashboardLayoutEngine.Normalize(Layout(Grid(Item("big", 0, 0, 8, 6), Item("top", 8, 0, 4, 3), Item("bottom", 8, 3, 4, 3))));

        Assert.Equal((0, 0, 8, 6), Box(layout, "big"));
        Assert.Equal((8, 3, 4, 3), Box(layout, "bottom"));
    }

    [Fact]
    public void Moving_onto_an_item_pushes_it_down()
    {
        var layout = DashboardLayoutEngine.Move(Layout(Grid(Item("a", 0, 0, 6, 3), Item("b", 6, 0, 6, 3))), "b", 0, 0, 0);

        Assert.Equal((0, 0, 6, 3), Box(layout, "b"));
        Assert.Equal((0, 3, 6, 3), Box(layout, "a"));
    }

    [Fact]
    public void Items_float_up_but_never_jump_over_another()
    {
        var layout = DashboardLayoutEngine.Remove(Layout(Grid(Item("a", 0, 0, 12, 2), Item("b", 0, 2, 6, 2), Item("c", 6, 2, 6, 2))), "a");

        Assert.Equal((0, 0, 6, 2), Box(layout, "b"));
        Assert.Equal((6, 0, 6, 2), Box(layout, "c"));
    }

    [Fact]
    public void Resizing_pushes_overlapped_items_down_and_is_clamped()
    {
        var layout = Layout(Grid(Item("a", 0, 0, 6, 3), Item("b", 6, 0, 6, 3)));

        layout = DashboardLayoutEngine.Resize(layout, "a", 8, 4);
        Assert.Equal((0, 0, 8, 4), Box(layout, "a"));
        Assert.Equal((6, 4, 6, 3), Box(layout, "b"));

        layout = DashboardLayoutEngine.Resize(layout, "a", 40, 0);
        Assert.Equal((0, 0, 12, 1), Box(layout, "a"));
    }

    [Fact]
    public void Adding_next_to_an_item_in_a_grid()
    {
        var layout = Layout(Grid(Item("a", 0, 0, 4, 2)));

        layout = DashboardLayoutEngine.AddNextTo(layout, "a", ItemSide.Right, new ItemLayout { Id = "r" });
        Assert.Equal((4, 0, 6, 2), Box(layout, "r"));

        layout = DashboardLayoutEngine.AddNextTo(layout, "a", ItemSide.Below, new ItemLayout { Id = "b" });
        Assert.Equal((0, 2, 4, 3), Box(layout, "b"));
    }

    [Fact]
    public void Adding_at_the_end_of_a_grid_starts_below_everything_then_floats_up()
    {
        var layout = DashboardLayoutEngine.Add(Layout(Grid(Item("a", 0, 0, 6, 2), Item("b", 6, 0, 6, 4))), 0, new ItemLayout { Id = "n" });

        Assert.Equal((0, 2, 6, 3), Box(layout, "n")); // under a, the shorter column
    }

    // --- Full row ---

    [Fact]
    public void A_full_row_item_covers_every_column_and_pushes_its_neighbours_down()
    {
        var layout = DashboardLayoutEngine.SetFullRow(Layout(Grid(Item("a", 0, 0, 6, 2), Item("b", 6, 0, 6, 2))), "a", true);

        Assert.Equal((0, 0, 12, 2), Box(layout, "a"));
        Assert.Equal((6, 2, 6, 2), Box(layout, "b"));

        // It stays full width whatever the requested size
        layout = DashboardLayoutEngine.Resize(layout, "a", 4, 2);
        Assert.Equal((0, 0, 12, 2), Box(layout, "a"));
    }

    [Fact]
    public void Nothing_is_added_to_the_right_of_a_full_row_item()
    {
        var layout = DashboardLayoutEngine.SetFullRow(Layout(Grid(Item("a", 0, 0, 6, 2))), "a", true);

        layout = DashboardLayoutEngine.AddNextTo(layout, "a", ItemSide.Right, new ItemLayout { Id = "n" });

        Assert.Equal((0, 2, 12, 3), Box(layout, "n")); // goes below instead
    }

    [Fact]
    public void Removing_full_row_keeps_the_size_so_the_item_can_be_narrowed()
    {
        var layout = DashboardLayoutEngine.SetFullRow(Layout(Grid(Item("a", 0, 0, 6, 2))), "a", true);

        layout = DashboardLayoutEngine.SetFullRow(layout, "a", false);
        Assert.Equal((0, 0, 12, 2), Box(layout, "a"));

        layout = DashboardLayoutEngine.AddNextTo(DashboardLayoutEngine.Resize(layout, "a", 6, 2), "a", ItemSide.Right, new ItemLayout { Id = "n" });
        Assert.Equal((6, 0, 6, 2), Box(layout, "n"));
    }

    [Fact]
    public void Splitting_a_full_row_puts_the_new_item_beside_it()
    {
        var layout = DashboardLayoutEngine.SetFullRow(Layout(Grid(Item("a", 0, 0, 6, 2), Item("b", 0, 2, 6, 2))), "a", true);

        layout = DashboardLayoutEngine.SplitFullRow(layout, "a", new ItemLayout { Id = "n" });

        Assert.False(DashboardLayoutEngine.Find(layout, "a")!.IsFullRow);
        Assert.Equal((0, 0, 6, 2), Box(layout, "a"));
        Assert.Equal((6, 0, 6, 2), Box(layout, "n"));
        Assert.Equal((0, 2, 6, 2), Box(layout, "b"));
    }

    [Fact]
    public void Splitting_a_full_row_in_a_strip_inserts_after_it()
    {
        var layout = DashboardLayoutEngine.SetFullRow(Layout(Strip(BandMode.Wrap, "a", "b")), "a", true);

        layout = DashboardLayoutEngine.SplitFullRow(layout, "a", new ItemLayout { Id = "n" });

        Assert.Equal([["a", "n", "b"]], Order(layout));
        Assert.False(layout.Bands[0].Items[0].IsFullRow);
    }

    // --- Scrolling and wrapping bands ---

    [Fact]
    public void Strip_bands_keep_item_order_and_insert_after_the_anchor()
    {
        var layout = DashboardLayoutEngine.AddNextTo(Layout(Strip(BandMode.Scroll, "a", "b")), "a", ItemSide.Right, new ItemLayout { Id = "n" });

        Assert.Equal([["a", "n", "b"]], Order(layout));
    }

    [Theory]
    [InlineData(0, new[] { "c", "a", "b" })]
    [InlineData(2, new[] { "a", "c", "b" })] // "before b" (index 2 of the original band)
    [InlineData(3, new[] { "a", "b", "c" })]
    public void Moving_within_a_strip_uses_indexes_of_the_original_band(int index, string[] expected)
    {
        var layout = DashboardLayoutEngine.MoveTo(Layout(Strip(BandMode.Wrap, "a", "c", "b")), "c", 0, index);

        Assert.Equal(expected, Order(layout)[0]);
    }

    [Fact]
    public void Items_move_between_bands_and_emptied_bands_disappear()
    {
        var layout = DashboardLayoutEngine.Move(Layout(Strip(BandMode.Scroll, "a"), Grid(Item("b", 0, 0, 6, 3))), "a", 1, 6, 0);

        Assert.Equal([["b", "a"]], Order(layout));
        Assert.Equal((6, 0, 6, 3), Box(layout, "a"));
    }

    [Fact]
    public void Resizing_a_strip_item_changes_its_width()
    {
        var layout = DashboardLayoutEngine.ResizeWidth(Layout(Strip(BandMode.Wrap, "a")), "a", 50, 2);

        var item = layout.Bands[0].Items[0];
        Assert.Equal((DashboardLayoutEngine.MinWidth, 2), (item.Width, item.RowSpan));
    }

    [Fact]
    public void Changing_band_mode_keeps_item_sizes_on_screen()
    {
        var layout = Layout(Grid(Item("a", 6, 0, 6, 2), Item("b", 0, 0, 3, 2)));

        var strip = DashboardLayoutEngine.SetBandMode(layout, 0, BandMode.Scroll);
        Assert.Equal(BandMode.Scroll, strip.Bands[0].Mode);
        Assert.Equal(["b", "a"], strip.Bands[0].Items.Select(i => i.Id));      // top to bottom, left to right
        Assert.Equal([300d, 600d], strip.Bands[0].Items.Select(i => i.Width)); // 3 and 6 of 12 columns of 1200 px

        // With the band's real width and gaps: 8 + 4 columns still fit one line (12 x 84 + 11 x 14 = 1162)
        var real = DashboardLayoutEngine.SetBandMode(Layout(Grid(Item("a", 0, 0, 8, 2), Item("b", 8, 0, 4, 2))), 0, BandMode.Wrap, 1162, 14);
        var widths = real.Bands[0].Items.Select(i => i.Width).ToArray();
        Assert.Equal([770d, 378d], widths);    // 8 x 84 + 7 x 14, 4 x 84 + 3 x 14
        Assert.Equal(1162d, widths.Sum() + 14); // plus the gap between them: the band width

        var back = DashboardLayoutEngine.SetBandMode(real, 0, BandMode.Fit, 1162, 14);
        Assert.Equal((0, 0, 8, 2), Box(back, "a"));
        Assert.Equal((8, 0, 4, 2), Box(back, "b"));
    }

    [Fact]
    public void Bands_can_be_inserted_and_removed()
    {
        var layout = Layout(Grid(Item("a", 0, 0, 6, 3)));

        layout = DashboardLayoutEngine.InsertBand(layout, 0, BandMode.Wrap, [new ItemLayout { Id = "n" }]);
        Assert.Equal([BandMode.Wrap, BandMode.Fit], layout.Bands.Select(b => b.Mode));
        Assert.Equal([["n"], ["a"]], Order(layout));

        layout = DashboardLayoutEngine.RemoveBand(layout, 1);
        Assert.Equal([["n"]], Order(layout));
    }
}
