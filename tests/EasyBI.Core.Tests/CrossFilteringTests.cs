using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Multiple selection (Ctrl+click) and highlight of the selection in its source visual.</summary>
public class CrossFilteringTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Web", 100m),
        new("Nord", "Magasin", 200m),
        new("Sud", "Web", 300m),
        new("Sud", "Magasin", 40m),
        new("Est", "Web", 7m),
    ];

    private static QueryableSemanticModel<Sale> Model(IEnumerable<Sale>? sales = null) =>
        SemanticModel.For(sales ?? Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA");

    private static readonly VisualDefinition ByRegion = new()
    {
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Values = [new("Amount")],
    };

    private static readonly VisualDefinition ByRegionAndChannel = ByRegion with { Legend = new("Channel") };

    private static readonly VisualDefinition Total = new() { Model = "sales", Type = VisualType.Kpi, Values = [new("Amount")] };

    private static async Task<decimal> TotalAsync(FilterContext filters) =>
        (decimal)(await Model().ExecuteAsync(filters.Apply(Total).ToQuery())).Rows[0][0]!;

    private static int Category(RenderRequest request, string name) => request.Chart.Categories.ToList().IndexOf(name);

    private static int Series(RenderRequest request, string name) => request.Chart.Series.ToList().FindIndex(s => s.Name == name);

    [Fact]
    public async Task Ctrl_clicking_categories_keeps_any_of_them()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegion);

        filters.Select(request, new CategorySelection(Category(request, "Nord")));
        filters.Select(request, new CategorySelection(Category(request, "Sud")), extend: true);

        var filter = Assert.Single(Assert.Single(filters.Selections).Filters);
        Assert.Equal(["Nord", "Sud"], filter.Values);
        Assert.Equal("Région = Nord or Sud", filters.Selections[0].Describe(Model().Metadata));
        Assert.Equal(640m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Ctrl_clicking_bars_of_different_categories_and_series_keeps_exactly_those_bars()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegionAndChannel);

        filters.Select(request, new CategorySelection(Category(request, "Nord"), Series(request, "Web")));
        filters.Select(request, new CategorySelection(Category(request, "Sud"), Series(request, "Magasin")), extend: true);

        // Region in (Nord, Sud) and Channel in (Web, Magasin) would keep all four bars
        Assert.Equal(FilterOperator.AnyOf, Assert.Single(Assert.Single(filters.Selections).Filters).Operator);
        Assert.Equal("(Région = Nord, Canal = Web) or (Région = Sud, Canal = Magasin)", filters.Selections[0].Describe(Model().Metadata));
        Assert.Equal(140m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Ctrl_clicking_a_category_and_a_single_bar_keeps_both()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegionAndChannel);

        filters.Select(request, new CategorySelection(Category(request, "Nord")));
        filters.Select(request, new CategorySelection(Category(request, "Sud"), Series(request, "Web")), extend: true);

        Assert.Equal(600m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Ctrl_clicking_a_selected_element_removes_it_and_the_last_one_clears_the_selection()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegion);
        var nord = new CategorySelection(Category(request, "Nord"));
        var sud = new CategorySelection(Category(request, "Sud"));

        filters.Select(request, nord);
        filters.Select(request, sud, extend: true);
        filters.Select(request, nord, extend: true);

        Assert.Equal(340m, await TotalAsync(filters));

        filters.Select(request, sud, extend: true);

        Assert.Empty(filters.Selections);
    }

    [Fact]
    public async Task A_plain_click_replaces_a_multiple_selection()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegion);
        filters.Select(request, new CategorySelection(Category(request, "Nord")));
        filters.Select(request, new CategorySelection(Category(request, "Sud")), extend: true);

        filters.Select(request, new CategorySelection(Category(request, "Sud")));

        Assert.Equal(340m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Ctrl_clicking_in_another_visual_combines_both_selections()
    {
        var filters = new FilterContext();
        var (regions, channels) = await RegionsAndChannelsAsync();
        filters.Select(regions, new CategorySelection(Category(regions, "Nord")));

        filters.Select(channels, new CategorySelection(Category(channels, "Web")), extend: true);
        filters.Select(regions, new CategorySelection(Category(regions, "Sud")), extend: true);

        // (Nord or Sud) and Web
        Assert.Equal(["Region", "Channel"], filters.Selections.Select(s => s.Elements[0].Fields[0].Field));
        Assert.Equal(400m, await TotalAsync(filters));
        Assert.Equal("(Région = Nord or Sud) and Canal = Web", filters.DescribeSelections(_ => Model().Metadata));
    }

    [Fact]
    public async Task Each_visual_is_filtered_by_the_selections_of_the_others_and_highlights_its_own()
    {
        var filters = new FilterContext();
        var (regions, channels) = await RegionsAndChannelsAsync();
        filters.Select(regions, new CategorySelection(Category(regions, "Nord")));
        filters.Select(channels, new CategorySelection(Category(channels, "Web")), extend: true);

        Assert.Equal("Channel", Assert.Single(filters.FiltersFor(regions.Visual)).Field);
        Assert.Equal("Region", Assert.Single(filters.FiltersFor(channels.Visual)).Field);
        var highlight = Assert.Single(filters.HighlightFor(channels)!);
        Assert.Equal(new CategorySelection(Category(channels, "Web")), highlight);
    }

    [Fact]
    public async Task A_plain_click_replaces_the_selections_of_every_visual()
    {
        var filters = new FilterContext();
        var (regions, channels) = await RegionsAndChannelsAsync();
        filters.Select(regions, new CategorySelection(Category(regions, "Nord")));
        filters.Select(channels, new CategorySelection(Category(channels, "Web")), extend: true);

        filters.Select(regions, new CategorySelection(Category(regions, "Sud")));

        Assert.Equal("regions", Assert.Single(filters.Selections).SourceVisualId);
        Assert.Equal(340m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Clearing_the_selection_of_one_visual_keeps_the_others()
    {
        var filters = new FilterContext();
        var (regions, channels) = await RegionsAndChannelsAsync();
        filters.Select(regions, new CategorySelection(Category(regions, "Nord")));
        filters.Select(channels, new CategorySelection(Category(channels, "Web")), extend: true);

        filters.ClearSelection("regions");

        Assert.Equal("channels", Assert.Single(filters.Selections).SourceVisualId);
        Assert.Equal(407m, await TotalAsync(filters));
    }

    [Fact]
    public async Task A_selection_only_filters_the_visuals_of_its_model()
    {
        var filters = new FilterContext();
        var (regions, _) = await RegionsAndChannelsAsync();
        filters.Select(regions, new CategorySelection(Category(regions, "Nord")));

        Assert.Empty(filters.FiltersFor(Total with { Model = "other" }));
    }

    private static async Task<(RenderRequest Regions, RenderRequest Channels)> RegionsAndChannelsAsync() => (
        await VisualPipeline.PrepareAsync(Model(), ByRegion with { Id = "regions" }),
        await VisualPipeline.PrepareAsync(Model(), ByRegion with { Id = "channels", Axis = [new("Channel")] }));

    [Fact]
    public async Task The_source_visual_emphasizes_its_selected_bars_only()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegionAndChannel);
        var (nord, sud, web, shop) = (Category(request, "Nord"), Category(request, "Sud"), Series(request, "Web"), Series(request, "Magasin"));

        filters.Select(request, new CategorySelection(nord, web));
        filters.Select(request, new CategorySelection(sud), extend: true);
        var source = request with { Highlight = filters.HighlightFor(request) };

        Assert.True(source.IsEmphasized(nord, web));
        Assert.False(source.IsEmphasized(nord, shop));
        Assert.True(source.IsEmphasized(sud, shop));
        Assert.False(source.IsEmphasized(Category(request, "Est"), web));
        // Another visual of the model is filtered, not highlighted
        var other = await VisualPipeline.PrepareAsync(Model(), filters.Apply(ByRegion with { Id = "other" }));
        Assert.Null(filters.HighlightFor(other));
        Assert.True(other.IsEmphasized(0, 0));
    }

    [Fact]
    public async Task The_highlight_follows_the_selected_values_when_the_categories_change()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegion);
        filters.Select(request, new CategorySelection(Category(request, "Sud")));

        // The source visual queries again without the North (a global filter, new data...)
        var requeried = await VisualPipeline.PrepareAsync(Model(Sales.Where(s => s.Region != "Nord")), ByRegion);

        var highlight = Assert.Single(filters.HighlightFor(requeried)!);
        Assert.Equal(new CategorySelection(Category(requeried, "Sud")), highlight);
    }

    [Fact]
    public async Task A_table_emphasizes_the_rows_of_its_selection()
    {
        var filters = new FilterContext();
        var table = ByRegionAndChannel with { Type = VisualType.Table };
        var request = await VisualPipeline.PrepareAsync(Model(), table);

        filters.Select(request, new RowSelection(0));
        filters.Select(request, new RowSelection(2), extend: true);
        var source = request with { Highlight = filters.HighlightFor(request) };

        Assert.Equal([true, false, true], Enumerable.Range(0, 3).Select(source.IsRowEmphasized));
    }

    [Fact]
    public void An_alternative_filter_round_trips_through_json_and_is_validated()
    {
        var report = new ReportDefinition
        {
            Visuals =
            [
                Total with
                {
                    Filters =
                    [
                        FilterSpec.AnyOf([[new("Region", FilterOperator.In, ["Nord"])], [new("Unknown", FilterOperator.In, ["x"])]]),
                    ],
                },
            ],
        };

        var filter = Assert.Single(ReportSerializer.Deserialize(ReportSerializer.Serialize(report)).Visuals[0].Filters);

        Assert.Equal(FilterOperator.AnyOf, filter.Operator);
        Assert.Equal(["Region", "Unknown"], filter.Alternatives!.Select(group => group[0].Field));
        Assert.Contains(ReportValidator.Validate(report, [Model().Metadata]), problem => problem.Contains("Unknown"));
    }
}
