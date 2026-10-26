using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Comparisons with numbers: on number dimensions (rows), and on measures of a visual (groups).</summary>
public class NumericFilterTests
{
    private sealed record Sale(string Region, int Quantity, decimal Amount, DateTime Date);

    private static readonly Sale[] Sales =
    [
        new("Nord", 1, 100m, new(2024, 1, 10)),
        new("Nord", 3, 200m, new(2024, 2, 10)),
        new("Sud", 2, 50m, new(2024, 1, 20)),
        new("Sud", 5, 40m, new(2024, 3, 1)),
        new("Est", 4, 400m, new(2024, 3, 5)),
        new("Est", 1, 10m, new(2023, 3, 5)),
        new("Sud", 1, 500m, new(2023, 1, 5)),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Quantity, "Quantité")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "CA")
            .Measure(s => s.Quantity, "Unités", name: "Units");

    private static readonly VisualDefinition ByRegion = new()
    {
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Values = [new("Amount")],
    };

    private static async Task<Dictionary<string, double>> AmountsAsync(VisualDefinition visual, IEnumerable<CalculatedMeasure>? measures = null)
    {
        var request = await VisualPipeline.PrepareAsync(Model(), visual, measures: measures);
        return request.Chart.Categories.Select((c, i) => (c, request.Chart.Series[0].Values[i]!.Value)).ToDictionary(p => p.c, p => p.Item2);
    }

    [Fact]
    public async Task A_range_on_a_number_dimension_keeps_the_rows_between_its_bounds()
    {
        var visual = ByRegion with { Filters = [NumericFilters.Range("Quantity", 2, 4)] };

        var amounts = await AmountsAsync(visual);

        Assert.Equal(new Dictionary<string, double> { ["Est"] = 400, ["Nord"] = 200, ["Sud"] = 50 }, amounts);
    }

    [Fact]
    public async Task An_open_range_and_strict_comparisons_keep_the_matching_rows()
    {
        Assert.Equal(660d, (await AmountsAsync(ByRegion with { Filters = [NumericFilters.Range("Quantity", null, 2)] })).Values.Sum());
        // An integer is compared as a decimal number
        Assert.Equal(640d, (await AmountsAsync(ByRegion with { Filters = [NumericFilters.Compare("Quantity", FilterOperator.GreaterThan, 2.5)] })).Values.Sum());
        Assert.Equal(1260d, (await AmountsAsync(ByRegion with { Filters = [NumericFilters.Compare("Quantity", FilterOperator.LessThanOrEqual, 4)] })).Values.Sum());
    }

    [Fact]
    public async Task A_filter_on_a_measure_keeps_the_groups_whose_aggregate_matches()
    {
        // Totals: Nord 300, Sud 590, Est 410
        var visual = ByRegion with { Filters = [NumericFilters.Compare("Amount", FilterOperator.LessThan, 500)] };

        var amounts = await AmountsAsync(visual);

        Assert.Equal(["Est", "Nord"], amounts.Keys.Order());
    }

    [Fact]
    public async Task Top_applies_to_the_groups_kept_by_a_measure_filter()
    {
        var visual = ByRegion with
        {
            Filters = [NumericFilters.Compare("Amount", FilterOperator.LessThan, 500)],
            Sort = new SortSpec("Amount", Descending: true),
            Top = 1,
        };

        Assert.Equal(["Est"], (await AmountsAsync(visual)).Keys);
    }

    [Fact]
    public async Task A_measure_filter_combines_with_row_filters()
    {
        // Rows of 2024 only: Nord 300, Sud 90, Est 400
        var visual = ByRegion with
        {
            Filters = [PeriodFilters.Between("Date", new(2024, 1, 1), new(2024, 12, 31)), NumericFilters.Range("Amount", 100, 350)],
        };

        Assert.Equal(["Nord"], (await AmountsAsync(visual)).Keys);
    }

    [Fact]
    public async Task A_calculated_measure_filters_the_groups_without_being_shown()
    {
        // Average amount per unit: Nord 75, Sud 73.75, Est 82
        var perUnit = new CalculatedMeasure { Name = "calc_per_unit", Caption = "CA par unité", Model = "sales", Formula = "[Amount] / [Units]" };
        var visual = ByRegion with { Filters = [NumericFilters.Compare("calc_per_unit", FilterOperator.GreaterThanOrEqual, 75)] };

        Assert.Equal(["Est", "Nord"], (await AmountsAsync(visual, [perUnit])).Keys.Order());
    }

    [Fact]
    public async Task A_year_earlier_the_groups_kept_by_a_measure_filter_show_their_whole_value()
    {
        // 2024: Nord 300, Sud 90, Est 400; 2023: Sud 500, Est 10
        var query = new AnalyticQuery(
            [new("Region")],
            [new("Amount"), new("Amount") { Time = TimeCalculation.PreviousYear }],
            [PeriodFilters.Between("Date", new(2024, 1, 1), new(2024, 12, 31)), NumericFilters.Compare("Amount", FilterOperator.LessThan, 100)]);

        var result = await Model().ExecuteWithTimeAsync(query);

        var row = Assert.Single(result.Rows);
        Assert.Equal(("Sud", 90d, 500d), ((string)row[0]!, Convert.ToDouble(row[1]), Convert.ToDouble(row[2])));
    }

    [Fact]
    public void Comparisons_are_described_with_their_numbers()
    {
        Assert.Equal("≥ 1,000", NumericFilters.Describe(NumericFilters.Compare("Amount", FilterOperator.GreaterThanOrEqual, 1000)));
        Assert.Equal("between 10 and 20.5", NumericFilters.Describe(NumericFilters.Range("Amount", 10, 20.5)));
        Assert.Equal("≤ 20", NumericFilters.Describe(NumericFilters.Range("Amount", null, 20)));
        Assert.Equal("< 5 %", NumericFilters.Describe(NumericFilters.Compare("Rate", FilterOperator.LessThan, 0.05), "0 %"));
    }

    [Fact]
    public void A_range_round_trips_through_json_with_an_open_end()
    {
        var report = new ReportDefinition { Visuals = [ByRegion with { Filters = [NumericFilters.Range("Quantity", 2.5, null)] }] };

        var filter = Assert.Single(ReportSerializer.Deserialize(ReportSerializer.Serialize(report)).Visuals[0].Filters);

        Assert.Equal(FilterOperator.Range, filter.Operator);
        Assert.Equal((2.5, (double?)null), NumericFilters.Bounds(filter));
    }

    [Fact]
    public void Measures_only_filter_visuals_and_comparisons_need_numbers()
    {
        var report = new ReportDefinition
        {
            Visuals =
            [
                ByRegion with { Title = "ok", Filters = [NumericFilters.Compare("Amount", FilterOperator.GreaterThan, 1), NumericFilters.Range("Quantity", 1, 2)] },
                ByRegion with { Title = "text", Filters = [NumericFilters.Compare("Region", FilterOperator.GreaterThan, 1)] },
            ],
            Filters = [new ReportFilter("sales", NumericFilters.Compare("Amount", FilterOperator.GreaterThan, 1))],
        };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("text") && p.Contains("Region"));
        Assert.Contains(problems, p => p.Contains("Amount") && p.Contains("only filter a visual"));
    }
}
