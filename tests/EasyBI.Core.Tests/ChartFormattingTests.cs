using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Formats of measures in charts: colors of points (rules, scales) and zones of rules shown on the chart.</summary>
public class ChartFormattingTests
{
    private static readonly BiTheme Theme = BiTheme.Default with { Success = "#00AA00", Warning = "#FFAA00", Error = "#CC0000" };

    // Two measures over three categories; Amount: 10, 50, 100; Count: 1, 2, null
    private static RenderRequest Request(ValueFormat? amount, ValueFormat? count = null, bool secondary = false)
    {
        var visual = new VisualDefinition
        {
            Mark = MarkType.Bar,
            Values = [new("Amount") { Format = amount }, new("Count") { Format = count, SecondaryAxis = secondary }],
        };
        var chart = new ChartData("Region", ["A", "B", "C"],
        [
            new ChartSeries("Amount", [10, 50, 100], "N0") { MeasureKey = "Amount" },
            new ChartSeries("Count", [1, 2, null], null) { MeasureKey = "Count", SecondaryAxis = secondary },
        ]);
        return new RenderRequest(visual, new ResultSet([], []), chart, Theme);
    }

    [Fact]
    public void A_rule_colors_its_points_with_its_text_color_or_else_its_background_at_full_strength()
    {
        var format = new ValueFormat
        {
            Rules =
            [
                new FormatRule { Operator = FilterOperator.GreaterThan, Value = 80, Color = ThemeColors.Success },
                new FormatRule { Operator = FilterOperator.LessThan, Value = 20, Background = ThemeColors.Error },
                new FormatRule { Operator = FilterOperator.LessThan, Value = 60, IsBold = true }, // no color: no effect on a chart
            ],
        };

        var formatting = ChartFormatting.For(Request(format));

        Assert.True(formatting.ColorsPoints);
        Assert.Equal(["#CC0000", null, "#00AA00"], Enumerable.Range(0, 3).Select(c => formatting.PointColor(0, c)));
        Assert.Null(formatting.PointColor(1, 0)); // Count has no format
    }

    [Fact]
    public void A_scale_colors_the_points_between_the_smallest_and_the_largest_of_the_measure()
    {
        var formatting = ChartFormatting.For(Request(null, new ValueFormat { Scale = new ColorScale { Minimum = "#000000", Maximum = "#FFFFFF" } }));

        Assert.Equal(["#000000", "#FFFFFF", null], Enumerable.Range(0, 3).Select(c => formatting.PointColor(1, c)));
    }

    [Fact]
    public void A_stack_is_judged_on_its_total_and_every_segment_takes_its_color()
    {
        // Two segments per category (a legend): totals 30, 70 and 150
        var format = new ValueFormat
        {
            Rules = [new FormatRule { Operator = FilterOperator.GreaterThan, Value = 60, Color = ThemeColors.Success }],
            Scale = new ColorScale { Minimum = "#000000", Maximum = "#FFFFFF" },
        };
        var visual = new VisualDefinition { Mark = MarkType.StackedBar, Values = [new("Amount") { Format = format }] };
        var chart = new ChartData("Region", ["A", "B", "C"],
        [
            new ChartSeries("Web", [10, 50, 100], null) { MeasureKey = "Amount", Mark = MarkType.StackedBar },
            new ChartSeries("Shop", [20, 20, 50], null) { MeasureKey = "Amount", Mark = MarkType.StackedBar },
        ]);

        var formatting = ChartFormatting.For(new RenderRequest(visual, new ResultSet([], []), chart, Theme));

        // A: 30 is the smallest total (the scale's minimum) for both segments; B and C: above 60, green for both
        Assert.Equal(["#000000", "#00AA00", "#00AA00"], Enumerable.Range(0, 3).Select(c => formatting.PointColor(0, c)));
        Assert.Equal(["#000000", "#00AA00", "#00AA00"], Enumerable.Range(0, 3).Select(c => formatting.PointColor(1, c)));
    }

    [Fact]
    public void A_stack_takes_the_color_of_any_of_its_measures_formats()
    {
        var visual = new VisualDefinition
        {
            Mark = MarkType.StackedBar,
            Values =
            [
                new("Amount"),
                new("Margin") { Format = new ValueFormat { Rules = [new FormatRule { Operator = FilterOperator.GreaterThan, Value = 100, Color = ThemeColors.Error }] } },
            ],
        };
        var chart = new ChartData("Region", ["A", "B"],
        [
            new ChartSeries("Amount", [80, 10], null) { MeasureKey = "Amount", Mark = MarkType.StackedBar },
            new ChartSeries("Margin", [40, 10], null) { MeasureKey = "Margin", Mark = MarkType.StackedBar },
        ]);

        var formatting = ChartFormatting.For(new RenderRequest(visual, new ResultSet([], []), chart, Theme));

        Assert.Equal(("#CC0000", "#CC0000"), (formatting.PointColor(0, 0), formatting.PointColor(1, 0))); // 120
        Assert.Null(formatting.PointColor(0, 1)); // 20
    }

    [Fact]
    public void A_format_without_colors_leaves_the_chart_as_it_is()
    {
        var formatting = ChartFormatting.For(Request(new ValueFormat { IsBold = true, Bar = new DataBar() }));

        Assert.False(formatting.ColorsPoints);
        Assert.Empty(formatting.Bands);
    }

    [Fact]
    public void Rules_shown_on_the_chart_give_zones_of_the_value_axis()
    {
        var amount = new ValueFormat
        {
            Rules =
            [
                new FormatRule { Operator = FilterOperator.GreaterThanOrEqual, Value = 1000, Color = ThemeColors.Success, ShowOnChart = true },
                new FormatRule { Operator = FilterOperator.LessThan, Value = 20, Color = ThemeColors.Error }, // not shown
            ],
        };
        var count = new ValueFormat
        {
            Rules = [new FormatRule { Operator = FilterOperator.Range, Value = 1, Maximum = 2, Background = "#123456", ShowOnChart = true }],
        };

        var bands = ChartFormatting.For(Request(amount, count, secondary: true)).Bands;

        Assert.Equal(2, bands.Count);
        Assert.Equal(new ChartBand(1000, null, "#00AA00", false, "≥ 1,000"), bands[0]);
        Assert.Equal(new ChartBand(1, 2, "#123456", true, "between 1 and 2"), bands[1]);
    }

    [Fact]
    public void A_zone_below_a_value_is_open_at_the_bottom()
    {
        var format = new ValueFormat { Rules = [new FormatRule { Operator = FilterOperator.LessThanOrEqual, Value = 5, ShowOnChart = true }] };

        var band = Assert.Single(ChartFormatting.For(Request(format)).Bands);

        Assert.Equal((null, 5d, Theme.ColorAt(0)), (band.From, band.To, band.Color)); // no color: the accent
    }

    [Fact]
    public async Task Each_series_knows_its_measure_to_take_its_format()
    {
        var model = SemanticModel.For(new[] { ("Nord", "Web", 1m), ("Sud", "Shop", 2m) }.Select(t => new { Region = t.Item1, Channel = t.Item2, Amount = t.Item3 }), "m")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA");
        var visual = new VisualDefinition { Model = "m", Axis = [new("Region")], Legend = new("Channel"), Values = [new("Amount", Aggregation.Average)] };

        var request = await VisualPipeline.PrepareAsync(model, visual);

        Assert.All(request.Chart.Series, s => Assert.Equal("Amount.Average", s.MeasureKey));
    }

    [Fact]
    public void Showing_a_zone_is_saved_only_when_chosen()
    {
        var shown = new FormatRule { Operator = FilterOperator.GreaterThan, Value = 1, ShowOnChart = true };
        string Json(FormatRule rule) => ReportSerializer.Serialize(new ReportDefinition
        {
            Visuals = [new VisualDefinition { Values = [new("Amount") { Format = new ValueFormat { Rules = [rule] } }] }],
        });

        Assert.True(ReportSerializer.Deserialize(Json(shown)).Visuals[0].Values[0].Format!.Rules[0].ShowOnChart);
        Assert.DoesNotContain("showOnChart", Json(shown with { ShowOnChart = false }));
    }
}
