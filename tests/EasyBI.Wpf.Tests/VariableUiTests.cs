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

    private static ReportVariable N(double value) =>
        new() { Name = "N", Caption = "Nombre", Value = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Minimum = 1, Maximum = 5 };

    private static ReportVariable TopRegions() => new()
    {
        Name = "TopRegions",
        Caption = "Meilleures régions",
        Kind = VariableKind.Set,
        Model = "sales",
        Field = "Region",
        Rule = new SetRule("Amount") { CountVariable = "N" },
    };

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

        Assert.Equal("2", context.Variables.Find("N")!.Value);
        Assert.Equal(DashboardItemKind.Variable, dashboard.Items.Single(i => i.Visual is not null).Kind);
        context.Variables.SetValue("N", "4");
        Assert.Equal("4", dashboard.Capture().Report.Variables.Single().Value);
    });

    [Fact]
    public void The_variable_editor_creates_parameters_and_sets() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2));

        var parameter = new VariableEditor(context) { Name = "Rate", Caption = "Taux", Value = "0.2", Format = "P0" };
        Assert.Null(parameter.Error);
        Assert.Equal(new ReportVariable { Name = "Rate", Caption = "Taux", Value = "0.2", Format = "P0" }, parameter.Save());

        var taken = new VariableEditor(context) { Name = "N" };
        Assert.NotNull(taken.Error);
        Assert.NotNull(new VariableEditor(context) { Name = "2bad" }.Error);

        var set = new VariableEditor(context, kind: VariableKind.Set) { Name = "Best" };
        set.CountVariable = set.CountVariables.Single(c => c.Value == "N");
        set.Order = set.Orders.Single(o => o.Value);
        var saved = set.Save()!;
        Assert.Equal(VariableKind.Set, saved.Kind);
        Assert.Equal(("sales", "Region"), (saved.Model, saved.Field));
        Assert.Equal(new SetRule("Amount") { CountVariable = "N", Bottom = true }, saved.Rule);
        Assert.Equal(3, context.Variables.Variables.Count);
    });

    [Fact]
    public void A_fixed_set_keeps_the_values_checked_in_the_editor() => Sta.Run(() =>
    {
        var (context, _) = Context();
        var editor = new VariableEditor(context, kind: VariableKind.Set) { Name = "Mine", IsFixed = true };
        Assert.True(SpinWait.SpinUntil(() => { Pump(); return editor.Members is { IsLoading: false }; }, TimeSpan.FromSeconds(5)));

        editor.Members!.Values.Single(v => Equals(v.Value, "South")).IsChecked = true;

        Assert.Equal(["South"], editor.ToVariable()!.Members);
    });

    [Fact]
    public void A_filter_can_use_a_set_or_a_parameter_instead_of_values() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2), TopRegions(), new ReportVariable { Name = "Region", DataType = DataType.Text, Value = "East" });
        var field = model.Metadata.GetField("Region");
        var filter = new FilterItemViewModel(field, model, () => { }, variables: context.Variables);

        Assert.Equal(["(none)", "InSet", "NotInSet", "Equal"], filter.VariableChoices.Select(c => c.Value?.Use.ToString() ?? "(none)"));
        filter.VariableChoice = filter.VariableChoices.Single(c => c.Value?.Use == FilterVariableUse.NotInSet);

        Assert.False(filter.IsValueList);
        Assert.Equal(new FilterSpec("Region", FilterOperator.NotIn, ["[@TopRegions]"]).Values, filter.ToFilterSpec()!.Values);
        Assert.Equal(FilterOperator.NotIn, filter.ToFilterSpec()!.Operator);

        // Loaded back, the filter shows the same choice
        var loaded = new FilterItemViewModel(field, model, () => { }, ["[@TopRegions]"], FilterOperator.NotIn, variables: context.Variables);
        Assert.Equal(new FilterVariable("TopRegions", FilterVariableUse.NotInSet), loaded.VariableChoice.Value);
    });

    [Fact]
    public void A_number_filter_can_compare_with_a_parameter() => Sta.Run(() =>
    {
        var (context, model) = Context(N(2));
        var measure = model.Metadata.GetField("Amount");
        var filter = new FilterItemViewModel(measure, model, () => { }, variables: context.Variables);

        filter.VariableChoice = filter.VariableChoices.Single(c => c.Value?.Use == FilterVariableUse.Compare);

        var spec = filter.ToFilterSpec()!;
        Assert.Equal(FilterOperator.GreaterThanOrEqual, spec.Operator);
        Assert.Equal(["[@N]"], spec.Values);
    });

    [Fact]
    public void The_designer_takes_its_top_from_a_parameter_and_groups_a_dimension_by_a_set() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions());
        var designer = new DesignerViewModel(context);
        designer.Load(ByRegion() with { TopVariable = "N", Axis = [new FieldRef("Region") { Set = "TopRegions" }] });

        Assert.Equal("N", designer.Visual!.TopVariable);
        Assert.Equal("TopRegions", designer.Visual.Axis[0].Set);
        designer.Axis[0].Set = null;
        designer.TopVariable = null;
        Assert.Null(designer.Visual.Axis[0].Set);
        Assert.Null(designer.Visual.TopVariable);
    });

    [Fact]
    public void A_calculated_measure_can_use_a_parameter() => Sta.Run(() =>
    {
        var (context, model) = Context(new ReportVariable { Name = "Rate", Value = "0.5" });
        var editor = new CalculatedMeasureEditor(context, model) { Caption = "Remise", Formula = "[CA] * [@Rate]" };

        Assert.Null(editor.Error);
        Assert.Contains(editor.Tabs, t => t.Kind == FormulaItemKind.Variable);
        Assert.True(SpinWait.SpinUntil(() => { Pump(); return editor.Preview is not null; }, TimeSpan.FromSeconds(5)));
        Assert.Equal("325", editor.Preview);
    });

    [Fact]
    public void The_field_list_shows_the_parameters_and_the_sets_of_its_model() => Sta.Run(() =>
    {
        var (context, _) = Context(N(2), TopRegions());
        var list = new FieldList { Context = context };

        Assert.Equal(["Nombre", "Meilleures régions"], list.Variables.Select(v => v.Caption));
        context.Variables.Remove("TopRegions");
        Assert.Equal(["Nombre"], list.Variables.Select(v => v.Caption));
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
