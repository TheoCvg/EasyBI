using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class CalculatedMeasureTests
{
    private sealed record Row(string Region, decimal Amount, decimal Margin);

    private static readonly Row[] Rows =
    [
        new("Nord", 100m, 30m),
        new("Nord", 300m, 60m),
        new("Sud", 200m, 100m),
        new("Est", 0m, 0m),
    ];

    private static QueryableSemanticModel<Row> Model(IQueryable<Row>? source = null) =>
        SemanticModel.For(source ?? Rows.AsQueryable(), "m")
            .Dimension(r => r.Region, "Région")
            .Measure(r => r.Amount, "CA")
            .Ratio(r => r.Margin, r => r.Amount, "Taux de marge", "MarginRate", format: "P1")
            .Calculated(g => g.Max(r => r.Amount) - g.Min(r => r.Amount), "Écart de CA", "AmountSpread");

    private static Dictionary<string, object?> ByRegion(ResultSet result, int column) =>
        result.Rows.ToDictionary(r => (string)r[0]!, r => r[column]);

    [Fact]
    public async Task A_ratio_divides_the_sums_of_each_group_not_the_rows()
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("MarginRate")]));

        var rates = ByRegion(result, 1);
        Assert.Equal(90d / 400d, (double)rates["Nord"]!, 10);
        Assert.Equal(0.5, (double)rates["Sud"]!, 10);
    }

    [Fact]
    public async Task A_ratio_is_empty_when_its_denominator_is_zero()
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("MarginRate")]));

        Assert.Null(ByRegion(result, 1)["Est"]);
    }

    [Fact]
    public async Task A_calculated_measure_aggregates_the_rows_of_each_group()
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("AmountSpread")]));

        Assert.Equal(200m, ByRegion(result, 1)["Nord"]);
        Assert.Equal(0m, ByRegion(result, 1)["Sud"]);
    }

    [Fact]
    public async Task Calculated_measures_have_no_aggregation_to_choose()
    {
        var field = Model().Metadata.GetField("MarginRate");
        Assert.True(field.IsCalculated);
        Assert.Null(field.DefaultAggregation);
        Assert.Equal("P1", field.Format);

        // An aggregation saved with the visual is ignored: the measure defines its own
        var result = await Model().ExecuteAsync(new([], [new("MarginRate", Aggregation.Average)]));
        Assert.Equal(190d / 600d, (double)Assert.Single(result.Rows)[0]!, 10);
        Assert.Equal("Taux de marge", Assert.Single(result.Columns).Caption);
    }

    [Fact]
    public async Task Calculated_measures_can_be_sorted_and_limited()
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("MarginRate")], Sort: [new("MarginRate", true)], Top: 1));

        Assert.Equal("Sud", Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task Calculated_measures_are_computed_by_the_provider_in_the_same_query()
    {
        var source = new RecordingQueryable<Row>(Rows);

        await Model(source).ExecuteAsync(new([new("Region")], [new("Amount"), new("MarginRate"), new("AmountSpread")]));

        var expression = Assert.Single(source.Log.Executed);
        Assert.Contains("Sum(r => r.Margin)", expression);
        Assert.Contains("Max(r => r.Amount)", expression);
    }
}
