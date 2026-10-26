using System.Windows.Data;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>The control's own properties: a designer used without view-model, in XAML or in code.</summary>
public class ReportDesignerTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount);

    private static EasyBIContext Context() => EasyBIContext.Create().AddModel(
        SemanticModel.For(new[] { new Sale("Nord", "Web", 1m) }, "sales")
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Channel, "Channel")
            .Measure(s => s.Amount, "Amount"));

    private static readonly VisualDefinition Sales = new() { Model = "sales", Title = "Sales", Axis = [new("Region")], Values = [new("Amount")] };

    private sealed class Holder
    {
        public VisualDefinition? Visual { get; set; }
    }

    [Fact]
    public void Setting_the_visual_loads_it_and_the_changes_of_the_user_come_back_into_it() => Sta.Run(() =>
    {
        var designer = new ReportDesigner { Context = Context(), Visual = Sales };
        Assert.Same(Sales, designer.Visual);
        Assert.Equal("Sales", designer.ViewModel!.Title);

        designer.ViewModel.Title = "Revenue";
        Assert.Equal("Revenue", designer.Visual!.Title);

        designer.ViewModel.Undo();
        Assert.Equal("Sales", designer.Visual!.Title);
    });

    [Fact]
    public void A_visual_set_before_the_context_is_loaded_with_it() => Sta.Run(() =>
    {
        // XAML may set the properties in any order
        var designer = new ReportDesigner { Visual = Sales };
        designer.Context = Context();

        Assert.Equal(["Region"], designer.ViewModel!.Axis.Select(a => a.Field.Name));
        Assert.Same(Sales, designer.Visual);
    });

    [Fact]
    public void A_null_visual_starts_a_new_one() => Sta.Run(() =>
    {
        var designer = new ReportDesigner { Context = Context(), Visual = Sales };

        designer.Visual = null;

        Assert.Empty(designer.ViewModel!.Axis);
        Assert.False(designer.ViewModel.CanUndo);
    });

    [Fact]
    public void A_bound_visual_receives_the_changes_of_the_user() => Sta.Run(() =>
    {
        var holder = new Holder { Visual = Sales };
        var designer = new ReportDesigner { Context = Context() };
        designer.SetBinding(ReportDesigner.VisualProperty, new Binding(nameof(Holder.Visual)) { Source = holder });
        Assert.Equal("Sales", designer.ViewModel!.Title);

        designer.ViewModel.Drop(designer.ViewModel.Dimensions.Single(d => d.Name == "Channel"), Shelf.Legend);

        Assert.Equal("Channel", holder.Visual!.Legend!.Field);
    });

    [Fact]
    public void The_rules_of_the_control_judge_the_changes_of_the_user() => Sta.Run(() =>
    {
        var designer = new ReportDesigner { Context = Context(), Visual = Sales };
        designer.Rules.Add(change => change.Changes(v => v.Title) ? "Fixed title" : null);

        designer.ViewModel!.Title = "Revenue";

        Assert.Equal(("Fixed title", "Sales"), (designer.ViewModel.Refusal, designer.Visual!.Title));
    });
}
