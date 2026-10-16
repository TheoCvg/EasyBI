using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ReportLayoutTests
{
    private static VisualDefinition Visual(string id) => new() { Id = id, Title = id, Model = "sales", Values = [new("Amount")] };

    private static string[][] Shape(ReportDefinition report) =>
        report.Layout!.Rows.Select(r => r.Slots.Select(s => s.VisualId ?? "_").ToArray()).ToArray();

    private static ReportDefinition Report(params string[][] rows) => new()
    {
        Visuals = rows.SelectMany(r => r).Where(id => id != "_").Select(Visual).ToArray(),
        Layout = new DashboardLayout
        {
            Rows = rows.Select((r, i) => new LayoutRow
            {
                Id = $"r{i}",
                Slots = r.Select(id => new LayoutSlot { Id = $"s-{id}", VisualId = id == "_" ? null : id }).ToArray(),
            }).ToArray(),
        },
    };

    [Fact]
    public void Empty_report_gets_one_empty_slot()
    {
        var report = ReportLayout.Normalize(new ReportDefinition());

        Assert.Equal([["_"]], Shape(report));
    }

    [Fact]
    public void Report_without_layout_gets_a_default_grid()
    {
        var report = ReportLayout.Normalize(new ReportDefinition { Visuals = [Visual("a"), Visual("b"), Visual("c")] });

        Assert.Equal([["a", "b"], ["c"]], Shape(report));
    }

    [Fact]
    public void Normalize_repairs_dangling_duplicate_and_unplaced_visuals()
    {
        var report = Report(["a", "a"], ["ghost"]) with { Visuals = [Visual("a"), Visual("b")] };

        Assert.Equal([["a", "_"], ["_"], ["b"]], Shape(ReportLayout.Normalize(report)));
    }

    [Fact]
    public void Inserting_slots_and_rows()
    {
        var report = Report(["a", "b"]);

        report = ReportLayout.InsertSlot(report, "r0", 1);
        Assert.Equal([["a", "_", "b"]], Shape(report));

        report = ReportLayout.InsertRow(report, 0);
        Assert.Equal([["_"], ["a", "_", "b"]], Shape(report));
    }

    [Fact]
    public void New_slot_in_a_fit_row_gets_an_average_share()
    {
        var report = Report(["a", "b"]);
        report = ReportLayout.SetSlotWidths(report, "r0", new Dictionary<string, double> { ["s-a"] = 200, ["s-b"] = 600 });

        report = ReportLayout.InsertSlot(report, "r0", 2);

        Assert.Equal(400, report.Layout!.Rows[0].Slots[2].Width);
    }

    [Theory]
    [InlineData(0, new[] { "c", "a", "b" })]
    [InlineData(2, new[] { "a", "c", "b" })] // "before b" (index 2 of the original row) leaves c in place
    [InlineData(3, new[] { "a", "b", "c" })]
    public void Moving_within_a_row_uses_indexes_of_the_original_row(int index, string[] expected)
    {
        var report = ReportLayout.MoveSlot(Report(["a", "c", "b"]), "s-c", "r0", index);

        Assert.Equal(expected, Shape(report)[0]);
    }

    [Fact]
    public void Moving_to_another_row_removes_the_emptied_row()
    {
        var report = ReportLayout.MoveSlot(Report(["a"], ["b", "c"]), "s-a", "r1", 1);

        Assert.Equal([["b", "a", "c"]], Shape(report));
    }

    [Fact]
    public void Moving_to_a_new_row()
    {
        var report = ReportLayout.MoveSlotToNewRow(Report(["a", "b"], ["c"]), "s-b", 2);

        Assert.Equal([["a"], ["c"], ["b"]], Shape(report));
    }

    [Fact]
    public void Removing_a_slot_removes_its_visual_and_keeps_one_slot()
    {
        var report = ReportLayout.RemoveSlot(Report(["a"], ["b"]), "s-a");
        Assert.Equal([["b"]], Shape(report));
        Assert.Equal(["b"], report.Visuals.Select(v => v.Id));

        report = ReportLayout.RemoveSlot(report, "s-b");
        Assert.Equal([["_"]], Shape(report));
        Assert.Empty(report.Visuals);
    }

    [Fact]
    public void Removing_a_row_removes_its_visuals()
    {
        var report = ReportLayout.RemoveRow(Report(["a", "b"], ["c"]), "r0");

        Assert.Equal([["c"]], Shape(report));
        Assert.Equal(["c"], report.Visuals.Select(v => v.Id));
    }

    [Fact]
    public void Setting_a_visual_fills_or_replaces_a_slot()
    {
        var report = Report(["a", "_"]);

        report = ReportLayout.SetVisual(report, "s-_", Visual("b"));
        Assert.Equal([["a", "b"]], Shape(report));

        var edited = Visual("a") with { Title = "edited" };
        report = ReportLayout.SetVisual(report, "s-a", edited);
        Assert.Equal("edited", report.Visuals.Single(v => v.Id == "a").Title);

        report = ReportLayout.SetVisual(report, "s-a", Visual("c"));
        Assert.Equal([["c", "b"]], Shape(report));
        Assert.Equal(["b", "c"], report.Visuals.Select(v => v.Id).Order());
    }

    [Fact]
    public void Sizes_are_clamped()
    {
        var report = ReportLayout.SetRowHeight(Report(["a"]), "r0", 10);
        report = ReportLayout.SetSlotWidths(report, "r0", new Dictionary<string, double> { ["s-a"] = 1 });

        Assert.Equal(ReportLayout.MinHeight, report.Layout!.Rows[0].Height);
        Assert.Equal(ReportLayout.MinWidth, report.Layout.Rows[0].Slots[0].Width);
    }

    [Fact]
    public void Layout_round_trips_through_json()
    {
        var report = ReportLayout.SetRowOverflow(Report(["a", "b"]), "r0", RowOverflow.Wrap);

        var back = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(Shape(report), Shape(back));
        Assert.Equal(RowOverflow.Wrap, back.Layout!.Rows[0].Overflow);
    }

    [Fact]
    public void Validator_reports_layout_problems()
    {
        var report = Report(["a", "a"], ["ghost"]) with { Visuals = [Visual("a")] };

        var problems = ReportValidator.Validate(report, [new ModelMetadata("sales", "Sales",
            [new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number)])]);

        Assert.Equal(["Layout: visual 'a' is placed more than once.", "Layout: unknown visual 'ghost'."], problems);
    }
}
