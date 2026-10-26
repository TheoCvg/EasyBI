using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Texts a visual shows instead of the values of its dimensions (Île-de-France shown as IDF).</summary>
public class ValueLabelTests
{
    private sealed record Sale(string Month, string Region, string Channel, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("2024-01", "Île-de-France", "Web", 10m),
        new("2024-01", "Nord", "Magasin", 20m),
        new("2024-02", "Île-de-France", "Magasin", 30m),
        new("2024-02", "Nord", "Web", 40m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Month, "Mois")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA");

    private static readonly FieldRef Region = new FieldRef("Region").WithLabel("Île-de-France", "IDF");

    private static Task<RenderRequest> PrepareAsync(VisualDefinition visual) => VisualPipeline.PrepareAsync(Model(), visual);

    [Fact]
    public async Task Axis_categories_show_the_labels_and_keep_their_values()
    {
        var request = await PrepareAsync(new VisualDefinition { Model = "sales", Type = VisualType.Bar, Axis = [Region], Values = [new("Amount")] });

        Assert.Equal(["IDF", "Nord"], request.Chart.Categories);
        Assert.Equal("Île-de-France", request.Chart.CategoryValues[0][0]);
        // Clicking it still filters on the value itself
        var element = SelectionFilters.Element(request, new CategorySelection(0))!;
        Assert.Equal("Île-de-France", Assert.Single(element.Values));
        Assert.Equal("Île-de-France", Assert.Single(Assert.Single(element.Filters).Values));
    }

    [Fact]
    public void Fields_with_the_same_labels_are_equal()
    {
        Assert.Equal(Region, new FieldRef("Region").WithLabel("Île-de-France", "IDF"));
        Assert.NotEqual(Region, new FieldRef("Region"));
        Assert.NotEqual(Region, new FieldRef("Region").WithLabel("Île-de-France", "Paris"));
    }

    [Fact]
    public async Task Legend_series_and_the_levels_of_an_axis_show_the_labels()
    {
        var request = await PrepareAsync(new VisualDefinition
        {
            Model = "sales",
            Type = VisualType.Bar,
            Axis = [new("Month"), Region],
            Legend = new FieldRef("Channel").WithLabel("Magasin", "Shop"),
            Values = [new("Amount")],
        });

        Assert.Equal(["Web", "Shop"], request.Chart.Series.Select(s => s.Name).OrderByDescending(n => n));
        Assert.Contains("2024-01 / IDF", request.Chart.Categories);
        Assert.Equal(["IDF", "Nord", "IDF", "Nord"], CategoryLevels.Of(request.Chart)!.Labels);
    }

    [Fact]
    public async Task Matrix_rows_and_columns_show_the_labels()
    {
        var matrix = (await PrepareAsync(new VisualDefinition
        {
            Model = "sales",
            Type = VisualType.Matrix,
            Axis = [Region],
            Legend = new FieldRef("Channel").WithLabel("Web", "Online"),
            Values = [new("Amount")],
        })).Matrix!;

        Assert.Contains(matrix.Rows, r => r.Labels[0] == "IDF");
        Assert.Contains(matrix.Columns, c => c.Labels[0] == "Online");
    }

    [Fact]
    public void A_blank_label_shows_the_value_again()
    {
        var field = Region.WithLabel("Nord", "N").WithLabel("Île-de-France", "  ");

        Assert.Equal("Île-de-France", field.Display("Île-de-France"));
        Assert.Equal("N", field.Display("Nord"));
        Assert.Null(field.WithLabel("Nord", "").Labels);
    }

    [Fact]
    public void Labels_are_saved_with_the_field_and_omitted_when_there_are_none()
    {
        var json = JsonSerializer.Serialize(Region, ReportSerializer.Options);

        Assert.Equal("IDF", JsonSerializer.Deserialize<FieldRef>(json, ReportSerializer.Options)!.Labels!["Île-de-France"]);
        Assert.DoesNotContain("labels", JsonSerializer.Serialize(new FieldRef("Region"), ReportSerializer.Options));
    }
}
