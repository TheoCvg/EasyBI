using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>Variables of the report in the visuals, the dashboard, the designer and the filters.</summary>
public class VariableUiTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("North", "Web", 100m),
        new("North", "Shop", 300m),
        new("South", "Web", 200m),
        new("East", "Shop", 50m),
    ];

    /// <summary>The sales, keeping the last visual it was asked for.</summary>
    private sealed class RecordingModel : ISemanticModel
    {
        private readonly QueryableSemanticModel<Sale> _inner = SemanticModel.For(Sales.AsQueryable(), "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA");

        public List<VisualDataRequest> Requests { get; } = [];

        public ModelMetadata Metadata => _inner.Metadata;

        public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return _inner.GetDataAsync(request, cancellationToken);
        }

        public Task<DistinctValues> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken cancellationToken = default) =>
            _inner.GetDistinctValuesAsync(request, cancellationToken);
    }

    private static ReportVariable Variable(string name, string formula) => new() { Name = name, Model = "sales", Formula = formula };

    private static ReportVariable N(int value) => Variable("N", $"{value} BETWEEN 1 AND 5");

    private static ReportVariable TopRegions() => Variable("TopRegions", "SELECT TOP [@N] [Region] ORDER BY [Amount] DESC");

    private static VisualDefinition ByRegion() =>
        new() { Model = "sales", Type = VisualType.Table, Axis = [new("Region")], Values = [new("Amount")] };

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    // Queries run on the thread pool: lets them end and their continuations run
    private static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until)
        {
            Pump();
            Thread.Sleep(5);
        }
        Pump();
    }

    private static Window Show(FrameworkElement content)
    {
        var window = new Window { Content = content, Width = 500, Height = 400, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        return window;
    }

    private static (EasyBIContext Context, RecordingModel Model) Context(params ReportVariable[] variables)
    {
        var model = new RecordingModel();
        var context = EasyBIContext.Create().AddModel(model);
        context.Variables.Load(variables);
        return (context, model);
    }

    [Fact]
    public void A_visual_queries_with_the_values_of_its_variables_and_again_when_they_change() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2));
        var view = new VisualView { Context = context, Definition = ByRegion() with { Title = "Top {N}", Sort = new("Amount", true), TopVariable = "N" } };
        var window = Show(view);
        PumpUntil(() => model.Requests.Count == 1);
        Assert.Equal(2, model.Requests[^1].Visual.Top);

        context.Variables.SetValue("N", "3");
        // Once queried, the visual is drawn again with its new title
        PumpUntil(() => Texts(view).Contains("Top 3"));

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(3, model.Requests[^1].Visual.Top);
        Assert.Contains(Texts(view), t => t == "Top 3");
        window.Close();
    });

    [Fact]
    public void A_visual_without_variables_is_left_alone_when_a_variable_changes() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2));
        var window = Show(new VisualView { Context = context, Definition = ByRegion() });
        PumpUntil(() => model.Requests.Count == 1);

        context.Variables.SetValue("N", "4");
        Pump();

        Assert.Single(model.Requests);
        window.Close();
    });

    [Fact]
    public void A_visual_grouped_by_a_set_gets_its_values_before_querying() => Sta.Run(() =>
    {
        var (context, model) = Context(N(1), TopRegions());
        var window = Show(new VisualView { Context = context, Definition = ByRegion() with { Axis = [new FieldRef("Region") { Set = "TopRegions" }] } });
        PumpUntil(() => model.Requests.Any(r => r.Visual.Type == VisualType.Table && r.Visual.Axis[0].SetMembers is not null && r.Visual.Id != "set:TopRegions"));

        var request = model.Requests.Last();
        Assert.Equal(["North"], request.Visual.Axis[0].SetMembers);
        window.Close();
    });

    [Fact]
    public void A_variable_visual_shows_a_slider_that_sets_its_parameter() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2));
        var view = new VisualView { Context = context, Definition = new VisualDefinition { Type = VisualType.Variable, Variable = "N" } };
        var window = Show(view);
        Pump();

        var variable = Find<VariableView>(view)!;
        var slider = Find<Slider>(variable)!;
        Assert.Equal(2, slider.Value);
        slider.Value = 4;
        variable.CommitPending();

        Assert.Equal("4", context.Variables.Find("N")!.Value);
        // A change from elsewhere moves the same slider
        context.Variables.SetValue("N", "5");
        Pump();
        Assert.Same(slider, Find<Slider>(variable));
        Assert.Equal(5, slider.Value);
        window.Close();
    });

    [Fact]
    public void A_variable_visual_lists_the_values_of_a_set() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions());
        var view = new VariableView { Context = context, Variable = "TopRegions" };
        var window = Show(view);
        PumpUntil(() => view.Members.Count == 2);

        Assert.Equal(["North", "South"], view.Members);
        context.Variables.SetValue("N", "3");
        PumpUntil(() => view.Members.Count == 3);
        Assert.Equal(["North", "South", "East"], view.Members);
        window.Close();
    });

    [Fact]
    public void A_dashboard_loads_and_saves_the_variables_of_its_report() => Sta.Run(() =>
    {
        var (context, _) = Context();
        var dashboard = new DashboardView { Context = context };
        dashboard.Load(new DashboardDocument
        {
            Report = new ReportDefinition { Variables = [N(2)], Visuals = [new VisualDefinition { Type = VisualType.Variable, Variable = "N" }] },
        });

        Assert.Equal("2", VariableContext.ValueOf(context.Variables.Find("N")!));
        Assert.Equal(DashboardItemKind.Variable, dashboard.Items.Single(i => i.Visual is not null).Kind);
        context.Variables.SetValue("N", "4");
        Assert.Equal("4", dashboard.Capture().Report.Variables.Single().Value);
    });

    [Fact]
    public void A_variable_visual_shows_a_list_for_choices_and_the_value_of_a_computed_variable() => Sta.Run(() =>
    {
        var (context, _) = Context(Variable("Canal", "\"Web\" IN (\"Web\"; \"Shop\")"), Variable("Moyenne", "AVERAGE([Amount])"));
        var choice = new VariableView { Context = context, Variable = "Canal" };
        var value = new VariableView { Context = context, Variable = "Moyenne" };
        var window = Show(new StackPanel { Children = { choice, value } });
        PumpUntil(() => value.Members.Count == 1);

        var combo = Find<ComboBox>(choice)!;
        Assert.Equal(["Web", "Shop"], combo.Items.Cast<Option<string>>().Select(o => o.Value));
        combo.SelectedIndex = 1;
        Assert.Equal("Shop", context.Variables.Find("Canal")!.Value);
        Assert.Equal(["162.5"], value.Members);
        window.Close();
    });

    [Fact]
    public void The_formula_editor_writes_parameters_sets_and_computed_values() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2));

        var rate = CalculatedMeasureEditor.ForVariable(context, model);
        rate.Caption = "Taux";
        rate.Formula = "0.2 BETWEEN 0 AND 1 STEP 0.05";
        Assert.Null(rate.Error);
        Assert.True(rate.Save());
        Assert.Equal("0.2 BETWEEN 0 AND 1 STEP 0.05", context.Variables.Find("Taux")!.Formula);

        var best = CalculatedMeasureEditor.ForVariable(context, model);
        best.Caption = "Meilleures";
        best.Formula = "SELECT TOP [@N] [Région] WHERE [Canal] = \"Web\" ORDER BY [CA] DESC";
        Assert.Null(best.Error);
        Assert.Contains(best.Tabs, t => t.Kind == FormulaItemKind.Keyword);
        Assert.True(SpinWait.SpinUntil(() => { Pump(); return best.Preview is not null; }, TimeSpan.FromSeconds(5)));
        Assert.Equal("South, North (2 values)", best.Preview);
        best.Save();
        Assert.Equal(("sales", "SELECT TOP [@N] [Region] WHERE [Channel] = \"Web\" ORDER BY [Amount] DESC"),
            (context.Variables.Find("Meilleures")!.Model, context.Variables.Find("Meilleures")!.Formula));

        var taken = CalculatedMeasureEditor.ForVariable(context, model);
        taken.Caption = "N";
        taken.Formula = "1";
        Assert.NotNull(taken.Error);
        taken.Caption = "a[b]";
        Assert.NotNull(taken.Error);
        taken.Caption = "Ok";
        taken.Formula = "SELECT [CA]";
        Assert.NotNull(taken.Problem);
    });

    [Fact]
    public void A_renamed_variable_is_renamed_in_the_formulas_using_it() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2), TopRegions());
        context.Measures.Set(new CalculatedMeasure { Name = "calc_x", Model = "sales", Formula = "[Amount] * [@N]" });

        var editor = CalculatedMeasureEditor.ForVariable(context, model, context.Variables.Find("N"));
        editor.Caption = "Count";
        Assert.True(editor.Save());

        Assert.Null(context.Variables.Find("N"));
        Assert.Equal("SELECT TOP [@Count] [Region] ORDER BY [Amount] DESC", context.Variables.Find("TopRegions")!.Formula);
        Assert.Equal("[Amount] * [@Count]", context.Measures.Find("sales", "calc_x")!.Formula);
    });

    [Theory]
    [InlineData("", "SELECT")]
    [InlineData("SELECT ", "TOP,Région,Canal")]
    [InlineData("SELECT TOP 3 ", "Région,Canal")]
    [InlineData("SELECT [Région] ", "IN,WHERE,ORDER BY")]
    [InlineData("SELECT [Région] WHERE [Canal] = \"Web\" ", "AND,OR,ORDER BY")]
    [InlineData("SELECT [Région] ORDER BY [CA] ", "DESC,ASC")]
    [InlineData("10 ", "BETWEEN,IN")]
    [InlineData("MAX([CA] ", "BY")]
    [InlineData("MAX([CA] BY ", "Région,Canal")]
    public void The_variable_editor_suggests_the_words_of_the_formula(string formula, string expected) => Sta.Run(() =>
    {
        var (context, model) = Context();
        var editor = CalculatedMeasureEditor.ForVariable(context, model);
        editor.Formula = formula;

        editor.Suggest(formula.Length, all: true);

        Assert.Equal(expected.Split(','), editor.Suggestions.Select(s => s.Caption).Take(expected.Split(',').Length));
    });

    [Fact]
    public void A_set_dropped_on_the_designer_groups_its_dimension_or_filters_on_its_values() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2), TopRegions());
        var list = new FieldList { Context = context };
        var set = list.Variables.Single(v => v.IsSet);
        var designer = new DesignerViewModel(context);
        designer.Drop(new ModelField(model, model.Metadata.GetField("Amount")), Shelf.Values);

        designer.Drop(set, Shelf.Axis);
        designer.Drop(set, Shelf.Filters);

        Assert.Equal(new FieldRef("Region") { Set = "TopRegions" }, designer.Visual!.Axis.Single());
        Assert.Equal("TopRegions", designer.Axis.Single().Caption);
        Assert.Equal(new FilterSpec("Region", FilterOperator.In, ["[@TopRegions]"]).Values, designer.Visual.Filters.Single().Values);
        designer.Filters.Single().ExcludeSet = true;
        Assert.Equal(FilterOperator.NotIn, designer.Visual.Filters.Single().Operator);
        // Once per well; a value is used in formulas, not dropped
        Assert.False(designer.CanDrop(set, Shelf.Axis));
        var value = list.Variables.Single(v => v.Caption == "N");
        Assert.False(designer.CanDrop(value, Shelf.Axis));
        Assert.Contains("[@N]", designer.RefusalReason(value, Shelf.Axis));

        // Loaded back, the filter is the set's
        var loaded = new DesignerViewModel(context);
        loaded.Load(designer.Visual);
        Assert.Equal(("TopRegions", true), (loaded.Filters.Single().Set, loaded.Filters.Single().ExcludeSet));
        Assert.Equal("TopRegions", loaded.Axis.Single().Set);
    });

    [Fact]
    public void A_variable_dropped_on_the_dashboard_shows_it_and_a_set_dropped_on_a_visual_groups_or_filters_it() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions());
        var behavior = new Dashboard.Behaviors.FieldDropBehavior();
        var n = context.Variables.Find("N")!;
        var set = context.Variables.Find("TopRegions")!;
        var byChannel = new VisualDefinition { Model = "sales", Type = VisualType.Bar, Axis = [new("Channel")], Values = [new("Amount")] };

        // On a free place: the variable's control, value or values
        var control = behavior.CreateVariableVisual(n)!;
        Assert.Equal((VisualType.Variable, "N"), (control.Type, control.Variable));
        // On a visual showing the set's dimension, the set filters it; otherwise it groups its dimension
        var filtered = behavior.AddVariable(ByRegion(), set)!;
        Assert.Equal(new FilterSpec("Region", FilterOperator.In, ["[@TopRegions]"]).Values, filtered.Filters.Single().Values);
        Assert.Null(behavior.AddVariable(filtered, set)); // already filtered
        Assert.Equal(new FieldRef("Region") { Set = "TopRegions" }, behavior.AddVariable(byChannel, set)!.Legend);
        // A value is used in formulas; a visual of another model does not take the set
        Assert.Null(behavior.AddVariable(ByRegion(), n));
        Assert.Null(behavior.AddVariable(ByRegion() with { Model = "other" }, set));
    });

    [Fact]
    public void The_field_list_scrolls_its_sections_as_one_so_that_each_shows_all_its_items() => Sta.Run(() =>
    {
        var (context, _) = Context([.. Enumerable.Range(1, 30).Select(i => Variable("V" + i, i.ToString(System.Globalization.CultureInfo.InvariantCulture)))]);
        var list = new FieldList { Context = context };
        var window = Show(list);

        var variables = (ListBox)list.Template.FindName("VariableList", list);
        var scroller = (ScrollViewer)list.Template.FindName("PART_Scroller", list);
        Assert.Equal(30, variables.Items.Count);
        Assert.True(variables.ActualHeight > 30 * 15, $"variable list height {variables.ActualHeight}");
        Assert.True(scroller.ScrollableHeight > 0);

        // The wheel over a list scrolls the whole field list
        var item = (ListBoxItem)variables.ItemContainerGenerator.ContainerFromIndex(0);
        item.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        Pump();
        Assert.True(scroller.VerticalOffset > 0);
        window.Close();
    });

    [Fact]
    public void The_designer_takes_its_top_from_a_number_variable() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions());
        var designer = new DesignerViewModel(context);
        designer.Load(ByRegion() with { TopVariable = "N" });

        Assert.Equal("N", designer.Visual!.TopVariable);
        designer.TopVariable = null;
        Assert.Null(designer.Visual.TopVariable);
    });

    [Fact]
    public void A_calculated_measure_can_use_a_variable() => Sta.Run(() =>
    {
        var (context, model) = Context(Variable("Rate", "0.5"));
        var editor = new CalculatedMeasureEditor(context, model) { Caption = "Remise", Formula = "[CA] * [@Rate]" };

        Assert.Null(editor.Error);
        Assert.Contains(editor.Tabs, t => t.Kind == FormulaItemKind.Variable);
        Assert.DoesNotContain(editor.Tabs, t => t.Kind == FormulaItemKind.Keyword);
        Assert.True(SpinWait.SpinUntil(() => { Pump(); return editor.Preview is not null; }, TimeSpan.FromSeconds(5)));
        Assert.Equal("325", editor.Preview);
    });

    [Fact]
    public void The_field_list_shows_the_variables_of_its_model() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions(), Variable("Moyenne", "AVERAGE([Amount])") with { Model = "other" });
        var list = new FieldList { Context = context };

        Assert.Equal(["N", "TopRegions"], list.Variables.Select(v => v.Caption));
        Assert.Equal(["@", "{ }"], list.Variables.Select(v => v.Glyph));
        context.Variables.Remove("TopRegions");
        Assert.Equal(["N"], list.Variables.Select(v => v.Caption));
    });

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text) yield return text.Text;
            foreach (var deeper in Texts(child)) yield return deeper;
        }
    }
}
