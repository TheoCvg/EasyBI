using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Global filters, cross-filtering and report validation.</summary>
public class FilteringTests
{
    private sealed record Sale(string Region, string Channel, DateTime Date, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Web", new(2024, 1, 15), 100m),
        new("Nord", "Magasin", new(2024, 4, 10), 200m),
        new("Sud", "Web", new(2024, 2, 20), 300m),
        new("Sud", "Magasin", new(2025, 7, 1), 40m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "CA");

    private static readonly VisualDefinition ByRegionAndChannel = new()
    {
        Model = "sales",
        Mark = MarkType.Bar,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount")],
    };

    private static readonly VisualDefinition Total = new() { Model = "sales", Mark = MarkType.Kpi, Values = [new("Amount")] };

    private static async Task<decimal> TotalAsync(FilterContext filters) =>
        (decimal)(await Model().ExecuteAsync(filters.Apply(Total).ToQuery())).Rows[0][0]!;

    [Fact]
    public async Task Clicking_a_bar_filters_other_visuals_on_axis_and_legend()
    {
        var filters = new FilterContext();
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegionAndChannel);
        var sud = request.Chart.Categories.ToList().IndexOf("Sud");
        var web = request.Chart.Series.ToList().FindIndex(s => s.Name == "Web");

        filters.Select(request, new CategorySelection(sud, web));

        Assert.Equal("Région = Sud, Canal = Web", filters.Selection!.Description);
        Assert.Equal(300m, await TotalAsync(filters));
        // The source visual is not filtered by its own selection
        Assert.Same(ByRegionAndChannel, filters.Apply(ByRegionAndChannel));
    }

    [Fact]
    public async Task Clicking_the_same_element_again_clears_the_selection()
    {
        var filters = new FilterContext();
        var changes = 0;
        filters.Changed += (_, _) => changes++;
        var request = await VisualPipeline.PrepareAsync(Model(), ByRegionAndChannel);

        filters.Select(request, new CategorySelection(0, 0));
        filters.Select(request, new CategorySelection(0, 0));

        Assert.Null(filters.Selection);
        Assert.Equal(2, changes);
        Assert.Equal(640m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Selecting_a_date_period_filters_by_grain()
    {
        var filters = new FilterContext();
        var byQuarter = new VisualDefinition { Model = "sales", Mark = MarkType.Line, Axis = [new("Date", DateGrain.Quarter)], Values = [new("Amount")] };
        var request = await VisualPipeline.PrepareAsync(Model(), byQuarter);

        filters.Select(request, new CategorySelection(request.Chart.Categories.ToList().IndexOf("2024 T1")));

        var filter = Assert.Single(filters.Selection!.Filters);
        Assert.Equal(("Date", DateGrain.Quarter), (filter.Field, filter.Grain));
        Assert.Equal(["20241"], filter.Values);
        Assert.Equal(400m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Clicking_a_table_row_selects_all_its_dimensions()
    {
        var filters = new FilterContext();
        var table = ByRegionAndChannel with { Mark = MarkType.Table };
        var request = await VisualPipeline.PrepareAsync(Model(), table);

        filters.Select(request, new RowSelection(0)); // Nord · Magasin

        Assert.Equal(200m, await TotalAsync(filters));
    }

    [Fact]
    public async Task Kpi_selection_is_ignored()
    {
        var filters = new FilterContext();
        filters.Select(await VisualPipeline.PrepareAsync(Model(), Total), new CategorySelection(0));
        Assert.Null(filters.Selection);
    }

    [Fact]
    public async Task Global_filters_apply_to_their_model_only()
    {
        var filters = new FilterContext();
        filters.SetFilter("sales", new FilterSpec("Region", FilterOperator.In, ["Nord"]));
        filters.SetFilter("other", new FilterSpec("Region", FilterOperator.In, ["Sud"]));

        Assert.Equal(300m, await TotalAsync(filters));
        Assert.Single(filters.FiltersFor(Total));

        filters.SetFilter("sales", new FilterSpec("Region", FilterOperator.In, []));
        Assert.Equal(640m, await TotalAsync(filters));
        Assert.Single(filters.Filters);
    }

    [Fact]
    public async Task Global_filters_and_selection_combine()
    {
        var filters = new FilterContext();
        filters.SetFilter("sales", new FilterSpec("Channel", FilterOperator.In, ["Web"]));
        var request = await VisualPipeline.PrepareAsync(Model(), filters.Apply(ByRegionAndChannel with { Legend = null }));
        Assert.Equal([100d, 300d], request.Chart.Series[0].Values);

        filters.Select(request, new CategorySelection(request.Chart.Categories.ToList().IndexOf("Nord")));

        Assert.Equal(100m, await TotalAsync(filters));
    }

    [Fact]
    public void Loading_report_filters_replaces_them_and_clears_selection()
    {
        var filters = new FilterContext();
        filters.SetFilter("sales", new FilterSpec("Region", FilterOperator.In, ["Nord"]));

        filters.Load([new ReportFilter("sales", new FilterSpec("Channel", FilterOperator.NotIn, ["Web"]))]);

        Assert.Equal("Channel", Assert.Single(filters.Filters).Filter.Field);
        Assert.Null(filters.Selection);
    }

    [Fact]
    public void Report_filters_round_trip_through_json()
    {
        var report = new ReportDefinition
        {
            Filters = [new ReportFilter("sales", new FilterSpec("Region", FilterOperator.In, ["Nord", null]))],
        };

        var back = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        var filter = Assert.Single(back.Filters);
        Assert.Equal("sales", filter.Model);
        Assert.Equal(["Nord", null], filter.Filter.Values);
    }

    [Fact]
    public void Validator_reports_unknown_models_and_fields()
    {
        var report = new ReportDefinition
        {
            Visuals =
            [
                new VisualDefinition { Title = "Inconnu", Model = "nope", Values = [new("Amount")] },
                new VisualDefinition
                {
                    Title = "Cassé",
                    Model = "sales",
                    Axis = [new("Region", DateGrain.Year)],
                    Values = [new("Region")],
                    Sort = new("Amount"),
                },
                ByRegionAndChannel,
            ],
            Filters = [new ReportFilter("sales", new FilterSpec("Ghost", FilterOperator.In, ["x"]))],
        };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Equal(
        [
            "« Inconnu » : modèle 'nope' inconnu.",
            "« Cassé » : granularité 'Year' sur 'Region', qui n'est pas une date.",
            "« Cassé » : 'Region' n'est pas une mesure.",
            "« Cassé » : tri sur 'Amount', qui n'est pas affiché par le visuel.",
            "Filtre du rapport : champ 'Ghost' absent du modèle 'sales'.",
        ], problems);
    }
}
