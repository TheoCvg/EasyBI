using System.Collections;
using System.Globalization;
using System.Resources;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WPF;

namespace EasyBI.Wpf.Tests;

public class LiveCharts2OptionsTests
{
    private static RenderRequest Request(VisualDefinition visual, int categories)
    {
        var names = Enumerable.Range(1, categories).Select(i => $"C{i}").ToArray();
        var chart = new ChartData("Category", names, [new ChartSeries("Amount", names.Select(_ => (double?)1).ToArray(), null)]);
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default);
    }

    private static readonly VisualDefinition Bars = new() { Mark = MarkType.Bar };

    [Fact]
    public void Scrollable_option_is_offered_for_cartesian_charts_only()
    {
        var option = new LiveCharts2Renderer().Capabilities.Options.Single(o => o.Key == LiveCharts2Renderer.ScrollableOption);

        Assert.Equal(RendererOptionKind.Toggle, option.Kind);
        Assert.True(option.AppliesTo!(Bars));
        Assert.False(option.AppliesTo!(new VisualDefinition { Mark = MarkType.Pie }));
    }

    [Fact]
    public void A_scrollable_chart_shows_the_first_categories_above_a_scroll_bar() => Sta.Run(() =>
    {
        var renderer = new LiveCharts2Renderer();
        var visual = Bars.WithRendererOption(renderer.Name, LiveCharts2Renderer.ScrollableOption, true);

        var grid = Assert.IsType<Grid>(renderer.Render(Request(visual, 50)));

        var charts = grid.Children.OfType<CartesianChart>().ToArray();
        Assert.Equal(2, charts.Length);
        var axis = (Axis)charts[0].XAxes.First();
        Assert.Equal(-0.5, axis.MinLimit);
        Assert.Equal(-0.5 + LiveCharts2Renderer.ScrollWindow, axis.MaxLimit);
        Assert.Single(charts[1].Sections);
    });

    [Fact]
    public void A_scrollable_chart_with_few_categories_shows_them_all() => Sta.Run(() =>
    {
        var renderer = new LiveCharts2Renderer();
        var visual = Bars.WithRendererOption(renderer.Name, LiveCharts2Renderer.ScrollableOption, true);

        var grid = Assert.IsType<Grid>(renderer.Render(Request(visual, 5)));

        Assert.Null(((Axis)grid.Children.OfType<CartesianChart>().First().XAxes.First()).MaxLimit);
    });

    [Fact]
    public void Every_category_has_a_label_slot_so_that_only_the_fit_skips_labels() => Sta.Run(() =>
    {
        // LiveCharts2 skips labels by itself unless told not to: on top of the fit's step, almost none were left
        var renderer = new LiveCharts2Renderer();
        var scrollable = Bars.WithRendererOption(renderer.Name, LiveCharts2Renderer.ScrollableOption, true);
        var charts = new[] { (CartesianChart)renderer.Render(Request(Bars, 50)), ((Grid)renderer.Render(Request(scrollable, 50))).Children.OfType<CartesianChart>().First() };

        foreach (var axis in charts.Select(c => (Axis)c.XAxes.First()))
            Assert.Equal((1d, true), (axis.MinStep, axis.ForceStepToMin));
    });

    [Fact]
    public void Without_the_option_the_chart_has_no_scroll_bar() => Sta.Run(() =>
    {
        Assert.IsType<CartesianChart>(new LiveCharts2Renderer().Render(Request(Bars, 50)));
    });

    [Fact]
    public void French_resources_of_the_connector_define_every_neutral_key()
    {
        var resources = new ResourceManager("EasyBI.Renderers.LiveCharts2.Resources.Strings", typeof(LiveCharts2Renderer).Assembly);

        var neutral = Keys(resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!);
        var french = Keys(resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false)!);

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Order(), french.Order());
    }

    private static IEnumerable<string> Keys(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToArray();
}
