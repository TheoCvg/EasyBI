using System.ComponentModel;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>The properties panel shows the settings of the selected chip, then the visual's.</summary>
public class DesignerPropertiesTests
{
    private sealed record Sale(string Region, DateTime Date, decimal Amount);

    private static DesignerViewModel Designer()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", new(2024, 1, 1), 1m) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "Amount")));
        designer.Load(new VisualDefinition { Model = "sales", Title = "Sales", Axis = [new("Region")], Values = [new("Amount")] });
        return designer;
    }

    private static string[] Keys(DesignerPropertySection section) => section.Properties.Where(p => p.IsAvailable).Select(p => p.Key).ToArray();

    [Fact]
    public void Without_a_selection_the_panel_shows_the_visual()
    {
        var designer = Designer();

        var visual = Assert.Single(designer.Properties);
        Assert.Equal(DesignerPropertySections.Visual, visual.Key);

        visual["Title"]!.TextValue = "Renamed";
        visual["Top"]!.NumberText = "5";
        Assert.Equal(("Renamed", 5), (designer.Visual!.Title, designer.Visual.Top));

        visual["Top"]!.NumberText = "5x"; // being typed: the value stays
        Assert.Equal(5, designer.Visual!.Top);
        Assert.Equal("5x", visual["Top"]!.NumberText);
        visual["Top"]!.NumberText = "";
        Assert.Null(designer.Visual!.Top);
    }

    [Fact]
    public void A_selected_measure_shows_its_settings_first()
    {
        var designer = Designer();
        var amount = designer.Values.Single();

        designer.SelectCommand.Execute(amount);

        Assert.True(amount.IsSelected);
        Assert.Equal([DesignerPropertySections.Field, DesignerPropertySections.Visual], designer.Properties.Select(s => s.Key));
        var field = designer.Properties[0];
        Assert.Equal("Amount", field.Header);
        Assert.Equal(["Aggregation", "Time", "SeriesMark", "SecondaryAxis", "Sort"], Keys(field));

        var aggregation = field["Aggregation"]!;
        aggregation.SelectedChoice = aggregation.Choices.Single(c => Equals(c.Value, Aggregation.Average));
        Assert.Equal(Aggregation.Average, amount.Aggregation);
        Assert.Equal(Aggregation.Average, designer.Visual!.Values.Single().Aggregation);

        field["Sort"]!.Value = ListSortDirection.Descending;
        Assert.Equal("Amount, descending", designer.Properties[1]["Sort"]!.TextValue); // the visual's summary follows
    }

    [Fact]
    public void A_selected_dimension_shows_the_settings_it_has()
    {
        var designer = Designer();
        designer.Drop(designer.Dimensions.Single(d => d.Name == "Date"), Shelf.Legend);

        designer.SelectedItem = designer.Axis.Single();
        Assert.Equal(["Sort"], Keys(designer.Properties[0]));

        designer.SelectedItem = designer.Legend.Single();
        Assert.Equal(["Grain"], Keys(designer.Properties[0]));
    }

    [Fact]
    public void Removing_the_selected_chip_selects_the_visual_again()
    {
        var designer = Designer();
        var amount = designer.Values.Single();
        designer.SelectedItem = amount;

        designer.RemoveCommand.Execute(amount);

        Assert.Null(designer.SelectedItem);
        Assert.False(amount.IsSelected);
        Assert.Equal(DesignerPropertySections.Visual, Assert.Single(designer.Properties).Key);
    }

    [Fact]
    public void A_property_reads_the_value_changed_elsewhere()
    {
        var designer = Designer();
        var amount = designer.Values.Single();
        designer.SelectedItem = amount;
        var time = designer.Properties[0]["Time"]!;
        var changed = new List<string?>();
        time.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        amount.Time = TimeCalculation.YearToDate;

        Assert.Equal(TimeCalculation.YearToDate, time.Value);
        Assert.NotEmpty(changed);
    }

    [Fact]
    public void An_application_changes_the_sections_and_folded_sections_stay_folded()
    {
        var designer = Designer();
        designer.PropertiesBuilding += (_, e) =>
        {
            e.Sections.Add(new DesignerPropertySection("Notes", () => "Notes", [DesignerProperty.Info("Help", () => "Help", () => "Drag fields")]));
            if (e.Selection is ShelfItemViewModel) e.Sections[0].Properties.Remove(e.Sections[0]["Time"]!);
        };

        designer.SelectedItem = designer.Values.Single();
        Assert.Equal([DesignerPropertySections.Field, DesignerPropertySections.Visual, "Notes"], designer.Properties.Select(s => s.Key));
        Assert.Null(designer.Properties[0]["Time"]);

        designer.Properties[1].IsExpanded = false;
        designer.SelectedItem = null;
        Assert.False(designer.Properties.Single(s => s.Key == DesignerPropertySections.Visual).IsExpanded);
    }

    [Fact]
    public void A_click_on_a_chip_or_a_filter_selects_it()
    {
        var designer = Designer();
        designer.Drop(designer.Dimensions.Single(d => d.Name == "Region"), Shelf.Filters);
        var filter = designer.Filters.Single();

        designer.Values.Single().SelectCommand.Execute(null);
        Assert.Same(designer.Values.Single(), designer.SelectedItem);

        filter.SelectCommand.Execute(null);
        Assert.Same(filter, designer.SelectedItem);
        Assert.True(filter.IsSelected);
        var editor = Assert.Single(designer.Properties[0].Properties);
        Assert.Equal((PropertyKind.Custom, (object?)filter), (editor.Kind, editor.Value)); // the filter's editor
    }

    [Fact]
    public void Badges_sum_the_settings_of_a_chip_up()
    {
        var designer = Designer();
        var amount = designer.Values.Single();
        Assert.Equal(["Σ"], amount.Badges);
        Assert.Empty(designer.Axis.Single().Badges);

        amount.Aggregation = Aggregation.Average;
        amount.Time = TimeCalculation.PreviousYear;
        amount.SortDirection = ListSortDirection.Descending;

        Assert.Equal(["avg", "PY", "↓"], amount.Badges);
        designer.Drop(designer.Dimensions.Single(d => d.Name == "Date"), Shelf.Legend);
        Assert.Equal(["Month"], designer.Legend.Single().Badges);
    }

    [Fact]
    public void The_type_gallery_groups_the_types_and_chooses_one()
    {
        var designer = Designer();
        Assert.Equal([MarkGroup.Comparison, MarkGroup.Trend, MarkGroup.Composition, MarkGroup.IndicatorsAndTables], designer.MarkGroups.Select(g => g.Value));
        Assert.Equal([MarkType.Line, MarkType.Area], designer.MarkGroups[1].Options.Select(o => o.Mark));

        // Tables are drawn by EasyBI itself: available without a chart library
        designer.IsTypeGalleryOpen = true;
        designer.MarkOptions.Single(o => o.Mark == MarkType.Table).ChooseCommand.Execute(null);

        Assert.Equal(MarkType.Table, designer.SelectedMark);
        Assert.Equal(MarkType.Table, designer.SelectedMarkOption.Mark);
        Assert.Equal([MarkType.Table], designer.MarkOptions.Where(o => o.IsSelected).Select(o => o.Mark));
        Assert.False(designer.IsTypeGalleryOpen);
    }

    [Fact]
    public void A_type_no_renderer_draws_cannot_be_chosen()
    {
        var designer = Designer(); // no renderer in this context
        var pie = designer.MarkOptions.Single(o => o.Mark == MarkType.Pie);
        Assert.False(pie.IsAvailable);

        pie.ChooseCommand.Execute(null);

        Assert.NotEqual(MarkType.Pie, designer.SelectedMark);
    }

    [Fact]
    public void Every_visual_type_has_an_icon_in_the_theme() => Sta.Run(() =>
    {
        var icon = new VisualTypeIcon().Hosted();
        foreach (var mark in Enum.GetValues<MarkType>().Where(m => !m.IsContent()))
            Assert.IsAssignableFrom<System.Windows.Media.Geometry>(icon.TryFindResource(VisualTypeIcons.KeyOf(mark)));
    });

    // --- Wells that follow the visual type ---

    [Theory]
    [InlineData(MarkType.Bar, "Axis", "Legend", "Values")]
    [InlineData(MarkType.Pie, "Category", null, "Value")]
    [InlineData(MarkType.Table, "Rows", null, "Values")]
    [InlineData(MarkType.Kpi, null, null, "Values")]
    public void Wells_take_the_names_and_the_use_of_the_visual_type(MarkType mark, string? axis, string? legend, string values)
    {
        var designer = Designer();
        designer.Clear();
        designer.SelectedMark = mark;

        Assert.Equal((axis, legend, values), (Used(designer.AxisWell), Used(designer.LegendWell), Used(designer.ValuesWell)));
        Assert.True(designer.FiltersWell.IsUsed);
    }

    private static string? Used(ShelfViewModel well) => well.IsUsed ? well.Label : null;

    [Fact]
    public void A_well_the_type_does_not_use_refuses_fields_and_stays_shown_while_it_holds_some()
    {
        var designer = Designer(); // Region on the axis
        designer.SelectedMark = MarkType.Kpi;

        Assert.False(designer.AxisWell.IsUsed);
        Assert.True(designer.AxisWell.IsVisible); // to remove Region
        Assert.False(designer.LegendWell.IsVisible);
        var date = designer.Dimensions.Single(d => d.Name == "Date");
        Assert.False(designer.CanDrop(date, Shelf.Axis));
        Assert.Equal("Axis is not used by this type of visual", designer.RefusalReason(date, Shelf.Axis));

        designer.RemoveCommand.Execute(designer.Axis.Single());
        Assert.False(designer.AxisWell.IsVisible);
    }

    [Fact]
    public void While_a_field_is_dragged_each_well_tells_whether_it_accepts_it()
    {
        var designer = Designer();
        Assert.Null(designer.ValuesWell.IsDropAllowed);

        designer.DraggedItem = designer.Measures.Single();
        Assert.Equal((false, false, true, false), (designer.AxisWell.IsDropAllowed, designer.LegendWell.IsDropAllowed, designer.ValuesWell.IsDropAllowed, designer.FiltersWell.IsDropAllowed));

        designer.DraggedItem = null;
        Assert.Null(designer.ValuesWell.IsDropAllowed);
    }

    // --- Undo and redo ---

    [Fact]
    public void Changes_are_undone_and_redone_one_by_one()
    {
        var designer = Designer();
        Assert.False(designer.CanUndo); // loading a visual starts a new history

        designer.Title = "Renamed";
        designer.Values.Single().Aggregation = Aggregation.Average;
        designer.Drop(designer.Dimensions.Single(d => d.Name == "Date"), Shelf.Legend);
        Assert.True(designer.CanUndo);

        designer.Undo();
        Assert.Empty(designer.Legend);
        designer.Undo();
        Assert.Equal(Aggregation.Sum, designer.Values.Single().Aggregation);
        designer.Undo();
        Assert.Equal("Sales", designer.Title);
        Assert.False(designer.CanUndo);

        designer.Redo();
        designer.Redo();
        Assert.Equal(("Renamed", Aggregation.Average), (designer.Title, designer.Values.Single().Aggregation));
        Assert.True(designer.CanRedo);

        designer.SelectedMark = MarkType.Table; // a new change drops what could be redone
        Assert.False(designer.CanRedo);
    }

    [Fact]
    public void Undoing_keeps_the_chip_at_the_place_of_the_selected_one_selected()
    {
        var designer = Designer();
        designer.SelectedItem = designer.Values.Single();
        designer.Values.Single().Time = TimeCalculation.PreviousYear;

        designer.UndoCommand.Execute(null);

        Assert.Equal(TimeCalculation.None, designer.Values.Single().Time);
        Assert.Same(designer.Values.Single(), designer.SelectedItem);
        Assert.Equal(DesignerPropertySections.Field, designer.Properties[0].Key);
    }

    [Fact]
    public void A_new_visual_starts_a_new_history()
    {
        var designer = Designer();
        designer.Title = "Renamed";

        designer.Clear();

        Assert.False(designer.CanUndo);
        Assert.False(designer.UndoCommand.CanExecute(null));
    }
}
