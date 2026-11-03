using System.Windows;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Both connectors draw the category labels as <see cref="ChartFitting"/> fits them: turned, shortened.</summary>
public class CategoryLabelUiTests
{
    private static RenderRequest Request()
    {
        var visual = new VisualDefinition { Type = VisualType.Bar, Values = [new("Amount")] };
        var categories = Enumerable.Range(0, 12).Select(i => $"Very long category name {i}").ToArray();
        var chart = new ChartData("Category", categories, [new ChartSeries("Amount", new double?[categories.Length], null) { SeriesType = VisualType.Bar }]);
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default);
    }

    // A low chart: no room for whole labels at 45°, shortened ones show every category
    private static void Shown(FrameworkElement view, Action check)
    {
        var window = new Window { Content = view, Width = 600, Height = 220, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            check();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void LiveCharts2_shortens_the_labels_a_low_chart_has_no_room_for() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(Request());

        Shown(view, () =>
        {
            var axis = (Axis)view.XAxes.Last();
            Assert.Equal(-45, axis.LabelsRotation);
            Assert.All(axis.Labels!, label => Assert.EndsWith("…", label));
        });
    });

    [Fact]
    public void OxyPlot_shortens_the_labels_a_low_chart_has_no_room_for() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(Request());

        Shown(view, () =>
        {
            var axis = view.Model!.Axes.OfType<OxyPlot.Axes.CategoryAxis>().Single();
            Assert.Equal(-45, axis.Angle);
            Assert.All(axis.Labels, label => Assert.EndsWith("…", label));
        });
    });
}
