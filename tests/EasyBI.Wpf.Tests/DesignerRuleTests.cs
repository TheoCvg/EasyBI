using System.ComponentModel;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Tests;

/// <summary>The rules of the designer: every change of the visual passes them, whatever the action.</summary>
public class DesignerRuleTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, int Quantity);

    private static DesignerViewModel Designer(params Func<VisualChange, string?>[] rules)
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", "Web", 1m, 1) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Channel, "Channel")
                .Measure(s => s.Amount, "Amount")
                .Measure(s => s.Quantity, "Quantity")));
        designer.Load(new VisualDefinition { Model = "sales", Title = "Sales", Axis = [new("Region")], Values = [new("Amount")] });
        foreach (var rule in rules) designer.Rules.Add(rule);
        return designer;
    }

    private static string? FixedType(VisualChange change) => change.Changes(v => v.Type) ? "Fixed type" : null;

    private static string? FixedFields(VisualChange change) => change.FieldsChanged ? "Fixed fields" : null;

    private FieldDescriptor Field(DesignerViewModel designer, string name) =>
        designer.Dimensions.Concat(designer.Measures).Single(f => f.Name == name);

    [Fact]
    public void A_refused_change_leaves_the_visual_as_it_was_and_tells_why()
    {
        var designer = Designer(FixedType);

        designer.SelectedType = VisualType.Pie;

        Assert.Equal((VisualType.Bar, VisualType.Bar), (designer.SelectedType, designer.Visual!.Type));
        Assert.Equal("Fixed type", designer.Refusal);
        Assert.False(designer.CanUndo); // nothing happened

        designer.Title = "Renamed"; // accepted: the message goes
        Assert.Null(designer.Refusal);
        Assert.Equal("Renamed", designer.Visual!.Title);
    }

    [Fact]
    public void The_gallery_greys_the_types_a_rule_refuses()
    {
        // Tables and KPIs: drawn by EasyBI's own renderers, without a charting library
        var designer = Designer(change => change.After.Type == VisualType.Table ? "No tables" : null);

        var table = designer.TypeOptions.Single(o => o.Type == VisualType.Table);
        Assert.Equal((false, "No tables"), (table.IsAvailable, table.UnavailableReason));
        Assert.True(designer.TypeOptions.Single(o => o.Type == VisualType.Kpi).IsAvailable);

        designer.Rules.Clear();
        Assert.True(table.IsAvailable);
    }

    [Fact]
    public void Fixed_fields_refuse_drops_moves_and_removals_but_let_their_settings_change()
    {
        var designer = Designer(FixedFields);

        Assert.False(designer.CanDrop(Field(designer, "Channel"), Shelf.Axis));
        Assert.Equal("Fixed fields", designer.RefusalReason(Field(designer, "Quantity"), Shelf.Values));
        Assert.False(designer.CanDrop(designer.Axis[0], Shelf.Legend));
        Assert.False(designer.RemoveCommand.CanExecute(designer.Values[0]));

        // The keyboard goes through the same rule
        designer.SelectedItem = designer.Values[0];
        designer.RemoveSelected();
        Assert.Single(designer.Visual!.Values);

        designer.Values[0].Aggregation = Aggregation.Average;
        Assert.Equal(Aggregation.Average, designer.Visual!.Values[0].Aggregation);
        Assert.Null(designer.Refusal);
    }

    [Fact]
    public void A_chip_moved_within_its_well_is_checked_once_moved()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Channel"), Shelf.Axis);
        designer.Rules.Add(FixedFields);

        designer.Drop(designer.Axis[1], Shelf.Axis, 0);

        Assert.Equal(["Region", "Channel"], designer.Visual!.Axis.Select(a => a.Field));
        Assert.Equal("Fixed fields", designer.Refusal);
    }

    [Fact]
    public void Loading_undoing_and_redoing_do_not_go_through_the_rules()
    {
        var designer = Designer();
        designer.SelectedType = VisualType.Line;
        designer.Rules.Add(FixedType);

        designer.Undo();
        Assert.Equal(VisualType.Bar, designer.Visual!.Type);
        designer.Redo();
        Assert.Equal(VisualType.Line, designer.Visual!.Type);

        designer.Load(new VisualDefinition { Model = "sales", Type = VisualType.Pie, Axis = [new("Region")], Values = [new("Amount")] });
        Assert.Equal(VisualType.Pie, designer.Visual!.Type);
    }

    [Fact]
    public void A_rule_behavior_is_a_rule_of_the_designer_it_is_attached_to() => Sta.Run(() =>
    {
        var first = Designer();
        var second = Designer();
        var control = new ReportDesigner { DataContext = first };
        var behavior = new LockVisualTypeBehavior { Message = "Kept" };

        Interaction.GetBehaviors(control).Add(behavior);
        first.SelectedType = VisualType.Line;
        Assert.Equal(("Kept", VisualType.Bar), (first.Refusal, first.Visual!.Type));

        // It follows the data context, and leaves when detached
        control.DataContext = second;
        Assert.Empty(first.Rules);
        Assert.Single(second.Rules);
        Interaction.GetBehaviors(control).Remove(behavior);
        Assert.Empty(second.Rules);
    });

    [Fact]
    public void The_rules_given_by_easybi_say_why_in_the_users_language() => Sta.Run(() =>
    {
        var designer = Designer();
        var control = new ReportDesigner { DataContext = designer };
        Interaction.GetBehaviors(control).Add(new LockFieldsBehavior());
        Interaction.GetBehaviors(control).Add(new ReadOnlyDesignerBehavior());

        Assert.Equal("The fields of this visual cannot change", designer.RefusalReason(Field(designer, "Channel"), Shelf.Axis));
        designer.Title = "Renamed";
        Assert.Equal(("This visual cannot be changed", "Sales"), (designer.Refusal, designer.Visual!.Title));
    });
}
