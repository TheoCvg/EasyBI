using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>Calculated measures the end user creates in the designer, shared by the report's field lists and visuals.</summary>
public class CalculatedMeasureUiTests
{
    private sealed record Sale(string Region, decimal Amount, decimal Margin);

    private static readonly Sale[] Sales = [new("Nord", 100m, 30m), new("Nord", 300m, 60m), new("Sud", 200m, 100m)];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Measure(s => s.Amount, "Chiffre d'affaires")
            .Measure(s => s.Margin, "Marge");

    private static CalculatedMeasure Rate(string formula = "[Margin] / [Amount]") =>
        new() { Name = "calc_rate", Caption = "Taux de marge", Model = "sales", Formula = formula, Format = "P1" };

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // --- The editor

    [Fact]
    public async Task A_new_measure_is_typed_with_captions_previewed_and_saved_with_field_names()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var editor = new CalculatedMeasureEditor(context, context.Models[0])
        {
            Caption = "Taux de marge",
            Formula = "[Marge] / [Chiffre d'affaires]",
        };
        editor.Format = editor.FormatOptions.Single(o => o.Value == "P1");
        await editor.PreviewTask;

        Assert.True(editor.CanSave);
        Assert.Equal((190d / 600d).ToString("P1", EasyBILocalization.FormatCulture), editor.Preview);
        Assert.True(editor.Save());
        var saved = Assert.Single(context.Measures.Measures);
        Assert.Equal(("Taux de marge", "[Margin] / [Amount]", "P1", "sales"), (saved.Caption, saved.Formula, saved.Format, saved.Model));
        Assert.StartsWith("calc_", saved.Name);
    }

    [Fact]
    public void The_editor_explains_what_prevents_saving()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var editor = new CalculatedMeasureEditor(context, context.Models[0]) { Formula = "[Marge] / [Chiffre d'affaires]" };
        Assert.Equal("Give the measure a name.", editor.Error);

        editor.Caption = "marge";
        Assert.Equal("Another measure is already named “marge”.", editor.Error);

        editor.Caption = "Taux";
        editor.Formula = "[Marge] / ";
        Assert.Contains("is expected at position", editor.Error);
        Assert.False(editor.CanSave);
        Assert.False(editor.Save());
        Assert.Empty(context.Measures.Measures);
    }

    [Fact]
    public async Task The_editor_offers_functions_and_dimensions_read_row_by_row()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var editor = new CalculatedMeasureEditor(context, context.Models[0]) { Caption = "CA Nord" };

        Assert.Equal(["[Région]"], editor.InsertableDimensions);
        Assert.Contains(editor.Functions, f => f.Name == "DISTINCTCOUNT");
        editor.Formula = "SUM([Chiffre d'affaires]; [Région] = \"Nord\")";
        await editor.PreviewTask;

        Assert.True(editor.CanSave);
        Assert.Equal("SUM([Amount]; [Region] = \"Nord\")", editor.Result!.Formula);
        Assert.Equal(ValueFormatter.Format(400d, null), editor.Preview);
        editor.Formula = "[Région]";
        Assert.StartsWith("[Région] is a dimension", editor.Error);
    }

    [Fact]
    public async Task Typing_the_caption_or_changing_the_format_does_not_query_the_model_again()
    {
        var model = new CountingModel(Model());
        var context = EasyBIContext.Create().AddModel(model);
        var editor = new CalculatedMeasureEditor(context, model) { Formula = "[Marge] / [Chiffre d'affaires]" };
        await editor.PreviewTask;
        Assert.Equal(1, model.Queries);

        foreach (var caption in new[] { "T", "Ta", "Tau", "Taux" }) editor.Caption = caption;
        editor.Format = editor.FormatOptions.Single(o => o.Value == "P1");
        await editor.PreviewTask;

        Assert.Equal(1, model.Queries);
        Assert.Equal((190d / 600d).ToString("P1", EasyBILocalization.FormatCulture), editor.Preview);
    }

    [Fact]
    public async Task Typing_a_formula_runs_one_preview_at_a_time_and_never_cancels_it()
    {
        var model = new GatedModel(Model());
        var context = EasyBIContext.Create().AddModel(model);
        var editor = new CalculatedMeasureEditor(context, model) { Caption = "Taux", Formula = "[Marge]" };
        var first = editor.PreviewTask;

        foreach (var formula in new[] { "[Marge] *", "[Marge] * 2", "[Marge] * 3" }) editor.Formula = formula;
        Assert.Equal(1, model.Queries);
        Assert.Null(editor.Preview);
        model.Release();
        await first;

        Assert.Equal(2, model.Queries);
        Assert.DoesNotContain(model.Tokens, token => token.IsCancellationRequested);
        Assert.Equal(ValueFormatter.Format(190d * 3, null), editor.Preview);
    }

    [Fact]
    public void Editing_a_measure_shows_captions_and_replaces_it()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        context.Measures.Set(Rate());
        var editor = new CalculatedMeasureEditor(context, context.Models[0], Rate());

        Assert.Equal("[Marge] / [Chiffre d'affaires]", editor.Formula);
        Assert.Equal("P1", editor.Format.Value);
        editor.Formula = "[Marge] / [Chiffre d'affaires] * 100";
        editor.Save();

        var saved = Assert.Single(context.Measures.Measures);
        Assert.Equal(("calc_rate", "[Margin] / [Amount] * 100"), (saved.Name, saved.Formula));
    }

    // --- Field lists and designers

    [Fact]
    public void Field_lists_offer_the_reports_calculated_measures_and_follow_their_changes() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var list = new FieldList { Context = context };
        context.Measures.Set(Rate());

        var fields = list.Measures.ToArray();
        Assert.Equal(["Chiffre d'affaires", "Marge", "Taux de marge"], fields.Select(f => f.Caption));
        Assert.True(fields[^1].IsReportMeasure);
        Assert.False(fields[0].IsReportMeasure);

        context.Measures.Remove("calc_rate");
        Assert.Equal(2, list.Measures.Count);
    });

    [Fact]
    public void The_designer_follows_a_renamed_or_deleted_measure()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        context.Measures.Set(Rate());
        var designer = new DesignerViewModel(context);
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount"), new("calc_rate")] });
        Assert.False(designer.Values[1].HasAggregation);

        context.Measures.Set(Rate() with { Caption = "Marge %" });
        Assert.Equal("Marge %", designer.Values[1].Caption);

        context.Measures.Remove("calc_rate");
        Assert.Equal(["Amount"], designer.Visual!.Values.Select(v => v.Measure));
    }

    // --- Measures of the model, redefined in the report

    private static CalculatedMeasure Redefinition(string formula, string caption = "Ventes") =>
        new() { Name = "Amount", Caption = caption, Model = "sales", Formula = formula };

    [Fact]
    public void The_field_list_edits_the_model_measures_and_restores_the_redefined_ones() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var list = new FieldList { Context = context };
        var amount = list.Measures[0];
        Assert.Equal((true, false, false), (amount.IsEditable, amount.IsRedefined, amount.IsReportMeasure));
        Assert.Equal("[Amount]", amount.Definition!.Formula);

        context.Measures.Set(Redefinition("AVERAGE([Amount])"));

        amount = list.Measures[0];
        Assert.Equal(["Ventes", "Marge"], list.Measures.Select(f => f.Caption));
        Assert.Equal((true, false), (amount.IsRedefined, amount.IsReportMeasure));
        Assert.Equal("AVERAGE([Amount])", amount.Definition!.Formula);
        Assert.True(FieldList.ResetMeasureCommand.CanExecute(amount, list));
        Assert.False(FieldList.DeleteMeasureCommand.CanExecute(amount, list));
    });

    [Fact]
    public void The_editor_redefines_a_model_measure_and_restores_it()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var model = context.Models[0];
        var editor = new CalculatedMeasureEditor(context, model, MeasureContext.Definition(model.Metadata.GetField("Amount"), "sales"));
        Assert.Equal((true, false, false), (editor.IsRedefinition, editor.CanReset, editor.IsNew));
        Assert.Equal(("Chiffre d'affaires", "[Chiffre d'affaires]", "[Chiffre d'affaires]"), (editor.Caption, editor.Formula, editor.DeclaredFormula));

        Assert.True(editor.Save());
        Assert.Empty(context.Measures.Measures); // saved as the model defines it: nothing to redefine

        editor.Formula = "AVERAGE([Chiffre d'affaires])";
        editor.Save();
        Assert.Equal(("Amount", "AVERAGE([Amount])"), (Assert.Single(context.Measures.Measures).Name, context.Measures.Measures[0].Formula));

        var again = new CalculatedMeasureEditor(context, model, context.Measures.Measures[0]);
        Assert.True(again.CanReset);
        again.Reset();
        Assert.Empty(context.Measures.Measures);
    }

    [Fact]
    public void The_editor_keeps_a_format_set_by_the_developer()
    {
        var context = EasyBIContext.Create().AddModel(SemanticModel.For(Sales, "sales").Measure(s => s.Amount, "CA", format: "#,##0 €"));
        var model = context.Models[0];

        var editor = new CalculatedMeasureEditor(context, model, MeasureContext.Definition(model.Metadata.GetField("Amount"), "sales"));

        Assert.Equal("#,##0 €", editor.Format.Value);
        Assert.Contains("#,##0 €", editor.Format.Label);
        Assert.Equal(5, editor.FormatOptions.Count);
    }

    [Fact]
    public void The_designer_shows_a_redefined_measure_and_keeps_its_aggregation_when_only_renamed()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        context.Measures.Set(Redefinition("[Amount]"));
        var designer = new DesignerViewModel(context);
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount")] });

        Assert.Equal(("Ventes", true), (designer.Values[0].Caption, designer.Values[0].HasAggregation));

        context.Measures.Set(Redefinition("MAX([Amount])", "CA max"));
        Assert.Equal(("CA max", false), (designer.Values[0].Caption, designer.Values[0].HasAggregation));

        context.Measures.Remove("sales", "Amount");
        Assert.Equal(("Chiffre d'affaires", true), (designer.Values[0].Caption, designer.Values[0].HasAggregation));
    }

    // --- Dashboards

    [Fact]
    public void A_dashboard_loads_and_saves_the_reports_calculated_measures() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var view = new DashboardView { Context = context };

        view.Load(new DashboardDocument { Report = new ReportDefinition { Measures = [Rate()] } });
        Assert.Equal([Rate()], context.Measures.Measures);

        var other = Rate() with { Name = "calc_other", Caption = "Autre" };
        context.Measures.Set(other);
        Assert.Equal([Rate(), other], view.Capture().Report.Measures);
    });

    [Fact]
    public void Undoing_a_dashboard_change_keeps_the_measures_created_since() => Sta.Run(() =>
    {
        var context = EasyBIContext.Create().AddModel(Model());
        var view = new DashboardView { Context = context };
        view.Load(new DashboardDocument());
        var before = view.Capture();

        context.Measures.Set(Rate());
        view.Restore(before);

        Assert.Equal([Rate()], context.Measures.Measures);
    });

    [Fact]
    public void A_dashboard_loaded_before_its_context_hands_over_its_measures() => Sta.Run(() =>
    {
        var view = new DashboardView();
        view.Load(new DashboardDocument { Report = new ReportDefinition { Measures = [Rate()] } });
        Assert.Equal([Rate()], view.Capture().Report.Measures);

        var context = EasyBIContext.Create().AddModel(Model());
        view.Context = context;

        Assert.Equal([Rate()], context.Measures.Measures);
    });

    // --- Visuals

    /// <summary>Counts the data requests of the model it wraps.</summary>
    private sealed class CountingModel(ISemanticModel inner) : ISemanticModel
    {
        public int Queries { get; private set; }

        public ModelMetadata Metadata => inner.Metadata;

        public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Queries++;
            return inner.GetDataAsync(request, cancellationToken);
        }

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
            inner.GetDistinctValuesAsync(field, cancellationToken);
    }

    /// <summary>Holds its queries until released, recording their cancellation tokens.</summary>
    private sealed class GatedModel(ISemanticModel inner) : ISemanticModel
    {
        private readonly TaskCompletionSource<bool> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Queries => Tokens.Count;

        public List<CancellationToken> Tokens { get; } = [];

        public ModelMetadata Metadata => inner.Metadata;

        public void Release() => _gate.SetResult(true);

        public async Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            await _gate.Task;
            return await inner.GetDataAsync(request, cancellationToken);
        }

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
            inner.GetDistinctValuesAsync(field, cancellationToken);
    }

    [Fact]
    public void A_visual_queries_again_when_a_measure_it_shows_changes() => Sta.Run(() =>
    {
        var model = new CountingModel(Model());
        var context = EasyBIContext.Create().AddModel(model);
        context.Measures.Set(Rate());
        var view = new VisualView { Context = context, Definition = new() { Model = "sales", Type = VisualType.Kpi, Values = [new("calc_rate")] } };
        var window = new Window { Content = view, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        Assert.Equal(1, model.Queries);

        context.Measures.Set(Rate() with { Name = "calc_unused", Caption = "Autre" });
        Pump();
        Assert.Equal(1, model.Queries);

        context.Measures.Set(Rate("[Margin] / [Amount] * 100"));
        Pump();
        Assert.Equal(2, model.Queries);
        window.Close();
    });

    // --- Measures used by other measures

    private static CalculatedMeasure Cost(string formula = "[Amount] - [Margin]") =>
        new() { Name = "calc_cost", Caption = "Coût", Model = "sales", Formula = formula };

    [Fact]
    public async Task The_editor_offers_and_previews_the_reports_other_measures()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        context.Measures.Set(Cost());
        context.Measures.Set(Rate());
        var editor = new CalculatedMeasureEditor(context, context.Models[0], Rate());

        Assert.Equal(["[Chiffre d'affaires]", "[Marge]", "[Coût]"], editor.Insertable);
        var cost = editor.Tabs[1].Items.Single(i => i.Caption == "Coût");
        Assert.Equal(("ƒx", true), (cost.Glyph, cost.IsReportMeasure));

        editor.Formula = "[Coût] / [Chiffre d'affaires]";
        await editor.PreviewTask;
        Assert.True(editor.CanSave);
        Assert.Equal("[calc_cost] / [Amount]", editor.Result!.Formula);
        Assert.Equal(ValueFormatter.Format(410d / 600d, "P1"), editor.Preview);

        // Not inside a function, and not itself
        Assert.Equal(["Chiffre d'affaires", "Marge"], editor.Tabs[1].Items.Where(i => !i.IsReportMeasure).Select(i => i.Caption));
        editor.Formula = "SUM([";
        editor.Suggest(editor.Formula.Length);
        Assert.DoesNotContain(editor.Suggestions, s => s.Caption == "Coût");
    }

    [Fact]
    public void The_editor_refuses_a_measure_that_would_use_itself()
    {
        var context = EasyBIContext.Create().AddModel(Model());
        context.Measures.Set(Cost("[calc_rate] * [Amount]"));
        context.Measures.Set(Rate());
        var editor = new CalculatedMeasureEditor(context, context.Models[0], Rate()) { Formula = "[Coût] / 2" };

        Assert.Equal("A measure cannot use itself: this measure → [Coût] → this measure.", editor.Error);
        Assert.Equal((0, 6), (editor.Problem!.Start, editor.Problem.Length));
    }

    [Fact]
    public void Deleting_a_measure_names_the_measures_that_use_it()
    {
        var context = new MeasureContext();
        context.Set(Cost());
        context.Set(Rate("[calc_cost] / [Amount]"));

        Assert.Equal("Delete the measure “Coût”? It is used by “Taux de marge”, which will show an error, as will the visuals that show it.",
            FieldList.DeleteConfirmation(context, Cost()));
        Assert.StartsWith("Delete the measure “Taux de marge”?", FieldList.DeleteConfirmation(context, Rate()));
    }

    [Fact]
    public void A_visual_queries_again_when_a_measure_used_by_its_measure_changes() => Sta.Run(() =>
    {
        var model = new CountingModel(Model());
        var context = EasyBIContext.Create().AddModel(model);
        context.Measures.Set(Cost());
        context.Measures.Set(Rate("[calc_cost] / [Amount]"));
        var view = new VisualView { Context = context, Definition = new() { Model = "sales", Type = VisualType.Kpi, Values = [new("calc_rate")] } };
        var window = new Window { Content = view, Width = 300, Height = 200, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        Assert.Equal(1, model.Queries);

        context.Measures.Set(Cost("[Amount]"));
        Pump();
        Assert.Equal(2, model.Queries);
        window.Close();
    });
}
