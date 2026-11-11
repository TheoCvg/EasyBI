using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Navigation;

namespace EasyBI.Wpf.Tests;

/// <summary>Navigation from report to report (EasyBI.Wpf.Navigation), built on the public API.</summary>
public class DashboardNavigationTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, decimal Cost);

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(new[] { new Sale("Nord", "Web", 1m, 1m) }, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA")
            .Measure(s => s.Cost, "Coûts");

    private static readonly VisualDefinition ByRegion = new()
    {
        Id = "regions",
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount")],
        Filters = [new("Amount", FilterOperator.GreaterThan, ["0"])],
    };

    private static DashboardDocument Report(string title, params ReportFilter[] filters) => new()
    {
        Report = new ReportDefinition
        {
            Title = title,
            Visuals = [ByRegion with { Id = title }],
            Filters = filters,
            Variables = [new ReportVariable { Name = "N", Model = "sales", Formula = "3" }],
        },
    };

    private static readonly ReportFilter WebOnly = new("sales", new("Channel", FilterOperator.In, ["Web"]));

    private static InteractionContext NorthWeb(IReadOnlyDictionary<string, string?>? parameters = null) => new(ByRegion)
    {
        Element = new SelectedElement([new("Region"), new("Channel")], ["Nord", "Web"]),
        Filters = [new("Amount", FilterOperator.GreaterThan, ["0"]), new("Region", FilterOperator.In, ["Nord", "Sud"])],
        Parameters = parameters ?? new Dictionary<string, string?> { ["N"] = "7" },
    };

    [Fact]
    public void A_link_brings_the_dimensions_of_the_element_and_of_the_page_and_the_parameters()
    {
        var link = new ReportLink("target", "Target") { Fields = new Dictionary<string, string> { ["Channel"] = "Canal" } };

        var arguments = link.ArgumentsFor(NorthWeb());

        // The measure's filter stays behind only when the target's model is known; the element's region wins
        Assert.Equal(["Amount", "Region", "Canal"], arguments.Filters.Select(f => f.Filter.Field));
        Assert.Equal(["Nord"], arguments.Filters[1].Filter.Values);
        Assert.Equal("7", arguments.Parameters["N"]);

        var known = link.ArgumentsFor(NorthWeb(), Model().Metadata);
        Assert.Equal(["Region"], known.Filters.Select(f => f.Filter.Field));
    }

    [Fact]
    public void A_link_is_offered_on_the_visuals_of_its_model_and_on_an_element_when_it_requires_one()
    {
        var link = new ReportLink("target", "Target") { Model = "sales", RequiresElement = true };

        Assert.True(link.AppliesTo(NorthWeb()));
        Assert.False(link.AppliesTo(NorthWeb() with { Element = null }));
        Assert.False((link with { Model = "other" }).AppliesTo(NorthWeb()));
    }

    [Fact]
    public void Following_a_link_imposes_the_data_pointed_at_and_going_back_restores_the_report_as_left() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var dashboard = new DashboardView { Context = context };
        var filters = new FilterContext();
        var reports = new Dictionary<string, DashboardDocument> { ["start"] = Report("Start"), ["target"] = Report("Target", WebOnly) };
        var navigation = new DashboardNavigation(dashboard, filters, id => reports.TryGetValue(id, out var report) ? report : null);
        var navigated = 0;
        navigation.Navigated += (_, _) => navigated++;

        Assert.True(navigation.Start("start"));
        filters.SetFilter("sales", new("Region", FilterOperator.In, ["Est"]));
        context.Variables.SetValue("N", "5");

        Assert.False(navigation.Follow(new ReportLink("unknown", "?"), NorthWeb()));
        Assert.True(navigation.Follow(new ReportLink("target", "Target"), NorthWeb()));

        Assert.Equal(["Start", "Target"], navigation.History.Path.Select(e => e.Caption));
        Assert.Equal("Target", dashboard.Capture().Report.Visuals[0].Id);
        Assert.Equal(["Nord"], filters.Filters.Single(f => f.Filter.Field == "Region").Filter.Values);
        Assert.Equal([WebOnly], filters.SavedFilters);
        Assert.Equal("7", context.Variables.Find("N")!.Value);
        Assert.Null(Assert.Single(dashboard.Capture().Report.Variables).Value);

        Assert.True(navigation.GoBack());

        Assert.Equal(["Start"], navigation.History.Path.Select(e => e.Caption));
        Assert.Equal("Start", dashboard.Capture().Report.Visuals[0].Id);
        Assert.Equal(["Est"], Assert.Single(filters.Filters).Filter.Values);
        Assert.Equal("5", context.Variables.Find("N")!.Value);
        Assert.False(navigation.GoBack());
        Assert.Equal(3, navigated);
    });

    [Fact]
    public void Going_back_several_steps_restores_that_report_with_what_it_had_received() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var dashboard = new DashboardView { Context = context };
        var filters = new FilterContext();
        var navigation = new DashboardNavigation(dashboard, filters, id => Report(id));
        navigation.Start("a");
        navigation.Follow(new ReportLink("b", "B"), NorthWeb());
        navigation.Follow(new ReportLink("c", "C"), NorthWeb() with { Element = null, Filters = [] });

        Assert.True(navigation.GoBack(1));

        Assert.Equal(["a", "b"], navigation.History.Path.Select(e => e.ReportId));
        Assert.Equal(["Nord"], filters.Filters.Single(f => f.Filter.Field == "Region").Filter.Values);
        Assert.True(filters.IsImposed("sales", "Region"));
    });

    [Fact]
    public void The_menu_of_a_visual_of_the_dashboard_offers_the_links_that_apply() => Sta.Run(() =>
    {
        // The context comes after the navigation, as through a binding
        var dashboard = new DashboardView();
        var navigation = new DashboardNavigation(dashboard, new FilterContext(), id => Report(id));
        var context = EasyBIContext.Create().AddModel(Model());
        dashboard.Context = context;
        navigation.Links.Add(new ReportLink("details", "Details") { Model = "sales" });
        navigation.Links.Add(new ReportLink("elsewhere", "Elsewhere") { Model = "other" });
        navigation.Start("start");
        // The dashboard's panel is part of its template: shown away from the screen
        var window = new System.Windows.Window { Content = dashboard, Width = 400, Height = 300, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var inside = new VisualView { Context = context, Definition = ByRegion };
            dashboard.Panel.Children.Add(inside);
            var outside = new VisualView { Context = context, Definition = ByRegion };

            var menu = context.MenuForTests(inside);

            var goTo = Assert.IsType<System.Windows.Controls.MenuItem>(Assert.Single(menu!.Items));
            Assert.Equal(["Details"], goTo.Items.OfType<System.Windows.Controls.MenuItem>().Select(i => i.Header));
            Assert.Null(context.MenuForTests(outside));
        }
        finally
        {
            window.Close();
        }
    });
}

internal static class MenuTestExtensions
{
    // The menu a right click on the whole visual would open
    public static System.Windows.Controls.ContextMenu? MenuForTests(this EasyBIContext context, VisualView view) =>
        context.MenuFor(view, view.InteractionFor(null)!);
}
