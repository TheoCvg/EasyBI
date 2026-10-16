using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ReportLayoutTests
{
    private static VisualDefinition Visual(string id) => new() { Id = id, Title = id, Model = "sales", Values = [new("Amount")] };

    /// <summary>Report whose items are given as (id, column, row, columnSpan, rowSpan); the item id is also its visual id.</summary>
    private static ReportDefinition Report(params (string Id, int Column, int Row, int ColumnSpan, int RowSpan)[] items) => new()
    {
        Visuals = items.Select(i => Visual(i.Id)).ToArray(),
        Layout = new DashboardLayout
        {
            Items = items.Select(i => new LayoutItem
            {
                Id = i.Id,
                VisualId = i.Id,
                Column = i.Column,
                Row = i.Row,
                ColumnSpan = i.ColumnSpan,
                RowSpan = i.RowSpan,
            }).ToArray(),
        },
    };

    private static (int Column, int Row, int ColumnSpan, int RowSpan) Box(ReportDefinition report, string id) =>
        report.Layout!.Items.Single(i => i.Id == id) is var i ? (i.Column, i.Row, i.ColumnSpan, i.RowSpan) : default;

    [Fact]
    public void Empty_report_gets_one_empty_item()
    {
        var item = Assert.Single(ReportLayout.Normalize(new ReportDefinition()).Layout!.Items);

        Assert.Null(item.VisualId);
        Assert.Equal((0, 0, 6, 3), (item.Column, item.Row, item.ColumnSpan, item.RowSpan));
    }

    [Fact]
    public void Report_without_layout_gets_two_visuals_per_line()
    {
        var report = ReportLayout.Normalize(new ReportDefinition { Visuals = [Visual("a"), Visual("b"), Visual("c")] });

        Assert.Equal([(0, 0, 6, 3), (6, 0, 6, 3), (0, 3, 6, 3)],
            report.Layout!.Items.Select(i => (i.Column, i.Row, i.ColumnSpan, i.RowSpan)));
    }

    [Fact]
    public void Normalize_repairs_overlaps_gaps_bounds_and_references()
    {
        var report = Report(("a", 0, 5, 6, 2), ("b", 2, 5, 6, 2), ("c", 10, 0, 6, 1)) with
        {
            Visuals = [Visual("a"), Visual("c"), Visual("d")],
        };

        report = ReportLayout.Normalize(report);

        Assert.Equal((0, 0, 6, 2), Box(report, "a"));      // floated up
        Assert.Equal((2, 2, 6, 2), Box(report, "b"));      // pushed under a
        Assert.Equal((6, 0, 6, 1), Box(report, "c"));      // kept inside the 12 columns
        Assert.Null(report.Layout!.Items.Single(i => i.Id == "b").VisualId); // unknown visual
        Assert.Contains(report.Layout.Items, i => i.VisualId == "d");       // unplaced visual added
    }

    [Fact]
    public void An_item_can_span_several_rows_and_columns()
    {
        var report = ReportLayout.Normalize(Report(("big", 0, 0, 8, 6), ("top", 8, 0, 4, 3), ("bottom", 8, 3, 4, 3)));

        Assert.Equal((0, 0, 8, 6), Box(report, "big"));
        Assert.Equal((8, 0, 4, 3), Box(report, "top"));
        Assert.Equal((8, 3, 4, 3), Box(report, "bottom"));
    }

    [Fact]
    public void Moving_onto_an_item_pushes_it_down()
    {
        var report = Report(("a", 0, 0, 6, 3), ("b", 6, 0, 6, 3));

        report = ReportLayout.MoveItem(report, "b", 0, 0);

        Assert.Equal((0, 0, 6, 3), Box(report, "b"));
        Assert.Equal((0, 3, 6, 3), Box(report, "a"));
    }

    [Fact]
    public void Items_float_up_but_never_jump_over_another()
    {
        var report = Report(("a", 0, 0, 12, 2), ("b", 0, 2, 6, 2), ("c", 6, 2, 6, 2));

        report = ReportLayout.RemoveItem(report, "a");
        Assert.Equal((0, 0, 6, 2), Box(report, "b"));
        Assert.Equal((6, 0, 6, 2), Box(report, "c"));

        // c is under b: it stays under it when b grows
        report = ReportLayout.MoveItem(Report(("b", 0, 0, 6, 2), ("c", 0, 2, 6, 2)), "b", 0, 0);
        Assert.Equal((0, 2, 6, 2), Box(report, "c"));
    }

    [Fact]
    public void Resizing_pushes_overlapped_items_down_and_is_clamped()
    {
        var report = Report(("a", 0, 0, 6, 3), ("b", 6, 0, 6, 3));

        report = ReportLayout.ResizeItem(report, "a", 8, 4);
        Assert.Equal((0, 0, 8, 4), Box(report, "a"));
        Assert.Equal((6, 4, 6, 3), Box(report, "b"));

        report = ReportLayout.ResizeItem(report, "a", 40, 0);
        Assert.Equal((0, 0, 12, 1), Box(report, "a"));
    }

    [Fact]
    public void Adding_next_to_an_item()
    {
        var report = Report(("a", 0, 0, 4, 2));

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Right, "r");
        Assert.Equal((4, 0, 6, 2), Box(report, "r"));

        report = ReportLayout.AddItemNextTo(report, "a", ItemSide.Below, "b");
        Assert.Equal((0, 2, 4, 3), Box(report, "b"));

        // No room on the right: the new item goes below
        report = ReportLayout.AddItemNextTo(Report(("full", 0, 0, 12, 2)), "full", ItemSide.Right, "n");
        Assert.Equal((0, 2, 12, 3), Box(report, "n"));
    }

    [Fact]
    public void Adding_at_the_end()
    {
        var report = ReportLayout.AddItemAtEnd(Report(("a", 0, 0, 12, 2)), "e");

        Assert.Equal((0, 2, 6, 3), Box(report, "e"));
    }

    [Fact]
    public void Removing_an_item_removes_its_visual_and_keeps_one_item()
    {
        var report = ReportLayout.RemoveItem(Report(("a", 0, 0, 6, 3)), "a");

        Assert.Empty(report.Visuals);
        Assert.Null(Assert.Single(report.Layout!.Items).VisualId);
    }

    [Fact]
    public void Setting_content_replaces_the_previous_one()
    {
        var report = Report(("a", 0, 0, 6, 3));

        report = ReportLayout.SetText(report, "a", new TextContent { Text = "Hello", IsBold = true });
        Assert.Empty(report.Visuals);
        Assert.Equal("Hello", report.Layout!.Items[0].Text!.Text);

        report = ReportLayout.SetImage(report, "a", new ImageContent { Data = "AAAA", Fit = ImageFit.Cover });
        Assert.Null(report.Layout!.Items[0].Text);
        Assert.Equal(ImageFit.Cover, report.Layout.Items[0].Image!.Fit);

        report = ReportLayout.SetVisual(report, "a", Visual("v"));
        Assert.Equal("v", report.Layout!.Items[0].VisualId);
        Assert.Null(report.Layout.Items[0].Image);
        Assert.Equal(["v"], report.Visuals.Select(v => v.Id));
    }

    [Fact]
    public void Editing_a_visual_keeps_its_item()
    {
        var report = Report(("a", 0, 0, 6, 3), ("b", 6, 0, 6, 3));

        report = ReportLayout.SetVisual(report, "a", Visual("a") with { Title = "edited" });

        Assert.Equal("edited", report.Visuals.Single(v => v.Id == "a").Title);
        Assert.Equal(2, report.Layout!.Items.Count);
        Assert.Equal((0, 0, 6, 3), Box(report, "a"));
    }

    [Fact]
    public void Layout_with_text_and_image_round_trips_through_json()
    {
        var report = ReportLayout.SetText(Report(("a", 0, 0, 8, 6)), "a",
            new TextContent { Text = "Title", FontSize = 24, Alignment = TextAlign.Center });
        report = ReportLayout.AddItem(report, new LayoutItem { Id = "img", Column = 8, ColumnSpan = 4, RowSpan = 2, Image = new ImageContent { Data = "AQID", MediaType = "image/jpeg" } });

        var back = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(TextAlign.Center, back.Layout!.Items.Single(i => i.Id == "a").Text!.Alignment);
        Assert.Equal("AQID", back.Layout.Items.Single(i => i.Id == "img").Image!.Data);
        Assert.Equal((8, 0, 4, 2), Box(back, "img"));
    }

    [Fact]
    public void Validator_reports_layout_problems()
    {
        var report = Report(("a", 0, 0, 6, 3)) with
        {
            Layout = new DashboardLayout
            {
                Items =
                [
                    new LayoutItem { VisualId = "a" },
                    new LayoutItem { VisualId = "a" },
                    new LayoutItem { VisualId = "ghost" },
                ],
            },
        };

        var problems = ReportValidator.Validate(report, [new ModelMetadata("sales", "Sales",
            [new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number)])]);

        Assert.Equal(["Layout: visual 'a' is placed more than once.", "Layout: unknown visual 'ghost'."], problems);
    }
}
