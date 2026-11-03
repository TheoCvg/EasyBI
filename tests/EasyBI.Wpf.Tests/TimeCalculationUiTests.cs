using System.Windows.Controls;
using System.Windows.Media;
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
            Values = [new("Amount"), new("Amount") { Time = MeasureTime.PreviousYear }],
        });

        Assert.Equal([null, MeasureTime.PreviousYear], designer.Values.Select(v => v.Time));
        Assert.True(designer.Values[0].IsValue);
        Assert.False(designer.Axis[0].IsValue);

        designer.Values[0].Time = MeasureTime.YearToDate;

        Assert.Equal([MeasureTime.YearToDate, MeasureTime.PreviousYear], designer.Visual!.Values.Select(v => v.Time));
    }

    private sealed record Spending(DateTime Date, decimal Amount, decimal Cost);

    private static (DesignerViewModel Designer, DesignerPropertySection Time) TimeForm(TimeCatalog? catalog = null)
    {
        var context = EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Spending(DateTime.Today, 1m, 1m) }, "sales")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "CA")
                .Measure(s => s.Cost, "Coûts", favorable: Favorable.Lower));
        if (catalog is not null) context.Time = catalog;
        var designer = new DesignerViewModel(context);
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Date", DateGrain.Month)], Values = [new("Amount"), new("Cost")] });
        designer.SelectedItem = designer.Values[0];
        return (designer, designer.Properties.Single(s => s.Key == DesignerPropertySections.Time));
    }

    [Fact]
    public void The_time_form_builds_the_calculation_part_by_part()
    {
        var (designer, form) = TimeForm();
        Assert.False(form["Count"]!.IsAvailable);
        Assert.False(form["Show"]!.IsAvailable);

        form["Period"]!.Value = TimeIds.Offset;
        Assert.True(form["Count"]!.IsAvailable);
        form["Count"]!.Value = 3d;
        form["Unit"]!.Value = TimeUnit.Month;
        form["Show"]!.Value = TimeIds.PercentChange;
        form["Accumulation"]!.Value = TimeIds.YearToDate;

        var expected = new MeasureTime { Accumulation = TimeIds.YearToDate, Period = TimeIds.Offset, Count = 3, Unit = TimeUnit.Month, Show = TimeIds.PercentChange };
        Assert.Equal(expected, designer.Visual!.Values[0].Time);
        Assert.Contains("Cum. Δ% M-3", designer.Values[0].Badges);

        form["Accumulation"]!.Value = null;
        form["Period"]!.Value = null;
        Assert.Null(designer.Visual!.Values[0].Time);
    }

    [Fact]
    public void The_time_form_offers_the_entries_of_the_context_catalog()
    {
        var catalog = new TimeCatalog().Add(new ComparisonPeriod("retail.week364", () => "Same weekday last year", _ => new TimeShift(0, -364)));
        catalog.RemoveAccumulation(TimeIds.Running);

        var (_, form) = TimeForm(catalog);

        Assert.Contains("retail.week364", form["Period"]!.Choices.Select(c => c.Value));
        Assert.Equal(["Nothing", "Previous period", "Same period last year", "Earlier by…", "Same weekday last year"], form["Period"]!.Choices.Select(c => c.Label));
        Assert.DoesNotContain(TimeIds.Running, form["Accumulation"]!.Choices.Select(c => c.Value));
    }

    [Fact]
    public void The_favorable_direction_starts_as_the_model_declares_it_and_is_saved_when_it_differs()
    {
        var (designer, _) = TimeForm();
        designer.SelectedItem = designer.Values[1];
        var favorable = designer.Properties.Single(s => s.Key == DesignerPropertySections.Time)["Favorable"]!;
        Assert.Equal(Favorable.Lower, favorable.Value);

        favorable.Value = Favorable.Higher;
        Assert.Equal(Favorable.Higher, designer.Visual!.Values[1].Favorable);

        favorable.Value = Favorable.Lower;
        Assert.Null(designer.Visual!.Values[1].Favorable);
    }

    // The KPI's value, in a host holding EasyBI's theme so that its colors resolve
    private static (string Text, Color Color, Border Host) Kpi(Favorable? change, double value)
    {
        var visual = new VisualDefinition { Type = VisualType.Kpi, Values = [new("Amount")] };
        var request = new RenderRequest(visual, new ResultSet([], []), new ChartData(null, ["Total"], [new ChartSeries("CA", [value], "P1") { Change = change }]), BiTheme.Default);
        var host = new Border { Child = new KpiRenderer().Render(request) };
        host.Resources.MergedDictionaries.Add(new EasyBIThemeDictionary());
        var panel = (WrapPanel)((Viewbox)host.Child).Child;
        var text = (TextBlock)((StackPanel)panel.Children[0]).Children[0];
        return (text.Text, ((SolidColorBrush)text.Foreground).Color, host);
    }

    private static Color Themed(Border host, object key) => ((SolidColorBrush)host.FindResource(key)).Color;

    [Fact]
    public void A_kpi_shows_the_trend_of_a_change() => Sta.Run(() =>
    {
        Assert.StartsWith("▲ ", Kpi(Favorable.Higher, 0.12).Text);
        Assert.StartsWith("▼ ", Kpi(Favorable.Higher, -3).Text);
        Assert.DoesNotContain("▲", Kpi(null, 0.12).Text);
    });

    [Fact]
    public void A_rise_of_a_measure_whose_favorable_direction_is_down_shows_in_the_error_color() => Sta.Run(() =>
    {
        var cost = Kpi(Favorable.Lower, 0.12);
        var revenue = Kpi(Favorable.Higher, 0.12);
        var saving = Kpi(Favorable.Lower, -0.12);

        Assert.Equal(Themed(cost.Host, EasyBITheme.ErrorBrushKey), cost.Color);
        Assert.Equal(Themed(revenue.Host, EasyBITheme.MeasureBrushKey), revenue.Color);
        Assert.Equal(Themed(saving.Host, EasyBITheme.MeasureBrushKey), saving.Color);
    });
}
