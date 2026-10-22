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
    private static RenderRequest Circular(MarkType mark)
    {
        var visual = new VisualDefinition { Mark = mark, Axis = [new("Region")], Values = [new("Amount")] };
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
        Assert.IsType<PieChart>(renderer.Render(Circular(MarkType.Pie)));

        var donut = Assert.IsType<ChartCenterLabel>(renderer.Render(Circular(MarkType.Donut)));

        Assert.Equal(("60.5", "Revenue"), (donut.Value.Text, donut.Caption.Text));
        Assert.Contains(donut.Children.OfType<PieChart>(), _ => true);
    });

    [Fact]
    public void A_pie_is_a_full_disc_and_a_donut_has_a_hole_with_OxyPlot() => Sta.Run(() =>
    {
        var renderer = new OxyPlotRenderer();
        var pie = (PlotView)renderer.Render(Circular(MarkType.Pie));
        Assert.Equal(0, ((global::OxyPlot.Series.PieSeries)pie.Model.Series[0]).InnerDiameter);

        var donut = Assert.IsType<ChartCenterLabel>(renderer.Render(Circular(MarkType.Donut)));

        var view = donut.Children.OfType<PlotView>().Single();
        Assert.Equal(0.5, ((global::OxyPlot.Series.PieSeries)view.Model.Series[0]).InnerDiameter);
        Assert.Equal("60.5", donut.Value.Text);
    });

    [Fact]
    public void Clicking_a_slice_of_a_donut_selects_its_category()
    {
        var request = Circular(MarkType.Donut);
        var filters = new FilterContext();

        filters.Select(request, new CategorySelection(1));

        Assert.Equal(["B"], filters.Selections.Single().Filters.Single().Values);
    }

    [Fact]
    public void The_designer_offers_the_donut_with_a_category_and_a_value()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().UseRenderer(new LiveCharts2Renderer()));

        designer.SelectedMark = MarkType.Donut;

        Assert.Contains(designer.MarkOptions, o => o.Value == MarkType.Donut);
        Assert.Equal(("Category", "Value", false), (designer.AxisWell.Label, designer.ValuesWell.Label, designer.LegendWell.IsUsed));
        Assert.Equal("A pie or a donut needs one category and a single value.", VisualValidator.Validate(new VisualDefinition { Mark = MarkType.Donut, Values = [new("Amount")] }));
        Assert.Null(VisualValidator.Validate(new VisualDefinition { Mark = MarkType.Donut, Axis = [new("Region")], Values = [new("Amount")] }));
    }
}
