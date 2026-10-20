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
}
