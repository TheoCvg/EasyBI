using System.Collections;
using System.Globalization;
using System.Resources;
using System.Windows;
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

    // Two months of three regions: an axis with two fields
    private static RenderRequest TwoLevels()
    {
        object?[][] keys = [["2024-01", "Est"], ["2024-01", "Nord"], ["2024-01", "Sud"], ["2024-02", "Est"], ["2024-02", "Nord"], ["2024-02", "Sud"]];
        var chart = new ChartData("Month / Region", [.. keys.Select(k => string.Join(" / ", k))], [new ChartSeries("Amount", [1, 2, 3, 4, 5, 6], null) { Mark = MarkType.Bar }])
        {
            CategoryValues = keys,
        };
        return new RenderRequest(Bars, new ResultSet([], []), chart, BiTheme.Default);
    }

    [Fact]
    public void Several_axis_fields_show_short_labels_under_the_groups_of_the_outer_field() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(TwoLevels());

        var axes = view.XAxes.Cast<Axis>().ToArray();
        // The outermost axis first: LiveCharts2 draws it farthest from the plot
        Assert.Equal(2, axes.Length);
        Assert.Equal(["Est", "Nord", "Sud", "Est", "Nord", "Sud"], axes[1].Labels);
        Assert.Equal([1d, 4d], axes[0].CustomSeparators);
        Assert.Equal(["2024-01", "2024-02"], axes[0].CustomSeparators!.Select(axes[0].Labeler));
        // Series follow the category axis; a line separates the months
        Assert.All(view.Series.OfType<LiveChartsCore.Kernel.Sketches.ICartesianSeries>(), s => Assert.Equal(1, s.ScalesXAt));
        Assert.Contains(view.Sections.OfType<LiveChartsCore.SkiaSharpView.RectangularSection>(), s => s.Xi == 2.5 && s.Xj == 2.5);
    });

    [Fact]
    public void Every_category_has_a_label_slot_so_that_only_the_fit_skips_labels() => Sta.Run(() =>
    {
        // LiveCharts2 skips labels by itself unless told not to: on top of the fit's step, almost none were left
        var renderer = new LiveCharts2Renderer();
        var scrollable = Bars.WithRendererOption(renderer.Name, LiveCharts2Renderer.ScrollableOption, true);
        var charts = new[] { (CartesianChart)renderer.Render(Request(Bars, 50)), ((Grid)renderer.Render(Request(scrollable, 50))).Children.OfType<CartesianChart>().First() };

        foreach (var axis in charts.Select(c => (Axis)c.XAxes.Last()))
            Assert.Equal((1d, true), (axis.MinStep, axis.ForceStepToMin));
    });

    [Fact]
    public void Labels_under_groups_turn_like_other_category_labels_rather_than_being_skipped() => Sta.Run(() =>
    {
        string[] regions = ["Est", "Île-de-France", "Nord", "Ouest", "Sud"];
        object?[][] keys = [.. new[] { "Équipement", "Services", "Vélos" }.SelectMany(c => regions.Select(r => new object?[] { c, r }))];
        var chart = new ChartData("Category / Region", [.. keys.Select(k => string.Join(" / ", k))], [new ChartSeries("Amount", new double?[keys.Length], null) { Mark = MarkType.Bar }])
        {
            CategoryValues = keys,
        };
        var view = (CartesianChart)new LiveCharts2Renderer().Render(new RenderRequest(Bars, new ResultSet([], []), chart, BiTheme.Default));
        var window = new Window { Content = view, Width = 700, Height = 450, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

            var axis = (Axis)view.XAxes.Last();
            Assert.Equal(-45, axis.LabelsRotation);
            Assert.Equal(keys.Select(k => (string)k[1]!), axis.Labels);
        }
        finally
        {
            window.Close();
        }
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
