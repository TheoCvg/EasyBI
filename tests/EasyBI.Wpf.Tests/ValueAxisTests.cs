using EasyBI.Abstractions;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Bars are compared by their length: whatever the library, their axis starts at zero.</summary>
public class ValueAxisTests
{
    // This year in bars, last year as a line, both far from zero
    private static RenderRequest ThisYearAndLast()
    {
        var chart = new ChartData("Month", ["Jan", "Feb", "Mar"],
        [
            new ChartSeries("This year", [12_000, 14_000, 13_000], null) { SeriesType = VisualType.Bar },
            new ChartSeries("Last year", [11_000, 12_500, 12_000], null) { SeriesType = VisualType.Line },
        ]);
        return new RenderRequest(new VisualDefinition { Type = VisualType.Bar }, new ResultSet([], []), chart, BiTheme.Default);
    }

    [Fact]
    public void LiveCharts2_starts_an_axis_of_bars_and_a_line_at_zero() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(ThisYearAndLast());

        Assert.Equal(0, ((Axis)view.YAxes.First()).MinLimit);
    });

    [Fact]
    public void OxyPlot_starts_an_axis_of_bars_and_a_line_at_zero() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(ThisYearAndLast());

        var axis = view.Model.Axes.OfType<global::OxyPlot.Axes.LinearAxis>().Single(a => a.Key != null && a.Position == global::OxyPlot.Axes.AxisPosition.Left);
        Assert.Equal(0, axis.Minimum);
    });
}
