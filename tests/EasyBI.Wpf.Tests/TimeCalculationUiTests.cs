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
        Assert.Equal(["Nothing", "Previous period", "Same period last year", "Earlier by…", "Same weekday last year", "New named period…"], form["Period"]!.Choices.Select(c => c.Label));
        Assert.DoesNotContain(TimeIds.Running, form["Accumulation"]!.Choices.Select(c => c.Value));
    }

    // --- Entries of the report

    private static DesignerPropertySection Section(DesignerViewModel designer) =>
        designer.Properties.Single(s => s.Key == DesignerPropertySections.Time);

    [Fact]
    public void A_named_period_created_in_the_designer_is_saved_in_the_report_and_shared()
    {
        var (designer, form) = TimeForm();

        form["Period"]!.Value = "newPeriod";

        var period = Assert.Single(designer.Context.Comparisons.Periods);
        Assert.StartsWith(TimeIds.ReportPrefix, period.Id);
        Assert.Equal(period.Id, designer.Visual!.Values[0].Time!.Period);
        form = Section(designer);
        Assert.Contains(period.Id, form["Period"]!.Choices.Select(c => c.Value));
        Assert.True(form["PeriodName"]!.IsAvailable);
        Assert.False(form["Count"]!.IsAvailable);

        form["PeriodName"]!.Value = "Il y a 3 ans";
        form["PeriodCount"]!.Value = 3d;

        Assert.Equal(("Il y a 3 ans", 3, TimeUnit.Year), (designer.Context.Comparisons.Periods[0].Caption, designer.Context.Comparisons.Periods[0].Count, designer.Context.Comparisons.Periods[0].Unit));
        // Renaming does not rebuild the panel: the text box being typed in stays
        Assert.Same(form, Section(designer));
        Assert.Contains("Il y a 3 ans", designer.Values[0].Badges);
        Assert.Equal("Il y a 3 ans", form["Period"]!.SelectedChoice!.Label);
    }

    [Fact]
    public void Deleting_a_period_of_the_report_stops_the_comparison()
    {
        var (designer, form) = TimeForm();
        form["Period"]!.Value = "newPeriod";

        ((DesignerAction)Section(designer)["RemovePeriod"]!.Value!).Command.Execute(null);

        Assert.Empty(designer.Context.Comparisons.Periods);
        Assert.Null(designer.Visual!.Values[0].Time);
        Assert.False(Section(designer)["PeriodName"]!.IsAvailable);
    }

    [Fact]
    public void A_presentation_created_in_the_designer_is_written_as_a_formula_checked_as_typed()
    {
        var (designer, form) = TimeForm();
        form["Period"]!.Value = TimeIds.LastYear;
        Section(designer)["Show"]!.Value = "newOutput";

        var output = Assert.Single(designer.Context.Comparisons.Outputs);
        Assert.Equal(output.Id, designer.Visual!.Values[0].Time!.Show);
        form = Section(designer);
        Assert.Equal("[Value] / [Compared] * 100", form["Formula"]!.Value);

        form["Formula"]!.Value = "[Value] - [Amount]";

        Assert.Equal(output.Formula, designer.Context.Comparisons.Outputs[0].Formula);
        Assert.Equal("[Value] - [Amount]", form["Formula"]!.Value);
        Assert.True(form["FormulaProblem"]!.IsAvailable);
        Assert.Contains("Amount", (string)form["FormulaProblem"]!.Value!);

        form["Formula"]!.Value = "[Value] - [Compared]";
        form["OutputIsChange"]!.Value = true;
        form["OutputFormat"]!.Value = "P1";

        Assert.Equal(output with { Formula = "[Value] - [Compared]", IsChange = true, Format = "P1" }, designer.Context.Comparisons.Outputs[0]);
        Assert.False(form["FormulaProblem"]!.IsAvailable);

        ((DesignerAction)form["RemoveOutput"]!.Value!).Command.Execute(null);

        Assert.Empty(designer.Context.Comparisons.Outputs);
        Assert.Equal(TimeIds.Value, designer.Visual!.Values[0].Time!.Show);
    }

    [Fact]
    public void A_dashboard_loads_and_saves_the_entries_of_the_report() => Sta.Run(() =>
    {
        var period = new ReportComparisonPeriod { Id = "report.p", Caption = "P", Count = 2 };
        var output = new ReportComparisonOutput { Id = "report.o", Caption = "O", Formula = "[Value]" };
        var context = EasyBIContext.Create();
        var view = new Dashboard.DashboardView { Context = context };

        view.Load(new Dashboard.DashboardDocument { Report = new ReportDefinition { ComparisonPeriods = [period], ComparisonOutputs = [output] } });
        Assert.Equal([period], context.Comparisons.Periods);

        context.Comparisons.Set(output with { Caption = "O2" });
        var saved = view.Capture().Report;
        Assert.Equal([period], saved.ComparisonPeriods);
        Assert.Equal("O2", Assert.Single(saved.ComparisonOutputs).Caption);

        var early = new Dashboard.DashboardView();
        early.Load(new Dashboard.DashboardDocument { Report = new ReportDefinition { ComparisonPeriods = [period] } });
        Assert.Equal([period], early.Capture().Report.ComparisonPeriods);
        var later = EasyBIContext.Create();
        early.Context = later;
        Assert.Equal([period], later.Comparisons.Periods);
    });

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
