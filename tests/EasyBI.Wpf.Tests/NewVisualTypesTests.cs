using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2;
using EasyBI.Renderers.OxyPlot;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;
using LiveChartsCore.SkiaSharpView.WPF;
using OxyPlot.Wpf;

namespace EasyBI.Wpf.Tests;

/// <summary>The donut, the gauge and the waterfall.</summary>
public class NewVisualTypesTests
{
    private static RenderRequest Circular(VisualType type)
    {
        var visual = new VisualDefinition { Type = type, Axis = [new("Region")], Values = [new("Amount")] };
        var chart = new ChartData("Region", ["A", "B", "C"], [new ChartSeries("Revenue", [10, 20, 30.5], "N1") { MeasureKey = "Amount" }])
        {
            CategoryValues = [["A"], ["B"], ["C"]],
        };
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default);
    }

    [Fact]
    public void A_pie_is_a_full_disc_and_a_donut_shows_its_total_in_its_hole_with_LiveCharts2() => Sta.Run(() =>
    {
        var renderer = new LiveCharts2Renderer();
        Assert.IsType<PieChart>(renderer.Render(Circular(VisualType.Pie)));

        var donut = Assert.IsType<ChartCenterLabel>(renderer.Render(Circular(VisualType.Donut)));

        Assert.Equal(("60.5", "Revenue"), (donut.Value.Text, donut.Caption.Text));
        Assert.Contains(donut.Children.OfType<PieChart>(), _ => true);
    });

    [Fact]
    public void A_pie_is_a_full_disc_and_a_donut_has_a_hole_with_OxyPlot() => Sta.Run(() =>
    {
        var renderer = new OxyPlotRenderer();
        var pie = (PlotView)renderer.Render(Circular(VisualType.Pie));
        Assert.Equal(0, ((global::OxyPlot.Series.PieSeries)pie.Model.Series[0]).InnerDiameter);

        var donut = Assert.IsType<ChartCenterLabel>(renderer.Render(Circular(VisualType.Donut)));

        var view = donut.Children.OfType<PlotView>().Single();
        Assert.Equal(0.5, ((global::OxyPlot.Series.PieSeries)view.Model.Series[0]).InnerDiameter);
        Assert.Equal("60.5", donut.Value.Text);
    });

    [Fact]
    public void Clicking_a_slice_of_a_donut_selects_its_category()
    {
        var request = Circular(VisualType.Donut);
        var filters = new FilterContext();

        filters.Select(request, new CategorySelection(1));

        Assert.Equal(["B"], filters.Selections.Single().Filters.Single().Values);
    }

    [Fact]
    public void The_designer_offers_the_donut_with_a_category_and_a_value()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().UseRenderer(new LiveCharts2Renderer()));

        designer.SelectedType = VisualType.Donut;

        Assert.Contains(designer.TypeOptions, o => o.Value == VisualType.Donut);
        Assert.Equal(("Category", "Value", false), (designer.AxisWell.Label, designer.ValuesWell.Label, designer.LegendWell.IsUsed));
        Assert.Equal("A pie or a donut needs one category and a single value.", VisualValidator.Validate(new VisualDefinition { Type = VisualType.Donut, Values = [new("Amount")] }));
        Assert.Null(VisualValidator.Validate(new VisualDefinition { Type = VisualType.Donut, Axis = [new("Region")], Values = [new("Amount")] }));
    }

    private static RenderRequest Gauge(double? value, GaugeSettings? settings = null, ValueFormat? format = null)
    {
        var visual = new VisualDefinition { Type = VisualType.Gauge, Values = [new("Amount") { Format = format }], Gauge = settings };
        var chart = new ChartData(null, ["Total"], [new ChartSeries("Revenue", [value], "N0") { MeasureKey = "Amount" }]);
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default with { Error = "#CC0000" });
    }

    private static System.Windows.Controls.Canvas Drawing(System.Windows.FrameworkElement gauge) =>
        (System.Windows.Controls.Canvas)((System.Windows.Controls.Viewbox)gauge).Child;

    private static IEnumerable<string> Texts(System.Windows.Controls.Canvas canvas) =>
        canvas.Children.OfType<System.Windows.Controls.TextBlock>()
            .Concat(canvas.Children.OfType<System.Windows.Controls.StackPanel>().SelectMany(p => p.Children.Cast<object>())
                .Select(c => c is System.Windows.Controls.Viewbox box ? box.Child : c).OfType<System.Windows.Controls.TextBlock>())
            .Select(t => new System.Windows.Documents.TextRange(t.ContentStart, t.ContentEnd).Text);

    [Fact]
    public void A_gauge_fills_its_arc_up_to_the_value_and_marks_its_target() => Sta.Run(() =>
    {
        var canvas = Drawing(new GaugeRenderer().Render(Gauge(750, new GaugeSettings { Maximum = 1000, Target = 900 })));

        var arcs = canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
        Assert.Equal(2, arcs.Length); // the track and the value
        var end = ((System.Windows.Media.ArcSegment)((System.Windows.Media.PathGeometry)arcs[1].Data).Figures[0].Segments[0]).Point;
        Assert.Equal(120 + 96 * Math.Cos(Math.PI * 0.25), end.X, 6); // three quarters of the way
        Assert.Single(canvas.Children.OfType<System.Windows.Shapes.Line>()); // the target
        Assert.Equal(["0", "1,000", "Revenue", "750", "Target: 900"], Texts(canvas));
    });

    [Fact]
    public void A_gauge_takes_the_color_of_its_format() => Sta.Run(() =>
    {
        var format = new ValueFormat { Rules = [new FormatRule { Operator = FilterOperator.LessThan, Value = 800, Color = ThemeColors.Error }] };

        var canvas = Drawing(new GaugeRenderer().Render(Gauge(750, format: format)));

        var value = (System.Windows.Media.SolidColorBrush)canvas.Children.OfType<System.Windows.Shapes.Path>().Last().Stroke;
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xCC, 0, 0), value.Color);
    });

    [Fact]
    public void The_designer_edits_the_scale_of_a_gauge_and_keeps_it_for_gauges_only()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new { Region = "Nord", Amount = 1m } }, "sales").Dimension(s => s.Region, "Région").Measure(s => s.Amount, "CA")));
        designer.Load(new VisualDefinition { Model = "sales", Type = VisualType.Gauge, Values = [new("Amount")] });
        Assert.Equal((false, false), (designer.AxisWell.IsUsed, designer.LegendWell.IsUsed));
        var visual = designer.Properties.Single(s => s.Key == DesignerPropertySections.Visual);
        Assert.True(visual["GaugeTarget"]!.IsAvailable);

        designer.GaugeMaximum = 2000;
        designer.GaugeTarget = 1500;
        Assert.Equal((2000d, 1500d), (designer.Visual!.Gauge!.Maximum!.Value, designer.Visual.Gauge.Target!.Value));

        designer.SelectedType = VisualType.Kpi;
        Assert.Null(designer.Visual!.Gauge);
        Assert.False(designer.Properties.Single(s => s.Key == DesignerPropertySections.Visual)["GaugeTarget"]!.IsAvailable);
        designer.SelectedType = VisualType.Gauge; // kept while the type changes
        Assert.Equal(1500d, designer.Visual!.Gauge!.Target);
    }

    [Fact]
    public void A_gauge_is_drawn_by_the_built_in_renderer()
    {
        Assert.IsType<GaugeRenderer>(EasyBIContext.Create().UseRenderer(new LiveCharts2Renderer()).ResolveRenderer(VisualType.Gauge));
    }

    private static RenderRequest WaterfallRequest(params DataSelection[] highlight)
    {
        var visual = new VisualDefinition { Type = VisualType.Waterfall, Axis = [new("Month")], Values = [new("Profit")] };
        var chart = new ChartData("Month", ["Jan", "Feb"], [new ChartSeries("Profit", [100, -30], "N0") { MeasureKey = "Profit", SeriesType = VisualType.Waterfall }])
        {
            CategoryValues = [["Jan"], ["Feb"]],
        };
        return new RenderRequest(visual, new ResultSet([], []), chart, BiTheme.Default with { Success = "#00AA00", Error = "#CC0000" })
        {
            Highlight = highlight,
        };
    }

    [Fact]
    public void LiveCharts2_draws_a_waterfall_as_candles_from_one_running_total_to_the_next() => Sta.Run(() =>
    {
        var view = (CartesianChart)new LiveCharts2Renderer().Render(WaterfallRequest());

        var steps = Assert.IsType<LiveChartsCore.SkiaSharpView.CandlesticksSeries<LiveChartsCore.Defaults.FinancialPointI>>(Assert.Single(view.Series));
        Assert.Equal([(100d, 0d, 100d), (100d, 100d, 70d), (70d, 0d, 70d)], steps.Values!.Select(p => (p.High, p.Open, p.Close)));
        Assert.Equal(["Jan", "Feb", "Total"], ((LiveChartsCore.SkiaSharpView.Axis)view.XAxes.First()).Labels);
        Assert.Equal("#ff00aa00", ((LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint)steps.UpFill!).Color.ToString());
        Assert.Equal("#ffcc0000", ((LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint)steps.DownFill!).Color.ToString());
    });

    [Fact]
    public void OxyPlot_draws_a_waterfall_with_bars_between_running_totals() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(WaterfallRequest(new CategorySelection(0)));

        var steps = Assert.IsType<global::OxyPlot.Series.IntervalBarSeries>(Assert.Single(view.Model.Series));
        Assert.Equal([(0d, 100d), (70d, 100d), (0d, 70d)], steps.Items.Select(i => (i.Start, i.End)));
        Assert.Equal(global::OxyPlot.OxyColor.Parse("#00AA00"), steps.Items[0].Color);
        Assert.True(steps.Items[1].Color.A < 255); // the decrease, left out of the highlight
        Assert.Equal(global::OxyPlot.OxyColor.Parse(BiTheme.Default.ColorAt(0)), steps.Items[2].Color); // the total
    });

    [Fact]
    public void OxyPlot_renders_a_waterfall() => Sta.Run(() =>
    {
        var view = (PlotView)new OxyPlotRenderer().Render(WaterfallRequest());

        // OxyPlot shows a rendering error inside the image instead of throwing: draw it and look for none
        using var image = new System.IO.MemoryStream();
        new global::OxyPlot.Wpf.PngExporter { Width = 400, Height = 300 }.Export(view.Model, image);
        Assert.Null(view.Model.GetLastPlotException());
    });

    [Fact]
    public void The_designer_offers_the_waterfall_with_an_axis_and_a_value()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().UseRenderer(new LiveCharts2Renderer()));

        designer.SelectedType = VisualType.Waterfall;

        Assert.Equal((true, false, "Value"), (designer.AxisWell.IsUsed, designer.LegendWell.IsUsed, designer.ValuesWell.Label));
        Assert.True(designer.TypeOptions.Single(o => o.Value == VisualType.Waterfall).IsAvailable);
    }
}
