using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>The steps of a waterfall and its rules.</summary>
public class WaterfallTests
{
    [Fact]
    public void Each_step_starts_where_the_running_total_stands_and_the_last_one_shows_the_total()
    {
        var chart = new ChartData("Month", ["Jan", "Feb", "Mar"], [new ChartSeries("Profit", [100, -30, null], "N0")])
        {
            CategoryValues = [["Jan"], ["Feb"], ["Mar"]],
        };

        var (waterfall, steps) = Waterfall.Of(chart);

        Assert.Equal(
            [
                new WaterfallStep(0, 100, WaterfallStepKind.Increase),
                new WaterfallStep(100, 70, WaterfallStepKind.Decrease),
                new WaterfallStep(70, 70, WaterfallStepKind.Increase), // missing: flat
                new WaterfallStep(0, 70, WaterfallStepKind.Total),
            ],
            steps);
        Assert.Equal(["Jan", "Feb", "Mar", "Total"], waterfall.Categories);
        Assert.Equal([100, -30, null, 70], waterfall.Series[0].Values);
        Assert.Equal(3, waterfall.CategoryValues.Count); // the total is no category
        Assert.Equal(-30, steps[1].Value);
    }

    [Fact]
    public void A_waterfall_needs_an_axis_and_a_single_value_without_legend()
    {
        var waterfall = new VisualDefinition { Mark = MarkType.Waterfall, Axis = [new("Month")], Values = [new("Profit")] };

        Assert.Null(VisualValidator.Validate(waterfall));
        Assert.NotNull(VisualValidator.Validate(waterfall with { Axis = [] }));
        Assert.NotNull(VisualValidator.Validate(waterfall with { Legend = new("Region") }));
        Assert.NotNull(VisualValidator.Validate(waterfall with { Values = [new("Profit"), new("Cost")] }));
    }
}
