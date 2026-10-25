using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

public class TimeCalculationUiTests
{
    private sealed record Sale(string Region, DateTime Date, decimal Amount);

    [Fact]
    public void Measures_of_the_values_shelf_are_seen_through_time_in_the_designer()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", DateTime.Today, 1m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "CA")));
        designer.Load(new VisualDefinition
        {
            Model = "sales",
            Axis = [new("Date", DateGrain.Year)],
            Values = [new("Amount"), new("Amount") { Time = TimeCalculation.PreviousYear }],
        });

        Assert.Equal([TimeCalculation.None, TimeCalculation.PreviousYear], designer.Values.Select(v => v.Time));
        Assert.True(designer.Values[0].IsValue);
        Assert.False(designer.Axis[0].IsValue);

        designer.Values[0].Time = TimeCalculation.YearToDate;

        Assert.Equal([TimeCalculation.YearToDate, TimeCalculation.PreviousYear], designer.Visual!.Values.Select(v => v.Time));
    }

    private static string KpiText(TimeCalculation time, double value)
    {
        var visual = new VisualDefinition { Type = VisualType.Kpi, Values = [new("Amount") { Time = time }] };
        var request = new RenderRequest(visual, new ResultSet([], []), new ChartData(null, ["Total"], [new ChartSeries("CA", [value], "P1")]), BiTheme.Default);
        var panel = (WrapPanel)((Viewbox)new KpiRenderer().Render(request)).Child;
        return ((TextBlock)((StackPanel)panel.Children[0]).Children[0]).Text;
    }

    [Fact]
    public void A_kpi_shows_the_trend_of_a_change() => Sta.Run(() =>
    {
        Assert.StartsWith("▲ ", KpiText(TimeCalculation.PercentChangeFromPreviousYear, 0.12));
        Assert.StartsWith("▼ ", KpiText(TimeCalculation.ChangeFromPreviousYear, -3));
        Assert.DoesNotContain("▲", KpiText(TimeCalculation.None, 0.12));
    });
}
