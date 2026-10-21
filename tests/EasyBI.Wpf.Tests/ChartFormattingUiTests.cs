using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf.Designer;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Charts draw the formats of their measures: colors of points and zones of rules.</summary>
public class ChartFormattingUiTests
{
    private static readonly BiTheme Theme = BiTheme.Default with { Success = "#00AA00", Error = "#CC0000" };

    // Above 50 in green, its zone shown; the values: 10, 50, 100
    private static readonly ValueFormat Format = new()
    {
        Rules = [new FormatRule { Operator = FilterOperator.GreaterThan, Value = 50, Color = ThemeColors.Success, ShowOnChart = true }],
    };

    private static RenderRequest Request(MarkType mark)
    {
        var visual = new VisualDefinition { Mark = mark, Values = [new("Amount") { Format = Format }] };
        var chart = new ChartData("Region", ["A", "B", "C"], [new ChartSeries("Amount", [10, 50, 100], null) { MeasureKey = "Amount", Mark = mark }]);
        return new RenderRequest(visual, new ResultSet([], []), chart, Theme);
    }

    [Fact]
    public void LiveCharts2_shades_the_zone_of_a_rule() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(Request(MarkType.Bar));

        // From 50 up: the open end goes far beyond the data (a null end would start at the bottom of the axis)
        var zone = (RectangularSection)Assert.Single(view.Sections);
        Assert.Equal((50d, 0), (zone.Yi!.Value, zone.ScalesYAt));
        Assert.True(zone.Yj > 100 * 100);
        Assert.Equal("> 50", zone.Label);
    });

    [Fact]
    public void LiveCharts2_colors_the_slices_of_a_pie_with_their_rule() => Sta.Run(() =>
    {
        var view = (PieChart)new LiveCharts2Renderer().Render(Request(MarkType.Pie));

        var colors = view.Series.Cast<PieSeries<double>>().Select(s => ((SolidColorPaint)s.Fill!).Color.ToString()).ToArray();
        Assert.Equal("#ff00aa00", colors[2]);
        Assert.NotEqual("#ff00aa00", colors[0]);
    });

    [Fact]
    public void OxyPlot_colors_each_bar_with_its_rule_and_shades_the_zone() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(Request(MarkType.Bar));

        var bars = (global::OxyPlot.Series.BarSeries)view.Model.Series[0];
        Assert.True(bars.Items[0].Color.IsAutomatic());
        Assert.Equal(global::OxyPlot.OxyColor.Parse("#00AA00"), bars.Items[2].Color);
        var zone = Assert.IsType<global::OxyPlot.Annotations.RectangleAnnotation>(Assert.Single(view.Model.Annotations));
        Assert.Equal(50, zone.MinimumY);
        Assert.Equal(double.PositiveInfinity, zone.MaximumY); // open at the top
    });

    [Fact]
    public void A_rule_shown_on_charts_is_saved_from_the_designer()
    {
        var format = new ValueFormatViewModel(null, () => { });
        format.AddRuleCommand.Execute(null);

        format.Rules[0].ShowOnChart = true;
        format.Rules[0].Number = "3";

        Assert.True(format.ToFormat()!.Rules[0].ShowOnChart);
        Assert.True(new ValueFormatViewModel(format.ToFormat(), () => { }).Rules[0].ShowOnChart);
    }
}
