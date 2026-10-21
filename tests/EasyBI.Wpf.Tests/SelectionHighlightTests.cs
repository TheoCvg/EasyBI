using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf.Renderers;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Renderers dim what is left out of the cross-filtering selection of their visual.</summary>
public class SelectionHighlightTests
{
    // Two categories, two series (legend)
    private static RenderRequest Request(MarkType mark, params DataSelection[] highlight)
    {
        var chart = new ChartData("Region", ["North", "South"],
        [
            new ChartSeries("Web", [1, 2], null) { Mark = mark },
            new ChartSeries("Shop", [3, 4], null) { Mark = mark },
        ]);
        return new RenderRequest(new VisualDefinition { Mark = mark }, new ResultSet([], []), chart, BiTheme.Default)
        {
            Highlight = highlight,
        };
    }

    private static byte Alpha(IReadOnlyList<LiveChartsCore.ISeries> series, int index) =>
        ((SolidColorPaint)((LineSeries<LiveChartsCore.Defaults.ObservableValue>)series[index]).Stroke!).Color.Alpha;

    [Fact]
    public void LiveCharts2_fades_the_lines_without_a_selected_point() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(Request(MarkType.Line, new CategorySelection(1, 0)));

        var series = view.Series.ToList();
        Assert.Equal(255, Alpha(series, 0));
        Assert.True(Alpha(series, 1) < 255);
    });

    [Fact]
    public void LiveCharts2_fades_the_slices_left_out_of_the_selection() => Sta.Run(() =>
    {
        var view = (PieChart)new LiveCharts2Renderer().Render(Request(MarkType.Pie, new CategorySelection(0)));

        var alphas = view.Series.Cast<PieSeries<double>>().Select(s => ((SolidColorPaint)s.Fill!).Color.Alpha).ToArray();
        Assert.Equal([255, (byte)(255 * SelectionFilters.DimmedOpacity)], alphas);
    });

    [Fact]
    public void Without_a_selection_nothing_is_faded() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(Request(MarkType.Line));

        Assert.All(Enumerable.Range(0, 2), i => Assert.Equal(255, Alpha(view.Series.ToList(), i)));
    });

    [Fact]
    public void OxyPlot_fades_each_bar_left_out_of_the_selection() => Sta.Run(() =>
    {
        // The whole North category, and South for the Web series only
        var view = (PlotView)new OxyPlotRenderer().Render(Request(MarkType.Bar, new CategorySelection(0), new CategorySelection(1, 0)));

        var bars = view.Model.Series.Cast<global::OxyPlot.Series.BarSeries>().ToArray();
        Assert.True(bars[0].Items.All(item => item.Color.IsAutomatic()));
        Assert.True(bars[1].Items[0].Color.IsAutomatic());
        Assert.Equal((byte)(255 * SelectionFilters.DimmedOpacity), bars[1].Items[1].Color.A);
    });

    [Fact]
    public void OxyPlot_reports_ctrl_clicks_too() => Sta.Run(() =>
    {
        var request = Request(MarkType.Bar) with { Select = _ => { } };

        var view = (PlotView)new OxyPlotRenderer().Render(request);

        // A binding only applies to its own modifier keys: Ctrl+click must select like a click
        var bindings = ((global::OxyPlot.PlotController)view.Controller).InputCommandBindings;
        global::OxyPlot.IViewCommand Command(global::OxyPlot.OxyModifierKeys modifiers) => bindings
            .Single(b => b.Gesture is global::OxyPlot.OxyMouseDownGesture { MouseButton: global::OxyPlot.OxyMouseButton.Left } g && g.Modifiers == modifiers)
            .Command;
        Assert.Same(Command(global::OxyPlot.OxyModifierKeys.None), Command(global::OxyPlot.OxyModifierKeys.Control));
    });

    [Fact]
    public void A_table_fades_the_rows_left_out_of_the_selection() => Sta.Run(() =>
    {
        var result = new ResultSet([new ResultColumn("Region", "Region", FieldKind.Dimension, DataType.Text)], [["North"], ["South"], ["East"]]);
        var request = new RenderRequest(new VisualDefinition { Mark = MarkType.Table }, result, new ChartData(null, [], []), BiTheme.Default)
        {
            Highlight = [new RowSelection(1)],
        };
        var grid = (DataGrid)new TableRenderer().Render(request);
        var host = new Border { Child = grid };

        host.Measure(new Size(400, 400));
        host.Arrange(new Rect(0, 0, 400, 400));

        var opacities = Enumerable.Range(0, 3).Select(i => ((DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(i)).Opacity);
        Assert.Equal([SelectionFilters.DimmedOpacity, 1, SelectionFilters.DimmedOpacity], opacities);
    });
}
