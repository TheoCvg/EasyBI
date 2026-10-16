using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardViewModelTests
{
    private static VisualDefinition Visual(string id) => new() { Id = id, Model = "sales", Values = [new("Amount")] };

    private static DashboardViewModel Dashboard()
    {
        var dashboard = new DashboardViewModel();
        dashboard.Load(new ReportDefinition { Visuals = [Visual("a"), Visual("b"), Visual("c")] });
        return dashboard;
    }

    private static string[][] Shape(DashboardViewModel dashboard) =>
        dashboard.Rows.Select(r => r.Slots.Select(s => s.Visual?.Id ?? "_").ToArray()).ToArray();

    [Fact]
    public void Moving_a_slot_keeps_its_view_model()
    {
        var dashboard = Dashboard();
        var c = dashboard.Rows[1].Slots[0];

        dashboard.Apply(r => ReportLayout.MoveSlot(r, c.Id, dashboard.Rows[0].Id, 0));

        Assert.Equal([["c", "a", "b"]], Shape(dashboard));
        Assert.Same(c, dashboard.Rows[0].Slots[0]);
        Assert.Same(dashboard.Rows[0], c.Row);
    }

    [Fact]
    public void Changes_can_be_undone_and_redone()
    {
        var dashboard = Dashboard();
        var a = dashboard.Rows[0].Slots[0];
        var changes = 0;
        dashboard.ReportChanged += (_, _) => changes++;

        dashboard.Apply(r => ReportLayout.RemoveSlot(r, a.Id));
        Assert.Equal([["b"], ["c"]], Shape(dashboard));
        Assert.True(dashboard.CanUndo);

        dashboard.Undo();
        Assert.Equal([["a", "b"], ["c"]], Shape(dashboard));
        Assert.Contains(dashboard.Report.Visuals, v => v.Id == "a");
        Assert.True(dashboard.CanRedo);

        dashboard.Redo();
        Assert.Equal([["b"], ["c"]], Shape(dashboard));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void A_new_change_clears_redo_and_loading_clears_history()
    {
        var dashboard = Dashboard();
        dashboard.Apply(r => ReportLayout.InsertRow(r, 0));
        dashboard.Undo();

        dashboard.Apply(r => ReportLayout.InsertRow(r, 2));
        Assert.False(dashboard.CanRedo);

        dashboard.Load(new ReportDefinition());
        Assert.False(dashboard.CanUndo);
        Assert.Equal([["_"]], Shape(dashboard));
    }

    [Fact]
    public void Slot_flags_follow_the_row()
    {
        var dashboard = Dashboard();
        var row = dashboard.Rows[0];

        Assert.True(row.Slots[0].IsFirst);
        Assert.True(row.Slots[0].CanResize);
        Assert.False(row.Slots[1].CanResize); // last slot of a Fit row

        dashboard.Apply(r => ReportLayout.SetRowOverflow(r, row.Id, RowOverflow.Scroll));
        Assert.True(row.Slots[1].CanResize);
    }
}
