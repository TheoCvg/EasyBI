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

    private static DashboardItemViewModel ItemOf(DashboardViewModel dashboard, string visualId) =>
        dashboard.Items.Single(i => i.Visual?.Id == visualId);

    [Fact]
    public void Moving_an_item_keeps_the_view_models()
    {
        var dashboard = Dashboard();
        var a = ItemOf(dashboard, "a");
        var c = ItemOf(dashboard, "c");

        dashboard.Apply(r => ReportLayout.MoveItem(r, c.Id, 0, 0));

        Assert.Same(c, ItemOf(dashboard, "c"));
        Assert.Equal((0, 0), (c.Column, c.Row));
        Assert.Equal((0, 3), (a.Column, a.Row)); // pushed below c
    }

    [Fact]
    public void Preview_moves_items_without_changing_the_report()
    {
        var dashboard = Dashboard();
        var c = ItemOf(dashboard, "c");
        var report = dashboard.Report;

        dashboard.Preview(r => ReportLayout.MoveItem(r, c.Id, 6, 0));
        Assert.Equal((6, 0), (c.Column, c.Row));
        Assert.Same(report, dashboard.Report);
        Assert.False(dashboard.CanUndo);

        dashboard.EndPreview();
        Assert.Equal((0, 3), (c.Column, c.Row));
    }

    [Fact]
    public void Changes_can_be_undone_and_redone()
    {
        var dashboard = Dashboard();
        var a = ItemOf(dashboard, "a");
        var changes = 0;
        dashboard.ReportChanged += (_, _) => changes++;

        dashboard.Apply(r => ReportLayout.RemoveItem(r, a.Id));
        Assert.DoesNotContain(dashboard.Items, i => i.Visual?.Id == "a");

        dashboard.Undo();
        Assert.Contains(dashboard.Report.Visuals, v => v.Id == "a");
        Assert.Contains(dashboard.Items, i => i.Visual?.Id == "a");
        Assert.True(dashboard.CanRedo);

        dashboard.Redo();
        Assert.DoesNotContain(dashboard.Items, i => i.Visual?.Id == "a");
        Assert.Equal(3, changes);
    }

    [Fact]
    public void A_new_change_clears_redo_and_loading_clears_history()
    {
        var dashboard = Dashboard();
        dashboard.Apply(r => ReportLayout.AddItemAtEnd(r));
        dashboard.Undo();

        dashboard.Apply(r => ReportLayout.AddItemAtEnd(r));
        Assert.False(dashboard.CanRedo);

        dashboard.Load(new ReportDefinition());
        Assert.False(dashboard.CanUndo);
        Assert.Equal(DashboardItemKind.Empty, Assert.Single(dashboard.Items).Kind);
    }

    [Fact]
    public void Item_kind_follows_its_content()
    {
        var dashboard = new DashboardViewModel();
        var item = Assert.Single(dashboard.Items);
        Assert.Equal(DashboardItemKind.Empty, item.Kind);

        dashboard.Apply(r => ReportLayout.SetText(r, item.Id, new TextContent { Text = "Hi" }));
        Assert.Equal(DashboardItemKind.Text, item.Kind);
        Assert.False(item.IsTextEmpty);

        // 1x1 transparent PNG
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        dashboard.Apply(r => ReportLayout.SetImage(r, item.Id, new ImageContent { Data = png }));
        Assert.Equal(DashboardItemKind.Image, item.Kind);
        Assert.NotNull(item.ImageSource);

        dashboard.Apply(r => ReportLayout.SetImage(r, item.Id, new ImageContent { Data = "not an image" }));
        Assert.Null(item.ImageSource);
    }
}
