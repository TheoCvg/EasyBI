using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;
using LiveChartsCore.Kernel.Sketches;
using OxyPlot;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>Chart tooltips: EasyBI's content shown in each connector's tooltip mechanism, and set up in the designer.</summary>
public class ChartTooltipTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, int Quantity);

    private static readonly TooltipContent Content = new("Nord", [new TooltipItem("Web", "100", "#FF0000")]);

    private static RenderRequest Request(MarkType mark)
    {
        var visual = new VisualDefinition { Mark = mark, Values = [new("Amount")] };
        var chart = new ChartData("Region", ["A", "B"], [new ChartSeries("Amount", [10, 50], null) { MeasureKey = "Amount", Mark = mark }]);
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default);
    }

    // A chart in a window away from the screen (a tooltip opens over a visible element)
    private static void Shown(FrameworkElement chart, Action test)
    {
        var window = new Window { Content = chart, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            test();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void The_default_tooltip_template_is_found_from_a_chart() => Sta.Run(() =>
    {
        Assert.IsType<DataTemplate>(new Border().TryFindResource(ChartTooltips.TemplateKey));
    });

    [Fact]
    public void A_host_shows_the_content_with_the_default_template_and_hides_it_on_null() => Sta.Run(() =>
    {
        var chart = new Border();
        Shown(chart, () =>
        {
            var host = new ChartTooltipHost(chart);

            host.Show(Content);
            Assert.True(host.ToolTip.IsOpen);
            Assert.Same(Content, host.ToolTip.Content);
            Assert.Same(chart.TryFindResource(ChartTooltips.TemplateKey), host.ToolTip.ContentTemplate);
            // The tooltip service does not open it on its own
            Assert.False(ToolTipService.GetIsEnabled(chart));

            host.Show(null);
            Assert.False(host.ToolTip.IsOpen);
        });
    });

    [Fact]
    public void An_application_replaces_the_tooltip_template_through_its_key() => Sta.Run(() =>
    {
        var template = (DataTemplate)XamlReader.Parse(
            """<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding Title}" /></DataTemplate>""");
        var chart = new Border { Resources = { [ChartTooltips.TemplateKey] = template } };
        Shown(chart, () =>
        {
            var host = new ChartTooltipHost(chart);

            host.Show(Content);

            Assert.Same(template, host.ToolTip.ContentTemplate);
        });
    });

    [Fact]
    public void LiveCharts2_shows_EasyBI_tooltips() => Sta.Run(() =>
    {
        foreach (var mark in new[] { MarkType.Bar, MarkType.Pie, MarkType.Waterfall })
        {
            var view = new LiveCharts2Renderer().Render(Request(mark));

            Assert.IsNotType<LiveChartsCore.SkiaSharpView.SKCharts.SKDefaultTooltip>(Assert.IsAssignableFrom<IChartView>(view).Tooltip);
            Assert.IsType<ToolTip>(view.ToolTip);
        }
    });

    [Fact]
    public void OxyPlot_shows_EasyBI_tooltips_instead_of_its_tracker() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(Request(MarkType.Bar) with { Select = _ => { } });

        Assert.IsType<ToolTip>(view.ToolTip);
        var leftClicks = ((PlotController)view.Controller!).InputCommandBindings
            .Where(b => b.Gesture is OxyMouseDownGesture { MouseButton: OxyMouseButton.Left })
            .Select(b => b.Command)
            .ToArray();
        Assert.NotEmpty(leftClicks); // selection
        Assert.DoesNotContain(OxyPlot.PlotCommands.SnapTrack, leftClicks);
        Assert.DoesNotContain(OxyPlot.PlotCommands.Track, leftClicks);
    });

    [Fact]
    public void An_application_changes_the_content_of_tooltips_before_they_open() => Sta.Run(() =>
    {
        // Query continuations come back to the dispatcher, as in an application
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var renderer = new RecordingRenderer();
        var context = EasyBIContext.Create()
            .AddModel(SemanticModel.For(new Sale[] { new("Nord", "Web", 1m, 1) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Measure(s => s.Amount, "CA"))
            .UseRenderer(renderer);
        TooltipOpeningEventArgs? opening = null;
        context.TooltipOpening += (_, e) =>
        {
            opening = e;
            e.Content = e.Content! with { Title = $"{e.Content.Title}!" };
        };
        var view = new VisualView
        {
            Context = context,
            Definition = new VisualDefinition { Model = "sales", Mark = MarkType.Bar, Axis = [new("Region")], Values = [new("Amount")] },
        };
        Shown(view, () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (renderer.Last is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Thread.Sleep(5);
            }

            var content = renderer.Last!.TooltipFor(0, [0]);

            Assert.Equal("Nord!", content?.Title);
            Assert.Equal((0, 0), (opening!.Category, opening.Series.Single()));
            Assert.Same(view.Definition, opening.Visual);
        });
    });

    private sealed class RecordingRenderer : IVisualRenderer
    {
        public RenderRequest? Last { get; private set; }

        public string Name => "Recording";

        public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Bar });

        public FrameworkElement Render(RenderRequest request)
        {
            Last = request;
            return new Border();
        }
    }

    // --- Designer ---

    private static DesignerViewModel Designer()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", "Web", 1m, 1) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Channel, "Channel")
                .Measure(s => s.Amount, "Amount")
                .Measure(s => s.Quantity, "Quantity")));
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount")] });
        return designer;
    }

    private static FieldDescriptor Field(DesignerViewModel designer, string name) =>
        designer.Dimensions.Concat(designer.Measures).Single(f => f.Name == name);

    [Fact]
    public void The_tooltip_well_takes_measures_of_charts_only()
    {
        var designer = Designer();

        Assert.True(designer.TooltipWell.IsUsed);
        Assert.False(designer.CanDrop(Field(designer, "Channel"), Shelf.Tooltip));
        designer.Drop(Field(designer, "Quantity"), Shelf.Tooltip);
        Assert.False(designer.CanDrop(Field(designer, "Quantity"), Shelf.Tooltip)); // already there

        Assert.Equal("Quantity", Assert.Single(designer.Visual!.Tooltip!.Values).Measure);
        Assert.DoesNotContain(designer.Visual.Values, v => v.Measure == "Quantity");

        // A table has no tooltips: the well holds its chip for when the user comes back to a chart
        designer.SelectedMark = MarkType.Table;
        Assert.False(designer.TooltipWell.IsUsed);
        Assert.True(designer.TooltipWell.IsVisible);
        Assert.Null(designer.Visual!.Tooltip);
        designer.SelectedMark = MarkType.Line;
        Assert.Single(designer.Visual!.Tooltip!.Values);
    }

    [Fact]
    public void The_properties_panel_sets_up_the_tooltips_of_a_chart()
    {
        var designer = Designer();
        Assert.Null(designer.Visual!.Tooltip); // nothing saved by default

        var section = designer.Properties.Single(s => s.Key == DesignerPropertySections.Tooltips);
        section["TooltipSeries"]!.IsOn = false;
        section["TooltipPercent"]!.IsOn = true;
        section["TooltipTemplate"]!.TextValue = "{category}: {value}";
        Assert.Contains("{Amount}", section["TooltipTemplate"]!.Description);

        var tooltip = designer.Visual!.Tooltip!;
        Assert.Equal((true, true, false, "{category}: {value}"), (tooltip.HideSeries, tooltip.ShowPercent, tooltip.HideCategory, tooltip.Template));

        section["ShowTooltips"]!.IsOn = false;
        Assert.True(designer.Visual!.Tooltip!.IsHidden);
        Assert.False(section["TooltipPercent"]!.IsAvailable);

        // Kept when the visual is loaded again; a KPI has no tooltips section
        var saved = designer.Visual;
        designer.Load(saved);
        Assert.False(designer.ShowTooltips);
        Assert.Equal("{category}: {value}", designer.TooltipTemplate);
        designer.SelectedMark = MarkType.Kpi;
        Assert.DoesNotContain(designer.Properties, s => s.Key == DesignerPropertySections.Tooltips);
    }

    [Fact]
    public void A_tooltip_measure_is_loaded_into_its_well_with_its_aggregation()
    {
        var designer = Designer();
        designer.Load(new VisualDefinition
        {
            Model = "sales",
            Axis = [new("Region")],
            Values = [new("Amount")],
            Tooltip = new() { Values = [new("Quantity", Aggregation.Max)] },
        });

        var chip = Assert.Single(designer.TooltipValues);
        Assert.Equal(Aggregation.Max, chip.Aggregation);
        Assert.Equal(Aggregation.Max, Assert.Single(designer.Visual!.Tooltip!.Values).Aggregation);
    }
}
