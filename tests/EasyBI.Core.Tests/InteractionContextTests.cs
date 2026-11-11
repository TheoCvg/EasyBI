using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>What the user acts on in a visual, and values imposed on a report without being saved.</summary>
public class InteractionContextTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, decimal Margin);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Web", 100m, 10m),
        new("Nord", "Magasin", 200m, 20m),
        new("Sud", "Web", 300m, 30m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA")
            .Measure(s => s.Margin, "Marge");

    private static readonly VisualDefinition ByRegion = new()
    {
        Id = "regions",
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount"), new("Margin")],
        Filters = [new("Amount", FilterOperator.GreaterThan, ["50"])],
    };

    private static VariableContext Variables()
    {
        var variables = new VariableContext();
        variables.Load([
            new ReportVariable { Name = "N", Model = "sales", Formula = "10 BETWEEN 1 AND 20" },
            new ReportVariable { Name = "Best", Model = "sales", Formula = "SELECT TOP 1 [Region] ORDER BY [Amount] DESC" },
        ]);
        return variables;
    }

    [Fact]
    public async Task An_element_of_a_chart_brings_its_dimensions_its_measure_and_everything_filtering_the_visual()
    {
        var filters = new FilterContext();
        filters.Load([new ReportFilter("sales", new("Channel", FilterOperator.In, ["Web", "Magasin"]))]);
        var request = await VisualPipeline.PrepareAsync(Model(), filters.Apply(ByRegion));
        var series = request.Chart.Series.ToList().FindIndex(s => s.Name.Contains("Marge") && Equals(s.LegendValue, "Web"));
        var variables = Variables();
        variables.SetValue("N", "5");

        var context = InteractionContext.Create(ByRegion, request, new CategorySelection(request.Chart.Categories.ToList().IndexOf("Sud"), series),
            filters, variables);

        Assert.Equal(["Region", "Channel"], context.Element!.Fields.Select(f => f.Field));
        Assert.Equal(["Sud", "Web"], context.Element.Values);
        Assert.Equal("Margin", context.Measure!.Measure);
        Assert.Equal(["Amount", "Channel"], context.Filters.Select(f => f.Field));
        Assert.Equal(["Amount", "Channel", "Region", "Channel"], context.DataFilters.Select(f => f.Field));
        Assert.Equal(new Dictionary<string, string?> { ["N"] = "5" }, context.Parameters);
    }

    [Fact]
    public void The_visual_as_a_whole_brings_its_filters_and_those_of_its_page()
    {
        var filters = new FilterContext();
        filters.Load([new ReportFilter("sales", new("Region", FilterOperator.In, ["Nord"]))]);

        var context = InteractionContext.Create(ByRegion with { Values = [new("Amount")] }, null, null, filters, Variables());

        Assert.Null(context.Element);
        Assert.Equal("Amount", context.Measure!.Measure);
        Assert.Equal(["Amount", "Region"], context.Filters.Select(f => f.Field));
        Assert.Empty(context.ElementFilters);
        Assert.Equal(["N"], context.Parameters.Keys);
    }

    [Fact]
    public async Task A_kpi_has_no_element_and_a_bar_of_several_measures_without_series_no_measure()
    {
        var kpi = new VisualDefinition { Model = "sales", Type = VisualType.Kpi, Values = [new("Amount")] };
        var request = await VisualPipeline.PrepareAsync(Model(), kpi);

        Assert.Null(InteractionContext.Create(kpi, request, new CategorySelection(0)).Element);
        Assert.Null(InteractionContext.Create(ByRegion, null, new CategorySelection(0)).Measure);
    }

    // --- Imposed filters

    private static readonly ReportFilter North = new("sales", new("Region", FilterOperator.In, ["Nord"]));
    private static readonly ReportFilter South = new("sales", new("Region", FilterOperator.In, ["Sud"]));
    private static readonly ReportFilter Web = new("sales", new("Channel", FilterOperator.In, ["Web"]));

    [Fact]
    public void Imposed_filters_apply_but_the_report_saves_those_they_replaced()
    {
        var filters = new FilterContext();
        filters.Load([North]);
        var changes = 0;
        filters.Changed += (_, _) => changes++;

        filters.Impose([South, Web]);

        Assert.Equal(1, changes);
        Assert.Equal([South, Web], filters.Filters);
        Assert.Equal(["Sud"], filters.FiltersFor(ByRegion)[0].Values);
        Assert.Equal([North], filters.SavedFilters);
        Assert.True(filters.IsImposed("sales", "Region"));

        filters.ClearImposed();

        Assert.Equal([North], filters.Filters);
        Assert.False(filters.IsImposed("sales", "Region"));
    }

    [Fact]
    public void An_imposed_filter_the_user_changes_or_removes_is_the_users()
    {
        var filters = new FilterContext();
        filters.Load([North]);
        filters.Impose([South, Web]);

        filters.SetFilter("sales", new("Region", FilterOperator.In, ["Est"]));
        filters.RemoveFilter("sales", "Channel");

        Assert.Equal(["Est"], Assert.Single(filters.SavedFilters).Filter.Values);
        Assert.False(filters.IsImposed("sales", "Region"));
    }

    [Fact]
    public void Loading_a_report_forgets_the_imposed_filters()
    {
        var filters = new FilterContext();
        filters.Impose([Web]);

        filters.Load([North]);

        Assert.Equal([North], filters.SavedFilters);
        Assert.False(filters.IsImposed("sales", "Channel"));
    }

    // --- Imposed values

    [Fact]
    public void Imposed_values_apply_but_the_report_saves_those_they_replaced()
    {
        var variables = Variables();
        variables.SetValue("N", "3");
        var changes = 0;
        variables.Changed += (_, _) => changes++;

        variables.Impose(new Dictionary<string, string?> { ["N"] = "7", ["Best"] = "Nord", ["Unknown"] = "1" });

        Assert.Equal(1, changes);
        Assert.Equal("7", VariableContext.ValueOf(variables.Find("N")!));
        Assert.Equal("3", variables.SavedVariables.Single(v => v.Name == "N").Value);
        Assert.Equal(variables.Find("Best"), variables.SavedVariables.Single(v => v.Name == "Best"));
        Assert.True(variables.IsImposed("N"));
        Assert.False(variables.IsImposed("Best"));

        variables.ClearImposed();

        Assert.Equal("3", variables.Find("N")!.Value);
    }

    [Fact]
    public void A_value_the_user_changes_after_it_was_imposed_is_saved()
    {
        var variables = Variables();
        variables.Impose(new Dictionary<string, string?> { ["N"] = "7" });

        variables.SetValue("N", "8");

        Assert.Equal("8", variables.SavedVariables.Single(v => v.Name == "N").Value);
        Assert.False(variables.IsImposed("N"));
    }

    [Fact]
    public void Imposing_the_value_of_the_formula_keeps_the_formulas_value()
    {
        var variables = Variables();
        variables.SetValue("N", "3");

        variables.Impose(new Dictionary<string, string?> { ["N"] = "10" });

        Assert.Null(variables.Find("N")!.Value);
        Assert.Equal("10", VariableContext.ValueOf(variables.Find("N")!));
    }
}
