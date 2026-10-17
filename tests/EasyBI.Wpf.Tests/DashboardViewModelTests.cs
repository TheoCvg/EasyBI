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

    private static IEnumerable<DashboardItemViewModel> Items(DashboardViewModel dashboard) => dashboard.Bands.SelectMany(b => b.Items);

    private static DashboardItemViewModel ItemOf(DashboardViewModel dashboard, string visualId) =>
        Items(dashboard).Single(i => i.Visual?.Id == visualId);

    [Fact]
    public void Moving_an_item_keeps_the_view_models()
    {
        var dashboard = Dashboard();
        var band = dashboard.Bands[0];
        var a = ItemOf(dashboard, "a");
        var c = ItemOf(dashboard, "c");

        dashboard.Apply(r => ReportLayout.MoveItem(r, c.Id, band.Id, 0, 0));

        Assert.Same(c, ItemOf(dashboard, "c"));
        Assert.Equal((0, 0), (c.Column, c.Row));
        Assert.Equal((0, 3), (a.Column, a.Row)); // pushed below c
    }

    [Fact]
    public void Moving_to_another_band_moves_the_view_model()
    {
        var dashboard = Dashboard();
        dashboard.Apply(r => ReportLayout.InsertBand(r, 1, BandMode.Wrap));
        var strip = dashboard.Bands[1];
        var c = ItemOf(dashboard, "c");

        dashboard.Apply(r => ReportLayout.MoveItemTo(r, c.Id, strip.Id, 0));

        Assert.Same(strip, c.Band);
        Assert.Equal(c, strip.Items[0]);
        Assert.Equal(3 * DashboardLayout.DefaultRowHeight + 2 * DashboardViewModel.Gap, c.Height);
    }

    [Fact]
    public void Preview_moves_items_without_changing_the_report()
    {
        var dashboard = Dashboard();
        var c = ItemOf(dashboard, "c");
        var report = dashboard.Report;

        dashboard.Preview(r => ReportLayout.MoveItem(r, c.Id, dashboard.Bands[0].Id, 6, 0));
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
        Assert.DoesNotContain(Items(dashboard), i => i.Visual?.Id == "a");

        dashboard.Undo();
        Assert.Contains(dashboard.Report.Visuals, v => v.Id == "a");
        Assert.Contains(Items(dashboard), i => i.Visual?.Id == "a");

        dashboard.Redo();
        Assert.DoesNotContain(Items(dashboard), i => i.Visual?.Id == "a");
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Full_row_warning_disappears_once_the_item_is_no_longer_full_row()
    {
        var dashboard = Dashboard();
        var a = ItemOf(dashboard, "a");
        dashboard.Apply(r => ReportLayout.SetFullRow(r, a.Id, true));
        a.ShowFullRowWarning = true;

        dashboard.Apply(r => ReportLayout.SplitFullRow(r, a.Id));

        Assert.False(a.IsFullRow);
        Assert.False(a.ShowFullRowWarning);
        Assert.Equal(4, Items(dashboard).Count());
    }

    [Fact]
    public void Item_kind_follows_its_content()
    {
        var dashboard = new DashboardViewModel();
        var item = Assert.Single(Items(dashboard));
        Assert.Equal(DashboardItemKind.Empty, item.Kind);

        dashboard.Apply(r => ReportLayout.SetText(r, item.Id, new TextContent { Text = "Hi" }));
        Assert.Equal(DashboardItemKind.Text, item.Kind);

        // 1x1 transparent PNG
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        dashboard.Apply(r => ReportLayout.SetImage(r, item.Id, new ImageContent { Data = png }));
        Assert.Equal(DashboardItemKind.Image, item.Kind);
        Assert.NotNull(item.ImageSource);

        dashboard.Apply(r => ReportLayout.SetImage(r, item.Id, new ImageContent { Data = "not an image" }));
        Assert.Null(item.ImageSource);
    }
}
