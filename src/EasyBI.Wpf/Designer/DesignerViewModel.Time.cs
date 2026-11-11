using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

// The "Over time" section of a measure: the catalog's entries, and those the end user adds to the report
public sealed partial class DesignerViewModel
{
    // Picked in a list, these create an entry of the report and select it
    private const string NewPeriod = "\u0001newPeriod";
    private const string NewOutput = "\u0001newOutput";

    /// <summary>The formats offered for a presentation of the report: null keeps the measure's.</summary>
    private static readonly IReadOnlyList<Option<string?>> OutputFormats =
    [
        new(null, () => Strings.TimeFormatMeasure),
        new("N0", () => Strings.TimeFormatNumber),
        new("N1", () => Strings.TimeFormatNumber1),
        new("N2", () => Strings.TimeFormatNumber2),
        new("P0", () => Strings.TimeFormatPercent),
        new("P1", () => Strings.TimeFormatPercent1),
    ];

    // The ids of the report's entries the sections were built with: adding or removing one rebuilds them
    private string[] _comparisonIds = [];

    // The formula being typed while it is not valid, and its problem
    private (string Id, string Text, string Problem)? _typedFormula;

    /// <summary>
    /// How a measure is seen through time, from the entries of the context's catalog and of the report: added up,
    /// compared with a period (how far back, for an offset) and what the comparison shows, and which way its changes are
    /// good. A period or a presentation of the report is edited here, for every visual using it.
    /// </summary>
    private DesignerPropertySection TimeSection(ShelfItemViewModel chip)
    {
        var time = Context.ReportTime;
        var comparisons = Context.Comparisons;
        _comparisonIds = ComparisonIds();
        IReadOnlyList<Option<string?>> accumulations = [new(null, () => Strings.TimeNoAccumulation), .. time.Accumulations.Select(a => new Option<string?>(a.Id, a.Caption))];
        IReadOnlyList<Option<string?>> periods =
        [
            new(null, () => Strings.TimeNoComparison),
            // An entry of the report is shown by its current name: renaming it does not rebuild the list
            .. time.Periods.Select(p => new Option<string?>(p.Id, () => comparisons.Periods.FirstOrDefault(r => r.Id == p.Id)?.Caption ?? p.Caption())),
            new(NewPeriod, () => Strings.TimeNewPeriod),
        ];
        IReadOnlyList<Option<string>> outputs =
        [
            .. time.Outputs.Select(o => new Option<string>(o.Id, () => comparisons.Outputs.FirstOrDefault(r => r.Id == o.Id)?.Caption ?? o.Caption())),
            new(NewOutput, () => Strings.TimeNewOutput),
        ];
        ReportComparisonPeriod? Period() => chip.TimePeriod is { } id ? comparisons.Periods.FirstOrDefault(p => p.Id == id) : null;
        ReportComparisonOutput? Output() => chip.TimePeriod is not null ? comparisons.Outputs.FirstOrDefault(o => o.Id == chip.TimeShow) : null;

        return new DesignerPropertySection(DesignerPropertySections.Time, () => Strings.PropertyTime,
        [
            DesignerProperty.Choice("Accumulation", () => Strings.PropertyTimeAccumulation, accumulations,
                () => chip.TimeAccumulation, v => chip.TimeAccumulation = v, description: () => Strings.TimeAccumulationHint),
            DesignerProperty.Choice("Period", () => Strings.PropertyTimePeriod, periods,
                () => chip.TimePeriod, v => chip.TimePeriod = v == NewPeriod ? AddPeriod() : v, description: () => Strings.TimeHint),
            DesignerProperty.Number("Count", () => Strings.PropertyTimeCount, () => chip.TimeCount, v => chip.TimeCount = (int)Math.Round(v ?? 1),
                () => chip.HasTimeOffset, () => Strings.TimeOffsetHint),
            DesignerProperty.Choice("Unit", () => Strings.PropertyTimeUnit, ShelfItemViewModel.TimeUnitOptions,
                () => chip.TimeUnit, v => chip.TimeUnit = v, () => chip.HasTimeOffset),

            // A period of the report: changed for every visual comparing with it
            DesignerProperty.Text("PeriodName", () => Strings.PropertyTimePeriodName, () => Period()?.Caption ?? "",
                v => Change(Period(), p => p with { Caption = v }), () => Period() is not null, () => Strings.TimeReportEntryHint),
            DesignerProperty.Number("PeriodCount", () => Strings.PropertyTimeCount, () => Period()?.Count,
                v => Change(Period(), p => p with { Count = Math.Max(1, (int)Math.Round(v ?? 1)) }), () => Period() is not null, () => Strings.TimeOffsetHint),
            DesignerProperty.Choice("PeriodUnit", () => Strings.PropertyTimeUnit, ShelfItemViewModel.TimeUnitOptions,
                () => Period()?.Unit ?? TimeUnit.Year, v => Change(Period(), p => p with { Unit = v }), () => Period() is not null),
            DesignerProperty.Custom("RemovePeriod", () => Strings.TimeRemovePeriod,
                new DesignerAction(() => Strings.TimeRemovePeriod, () => Remove(Period()?.Id, () => chip.TimePeriod = null)), () => Period() is not null),

            DesignerProperty.Choice("Show", () => Strings.PropertyTimeShow, outputs,
                () => chip.TimeShow, v => chip.TimeShow = v == NewOutput ? AddOutput() : v, () => chip.TimePeriod is not null),

            // A presentation of the report: changed for every visual showing it
            DesignerProperty.Text("OutputName", () => Strings.PropertyTimeOutputName, () => Output()?.Caption ?? "",
                v => Change(Output(), o => o with { Caption = v }), () => Output() is not null, () => Strings.TimeReportEntryHint),
            DesignerProperty.Text("Formula", () => Strings.PropertyTimeFormula, () => FormulaText(Output()), v => SetFormula(Output(), v),
                () => Output() is not null, () => Strings.Format(Strings.TimeFormulaHint, ComparisonFormula.ValueCaption, ComparisonFormula.ComparedCaption)),
            DesignerProperty.Info("FormulaProblem", () => "", () => FormulaProblem(Output()), () => FormulaProblem(Output()) is not null),
            DesignerProperty.Choice("OutputFormat", () => Strings.PropertyTimeOutputFormat, OutputFormats,
                () => Output()?.Format, v => Change(Output(), o => o with { Format = v }), () => Output() is not null),
            DesignerProperty.Toggle("OutputIsChange", () => Strings.PropertyTimeOutputIsChange, () => Output()?.IsChange == true,
                v => Change(Output(), o => o with { IsChange = v }), () => Output() is not null, () => Strings.TimeOutputIsChangeHint),
            DesignerProperty.Custom("RemoveOutput", () => Strings.TimeRemoveOutput,
                new DesignerAction(() => Strings.TimeRemoveOutput, () => Remove(Output()?.Id, () => chip.TimeShow = TimeIds.Value)), () => Output() is not null),

            DesignerProperty.Choice("Favorable", () => Strings.PropertyFavorable, ShelfItemViewModel.FavorableOptions,
                () => chip.Favorable ?? chip.Field.Favorable, v => chip.Favorable = v, description: () => Strings.FavorableHint),
        ]);
    }

    private string[] ComparisonIds() =>
        [.. Context.Comparisons.Periods.Select(p => p.Id), .. Context.Comparisons.Outputs.Select(o => o.Id)];

    private string AddPeriod()
    {
        var period = new ReportComparisonPeriod { Id = ComparisonContext.NewId(), Caption = Strings.TimeNewPeriodCaption, Count = 2, Unit = TimeUnit.Year };
        Context.Comparisons.Set(period);
        return period.Id;
    }

    private string AddOutput()
    {
        var output = new ReportComparisonOutput
        {
            Id = ComparisonContext.NewId(),
            Caption = Strings.TimeNewOutputCaption,
            Formula = $"[{ComparisonFormula.Value}] / [{ComparisonFormula.Compared}] * 100",
            Format = "N1",
        };
        Context.Comparisons.Set(output);
        return output.Id;
    }

    private void Change(ReportComparisonPeriod? period, Func<ReportComparisonPeriod, ReportComparisonPeriod> change)
    {
        if (period is not null && change(period) is var changed && changed != period) Context.Comparisons.Set(changed);
    }

    private void Change(ReportComparisonOutput? output, Func<ReportComparisonOutput, ReportComparisonOutput> change)
    {
        if (output is not null && change(output) is var changed && changed != output) Context.Comparisons.Set(changed);
    }

    // The visuals using it show empty values; this one stops using it first
    private void Remove(string? id, Action unselect)
    {
        if (id is null) return;
        unselect();
        Context.Comparisons.Remove(id);
    }

    private string FormulaText(ReportComparisonOutput? output) =>
        output is null ? "" : _typedFormula is { } typed && typed.Id == output.Id ? typed.Text : ComparisonFormula.ToDisplay(output.Formula);

    private string? FormulaProblem(ReportComparisonOutput? output) =>
        output is not null && _typedFormula is { } typed && typed.Id == output.Id ? typed.Problem : null;

    // A valid formula changes the presentation; another one is kept as typed, with its problem, until it is
    private void SetFormula(ReportComparisonOutput? output, string text)
    {
        if (output is null) return;
        if (ComparisonFormula.Check(text, out var stored) is { } problem)
        {
            _typedFormula = (output.Id, text, problem.Message);
            foreach (var property in Properties.SelectMany(s => s.Properties)) property.Refresh();
            return;
        }
        _typedFormula = null;
        if (stored != output.Formula) Context.Comparisons.Set(output with { Formula = stored! });
        else foreach (var property in Properties.SelectMany(s => s.Properties)) property.Refresh();
    }

    /// <summary>
    /// An entry of the report was added, changed or removed: the chips show its new name, and the properties panel offers
    /// the entries there are (rebuilt when one was added or removed, only read again otherwise, so that typing a name
    /// is not interrupted).
    /// </summary>
    private void OnComparisonsChanged(object? sender, EventArgs e)
    {
        var time = Context.ReportTime;
        foreach (var chip in Values.Concat(TooltipValues)) chip.TimeCatalog = time;
        if (ComparisonIds().SequenceEqual(_comparisonIds))
        {
            foreach (var property in Properties.SelectMany(s => s.Properties)) property.Refresh();
            return;
        }
        _propertiesShape = null;
        SyncProperties();
    }
}

/// <summary>A button of the properties panel, such as the one removing a period of the report.</summary>
public sealed class DesignerAction(Func<string> label, Action execute)
{
    public string Label => label();

    public ICommand Command { get; } = new RelayCommand(execute, () => true);
}
