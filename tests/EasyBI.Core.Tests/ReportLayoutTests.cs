using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ReportLayoutTests
{
    private static VisualDefinition Visual(string id) => new() { Id = id, Title = id, Model = "sales", Values = [new("Amount")] };

    private static LayoutItem Item(string id, int column, int row, int columnSpan, int rowSpan) =>
        new() { Id = id, VisualId = id, Column = column, Row = row, ColumnSpan = columnSpan, RowSpan = rowSpan };

    /// <summary>Report made of bands; every item id is also the id of the visual it shows.</summary>
    private static ReportDefinition Report(params LayoutBand[] bands) => new()
    {
        Visuals = bands.SelectMany(b => b.Items).Select(i => Visual(i.Id)).ToArray(),
        Layout = new DashboardLayout { Bands = bands },
    };

    private static LayoutBand Grid(string id, params LayoutItem[] items) => new() { Id = id, Items = items };

    private static LayoutBand Strip(string id, BandMode mode, params string[] items) =>
        new() { Id = id, Mode = mode, Items = items.Select(i => new LayoutItem { Id = i, VisualId = i }).ToArray() };

    private static (int Column, int Row, int ColumnSpan, int RowSpan) Box(ReportDefinition report, string id) =>
        report.Layout!.Bands.SelectMany(b => b.Items).Single(i => i.Id == id) is var i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default;

    private static string[][] Order(ReportDefinition report) =>
        report.Layout!.Bands.Select(b => b.Items.Select(i => i.Id).ToArray()).ToArray();

    // --- Normalization ---

    [Fact]
    public void Empty_report_gets_one_band_with_one_empty_item()
    {
        var band = Assert.Single(ReportLayout.Normalize(new ReportDefinition()).Layout!.Bands);

        Assert.Equal(BandMode.Fit, band.Mode);
        Assert.Null(Assert.Single(band.Items).VisualId);
    }

    [Fact]
    public void Report_without_layout_gets_two_visuals_per_line()
    {
        var report = ReportLayout.Normalize(new ReportDefinition { Visuals = [Visual("a"), Visual("b"), Visual("c")] });

        Assert.Equal([(0, 0, 6, 3), (6, 0, 6, 3), (0, 3, 6, 3)], new[] { "a", "b", "c" }.Select(id =>
            report.Layout!.Bands[0].Items.Single(i => i.VisualId == id) is var i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default));
    }

    [Fact]
    public void Normalize_repairs_overlaps_bounds_references_and_empty_bands()
    {
        var report = Report(Grid("g", Item("a", 0, 5, 6, 2), Item("b", 2, 5, 6, 2), Item("c", 10, 0, 6, 1)), Grid("empty")) with
        {
            Visuals = [Visual("a"), Visual("c"), Visual("d")],
        };

        report = ReportLayout.Normalize(report);

        Assert.Equal((0, 0, 6, 2), Box(report, "a"));      // floated up
        Assert.Equal((2, 2, 6, 2), Box(report, "b"));      // pushed under a
        Assert.Equal((6, 0, 6, 1), Box(report, "c"));      // kept inside the 12 columns
        Assert.Null(report.Layout!.Bands[0].Items.Single(i => i.Id == "b").VisualId); // unknown visual
        Assert.Contains(report.Layout.Bands[0].Items, i => i.VisualId == "d");       // unplaced visual added
        Assert.Single(report.Layout.Bands);                                            // empty band dropped
    }

    // --- Grid bands ---

    [Fact]
    public void An_item_can_span_several_rows_and_columns()
    {
        var report = ReportLayout.Normalize(Report(Grid("g", Item("big", 0, 0, 8, 6), Item("top", 8, 0, 4, 3), Item("bottom", 8, 3, 4, 3))));

        Assert.Equal((0, 0, 8, 6), Box(report, "big"));
        Assert.Equal((8, 3, 4, 3), Box(report, "bottom"));
    }

    [Fact]
    public void Moving_onto_an_item_pushes_it_down()
    {
        var report = ReportLayout.MoveItem(Report(Grid("g", Item("a", 0, 0, 6, 3), Item("b", 6, 0, 6, 3))), "b", "g", 0, 0);

        Assert.Equal((0, 0, 6, 3), Box(report, "b"));
        Assert.Equal((0, 3, 6, 3), Box(report, "a"));
    }

    [Fact]
    public void Items_float_up_but_never_jump_over_another()
    {
        var report = ReportLayout.RemoveItem(Report(Grid("g", Item("a", 0, 0, 12, 2), Item("b", 0, 2, 6, 2), Item("c", 6, 2, 6, 2))), "a");

        Assert.Equal((0, 0, 6, 2), Box(report, "b"));
        Assert.Equal((6, 0, 6, 2), Box(report, "c"));
    }

    [Fact]
    public void Resizing_pushes_overlapped_items_down_and_is_clamped()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 6, 3), Item("b", 6, 0, 6, 3)));

        report = ReportLayout.ResizeItem(report, "a", 8, 4);
        Assert.Equal((0, 0, 8, 4), Box(report, "a"));
        Assert.Equal((6, 4, 6, 3), Box(report, "b"));

        report = ReportLayout.ResizeItem(report, "a", 40, 0);
        Assert.Equal((0, 0, 12, 1), Box(report, "a"));
    }

    [Fact]
    public void Adding_next_to_an_item_in_a_grid()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 4, 2)));

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Right, "r");
        Assert.Equal((4, 0, 6, 2), Box(report, "r"));

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Below, "b");
        Assert.Equal((0, 2, 4, 3), Box(report, "b"));
    }

    // --- Full row ---

    [Fact]
    public void A_full_row_item_covers_every_column_and_pushes_its_neighbours_down()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 6, 2), Item("b", 6, 0, 6, 2)));

        report = ReportLayout.SetFullRow(report, "a", true);

        Assert.Equal((0, 0, 12, 2), Box(report, "a"));
        Assert.Equal((6, 2, 6, 2), Box(report, "b"));

        // It stays full width whatever the requested size
        report = ReportLayout.ResizeItem(report, "a", 4, 2);
        Assert.Equal((0, 0, 12, 2), Box(report, "a"));
    }

    [Fact]
    public void Nothing_is_added_to_the_right_of_a_full_row_item()
    {
        var report = ReportLayout.SetFullRow(Report(Grid("g", Item("a", 0, 0, 6, 2))), "a", true);

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Right, "n");

        Assert.Equal((0, 2, 12, 3), Box(report, "n")); // goes below instead
    }

    [Fact]
    public void Removing_full_row_keeps_the_size_so_the_item_can_be_narrowed()
    {
        var report = ReportLayout.SetFullRow(Report(Grid("g", Item("a", 0, 0, 6, 2))), "a", true);

        report = ReportLayout.SetFullRow(report, "a", false);
        Assert.Equal((0, 0, 12, 2), Box(report, "a"));

        report = ReportLayout.AddItemNextTo(ReportLayout.ResizeItem(report, "a", 6, 2), "a", ItemSide.Right, "n");
        Assert.Equal((6, 0, 6, 2), Box(report, "n"));
    }

    [Fact]
    public void Splitting_a_full_row_puts_the_new_item_beside_it()
    {
        var report = ReportLayout.SetFullRow(Report(Grid("g", Item("a", 0, 0, 6, 2), Item("b", 0, 2, 6, 2))), "a", true);

        report = ReportLayout.SplitFullRow(report, "a", "n");

        Assert.False(report.Layout!.Bands[0].Items.Single(i => i.Id == "a").IsFullRow);
        Assert.Equal((0, 0, 6, 2), Box(report, "a"));
        Assert.Equal((6, 0, 6, 2), Box(report, "n"));
        Assert.Equal((0, 2, 6, 2), Box(report, "b"));
    }

    [Fact]
    public void Splitting_a_full_row_in_a_strip_inserts_after_it()
    {
        var report = ReportLayout.SetFullRow(Report(Strip("s", BandMode.Wrap, "a", "b")), "a", true);

        report = ReportLayout.SplitFullRow(report, "a", "n");

        Assert.Equal([["a", "n", "b"]], Order(report));
        Assert.False(report.Layout!.Bands[0].Items[0].IsFullRow);
    }

    // --- Scrolling and wrapping bands ---

    [Fact]
    public void Strip_bands_keep_item_order_and_insert_after_the_anchor()
    {
        var report = Report(Strip("s", BandMode.Scroll, "a", "b"));

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Right, "n");

        Assert.Equal([["a", "n", "b"]], Order(report));
    }

    [Theory]
    [InlineData(0, new[] { "c", "a", "b" })]
    [InlineData(2, new[] { "a", "c", "b" })] // "before b" (index 2 of the original band)
    [InlineData(3, new[] { "a", "b", "c" })]
    public void Moving_within_a_strip_uses_indexes_of_the_original_band(int index, string[] expected)
    {
        var report = ReportLayout.MoveItemTo(Report(Strip("s", BandMode.Wrap, "a", "c", "b")), "c", "s", index);

        Assert.Equal(expected, Order(report)[0]);
    }

    [Fact]
    public void Items_move_between_bands_and_emptied_bands_disappear()
    {
        var report = Report(Strip("s", BandMode.Scroll, "a"), Grid("g", Item("b", 0, 0, 6, 3)));

        report = ReportLayout.MoveItem(report, "a", "g", 6, 0);

        Assert.Equal([["b", "a"]], Order(report));
        Assert.Equal((6, 0, 6, 3), Box(report, "a"));
    }

    [Fact]
    public void Resizing_a_strip_item_changes_its_width()
    {
        var report = ReportLayout.ResizeItemWidth(Report(Strip("s", BandMode.Wrap, "a")), "a", 50, 2);

        var item = report.Layout!.Bands[0].Items[0];
        Assert.Equal((ReportLayout.MinWidth, 2), (item.Width, item.RowSpan));
    }

    [Fact]
    public void Changing_band_mode_converts_spans_and_widths()
    {
        var report = Report(Grid("g", Item("a", 6, 0, 6, 2), Item("b", 0, 0, 3, 2)));

        report = ReportLayout.SetBandMode(report, "g", BandMode.Scroll);
        var band = report.Layout!.Bands[0];
        Assert.Equal(BandMode.Scroll, band.Mode);
        Assert.Equal(["b", "a"], band.Items.Select(i => i.Id));             // top to bottom, left to right
        Assert.Equal([300d, 600d], band.Items.Select(i => i.Width));         // 3 and 6 of 12 columns of 1200 px

        // With the band's real width and gaps, items keep their size on screen: 8 + 4 columns still fit one line
        var real = ReportLayout.SetBandMode(Report(Grid("g", Item("a", 0, 0, 8, 2), Item("b", 8, 0, 4, 2))), "g", BandMode.Wrap, 1166, 14);
        var widths = real.Layout!.Bands[0].Items.Select(i => i.Width).ToArray();
        Assert.Equal([774d, 378d], widths);   // column width 84: 8 x 84 + 7 x 14, 4 x 84 + 3 x 14
        Assert.Equal(1166d, widths.Sum() + 14); // plus the gap between them: the band width

        var back = ReportLayout.SetBandMode(real, "g", BandMode.Fit, 1166, 14);
        Assert.Equal((0, 0, 8, 2), Box(back, "a"));
        Assert.Equal((8, 0, 4, 2), Box(back, "b"));

        report = ReportLayout.SetBandMode(report, "g", BandMode.Fit);
        Assert.Equal((0, 0, 3, 2), Box(report, "b"));
        Assert.Equal((3, 0, 6, 2), Box(report, "a"));
    }

    [Fact]
    public void Bands_can_be_inserted_and_removed()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 6, 3)));

        report = ReportLayout.InsertBand(report, 0, BandMode.Wrap);
        Assert.Equal([BandMode.Wrap, BandMode.Fit], report.Layout!.Bands.Select(b => b.Mode));

        report = ReportLayout.RemoveBand(report, "g");
        Assert.Empty(report.Visuals);
        Assert.Equal(BandMode.Wrap, Assert.Single(report.Layout!.Bands).Mode);
    }

    // --- Content and persistence ---

    [Fact]
    public void Setting_content_replaces_the_previous_one()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 6, 3)));

        report = ReportLayout.SetText(report, "a", new TextContent { Text = "Hello", IsBold = true });
        Assert.Empty(report.Visuals);
        Assert.Equal("Hello", report.Layout!.Bands[0].Items[0].Text!.Text);

        report = ReportLayout.SetImage(report, "a", new ImageContent { Data = "AAAA", Fit = ImageFit.Cover });
        Assert.Null(report.Layout!.Bands[0].Items[0].Text);

        report = ReportLayout.SetVisual(report, "a", Visual("v"));
        Assert.Equal("v", report.Layout!.Bands[0].Items[0].VisualId);
        Assert.Null(report.Layout.Bands[0].Items[0].Image);
        Assert.Equal(["v"], report.Visuals.Select(v => v.Id));
    }

    [Fact]
    public void Layout_round_trips_through_json()
    {
        var report = ReportLayout.SetFullRow(Report(Grid("g", Item("a", 0, 0, 8, 6)), Strip("s", BandMode.Wrap, "b")), "a", true);
        report = ReportLayout.SetImage(report, "b", new ImageContent { Data = "AQID", MediaType = "image/jpeg" });

        var back = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.True(back.Layout!.Bands[0].Items[0].IsFullRow);
        Assert.Equal(BandMode.Wrap, back.Layout.Bands[1].Mode);
        Assert.Equal("AQID", back.Layout.Bands[1].Items[0].Image!.Data);
    }

    [Fact]
    public void Validator_reports_layout_problems()
    {
        var report = Report(Grid("g", Item("a", 0, 0, 6, 3), Item("a2", 6, 0, 6, 3) with { VisualId = "a" }, Item("ghost", 0, 3, 6, 3))) with
        {
            Visuals = [Visual("a")],
        };

        var problems = ReportValidator.Validate(report, [new ModelMetadata("sales", "Sales",
            [new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number)])]);

        Assert.Equal(["Layout: visual 'a' is placed more than once.", "Layout: unknown visual 'ghost'."], problems);
    }
}
