using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

/// <summary>Value labels in the designer: the texts a visual shows instead of the values of a dimension.</summary>
public class ValueLabelsUiTests
{
    private sealed record Sale(string Region, DateTime Date, decimal Amount);

    private static DesignerViewModel Designer()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Île-de-France", new(2024, 1, 1), 1m), new Sale("Nord", new(2024, 2, 1), 2m) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "Amount")));
        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Amount")] });
        return designer;
    }

    [Fact]
    public async Task Labels_are_added_one_at_a_time_among_the_values_of_the_dimension()
    {
        var designer = Designer();
        designer.SelectedItem = designer.Axis.Single();

        var section = designer.Properties.Single(s => s.Key == DesignerPropertySections.ValueLabels);
        var editor = Assert.IsType<ValueLabelsViewModel>(section["Labels"]!.Value);
        await editor.Loaded;
        Assert.Empty(editor.Labels);
        Assert.Equal(["Île-de-France", "Nord"], editor.AvailableValues.Select(v => v.Label));
        Assert.False(editor.AddCommand.CanExecute(null));

        editor.NewValue = editor.AvailableValues[0];
        editor.NewText = "IDF";
        editor.AddCommand.Execute(null);

        Assert.Equal("IDF", designer.Visual!.Axis[0].Labels!["Île-de-France"]);
        Assert.Equal(["Nord"], editor.AvailableValues.Select(v => v.Label));
        var label = Assert.Single(editor.Labels);
        Assert.Equal((null, ""), (editor.NewValue, editor.NewText));

        label.Text = "Paris";
        Assert.Equal("Paris", designer.Visual!.Axis[0].Labels!["Île-de-France"]);

        editor.RemoveCommand.Execute(label);
        Assert.Null(designer.Visual!.Axis[0].Labels);
        Assert.Equal(["Île-de-France", "Nord"], editor.AvailableValues.Select(v => v.Label));
    }

    [Fact]
    public async Task The_labels_of_a_loaded_visual_are_listed()
    {
        var designer = Designer();
        designer.Axis.Single().SetLabel("Nord", "N");
        designer.Load(designer.Visual!);
        designer.SelectedItem = designer.Axis.Single();

        var editor = (ValueLabelsViewModel)designer.Properties.Single(s => s.Key == DesignerPropertySections.ValueLabels)["Labels"]!.Value!;
        await editor.Loaded;

        Assert.Equal(("Nord", "N"), (Assert.Single(editor.Labels).Original, editor.Labels[0].Text));
        Assert.Equal(["Île-de-France"], editor.AvailableValues.Select(v => v.Label));
    }

    [Fact]
    public void Dates_have_no_value_labels()
    {
        var designer = Designer();
        designer.Drop(designer.Dimensions.Single(f => f.Name == "Date"), Shelf.Axis);

        designer.SelectedItem = designer.Axis.Single(c => c.Field.Name == "Date");

        Assert.DoesNotContain(designer.Properties, s => s.Key == DesignerPropertySections.ValueLabels);
    }

    [Fact]
    public void Labels_are_kept_when_the_visual_is_loaded_again_and_when_the_chip_moves_to_the_legend()
    {
        var designer = Designer();
        designer.Axis.Single().SetLabel("Île-de-France", "IDF");
        designer.Load(designer.Visual!);
        Assert.Equal("IDF", designer.Axis.Single().Labels!["Île-de-France"]);

        designer.Drop(designer.Axis.Single(), Shelf.Legend);

        Assert.Equal("IDF", designer.Visual!.Legend!.Labels!["Île-de-France"]);
    }

    [Fact]
    public void A_table_shows_the_labels_of_its_dimensions() => Sta.Run(() =>
    {
        var field = new FieldRef("Region").WithLabel("Île-de-France", "IDF");
        var visual = new VisualDefinition { Mark = MarkType.Table, Axis = [field], Values = [new("Amount")] };
        var result = new ResultSet(
            [new ResultColumn("Region", "Région", FieldKind.Dimension, DataType.Text), new ResultColumn("Amount", "CA", FieldKind.Measure, DataType.Number)],
            [["Île-de-France", 1m], ["Nord", 2m]]);
        var request = new RenderRequest(visual, result, ChartDataBuilder.Build(visual, result), BiTheme.Default);

        var grid = (DataGrid)new TableRenderer().Render(request);

        var binding = (System.Windows.Data.Binding)((DataGridTextColumn)grid.Columns[0]).Binding;
        Assert.Equal("IDF", binding.Converter.Convert("Île-de-France", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Nord", binding.Converter.Convert("Nord", typeof(string), null, System.Globalization.CultureInfo.InvariantCulture));
    });
}
