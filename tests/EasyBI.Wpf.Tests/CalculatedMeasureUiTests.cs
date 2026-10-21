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
        Assert.True(fields[^1].IsEditable);
        Assert.False(fields[0].IsEditable);

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

    /// <summary>Counts the queries of the model it wraps.</summary>
    private sealed class CountingModel(ISemanticModel inner) : ISemanticModel
    {
        public int Queries { get; private set; }

        public ModelMetadata Metadata => inner.Metadata;

        public Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default)
        {
            Queries++;
            return inner.ExecuteAsync(query, cancellationToken);
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
        var view = new VisualView { Context = context, Definition = new() { Model = "sales", Mark = MarkType.Kpi, Values = [new("calc_rate")] } };
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
}
