using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Formulas of calculated measures that read the rows of each group: aggregation functions, conditions, dimensions.</summary>
public class FormulaFunctionTests
{
    private sealed record Sale(string Region, string? Customer, DateTime Date, bool Online, int Quantity, decimal Price, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Alice", new(2024, 1, 10), true, 2, 50m, 100m),
        new("Nord", "Bob", new(2024, 2, 5), false, 1, 300m, 300m),
        new("Nord", "Alice", new(2024, 3, 1), true, 4, 25m, 100m),
        new("Sud", null, new(2024, 1, 20), false, 3, 40m, 120m),
        new("Sud", "Chloé", new(2024, 2, 15), true, 1, 80m, 80m),
    ];

    private static QueryableSemanticModel<Sale> Model(IQueryable<Sale>? source = null) =>
        SemanticModel.For(source ?? Sales.AsQueryable(), "m")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Customer, "Client")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Online, "En ligne")
            .Dimension(s => s.Price, "Prix")
            .Measure(s => s.Quantity, "Quantité")
            .Measure(s => s.Amount, "CA")
            .Count("Ventes")
            .Ratio(s => s.Amount, s => s.Quantity, "Prix moyen", "AveragePrice");

    private static CalculatedMeasure Measure(string formula) =>
        new() { Name = "calc_1", Caption = "Mesure", Model = "m", Formula = formula };

    private static async Task<Dictionary<string, double?>> ByRegion(string formula, IQueryable<Sale>? source = null)
    {
        var result = await Model(source).ExecuteAsync(new([new("Region")], [new("calc_1")], Calculations: [Measure(formula)]));
        return result.Rows.ToDictionary(r => (string)r[0]!, r => (double?)r[1]);
    }

    private static async Task<double?> Total(string formula)
    {
        var result = await Model().ExecuteAsync(new([], [new("calc_1")], Calculations: [Measure(formula)]));
        return (double?)result.Rows.Single()[0];
    }

    // --- Computing

    [Fact]
    public async Task Aggregation_functions_choose_how_a_measure_is_aggregated()
    {
        Assert.Equal(500d, (await ByRegion("SUM([Amount])"))["Nord"]);
        Assert.Equal(500d / 3, (await ByRegion("AVERAGE([Amount])"))["Nord"]!.Value, 10);
        Assert.Equal(500d / 3, (await ByRegion("avg([Amount])"))["Nord"]!.Value, 10);
        Assert.Equal(100d, (await ByRegion("MIN([Amount])"))["Nord"]);
        Assert.Equal(300d, (await ByRegion("MAX([Amount])"))["Nord"]);
        Assert.Equal(3d, (await ByRegion("COUNT()"))["Nord"]);
    }

    [Fact]
    public async Task Inside_a_function_dimensions_and_measures_are_read_row_by_row()
    {
        Assert.Equal(2 * 50d + 300 + 4 * 25, (await ByRegion("SUM([Quantity] * [Price])"))["Nord"]);
        Assert.Equal(300d, (await ByRegion("MAX([Price] * [Quantity])"))["Nord"]);
    }

    [Fact]
    public async Task A_condition_keeps_only_the_rows_matching_it()
    {
        Assert.Equal(500d, await Total("SUM([Amount]; [Region] = \"Nord\")"));
        Assert.Equal(200d, await Total("SUM([Amount], [Region] <> \"Nord\")"));
        Assert.Equal(3d, await Total("COUNT([Online])"));
        Assert.Equal(2d, await Total("COUNT(NOT [Online] AND [Price] >= 40)"));
        Assert.Equal(600d, await Total("SUM([Amount]; [Date] >= \"2024-02-01\" OR [Region] = \"Sud\" AND [Online] = FALSE)"));
        Assert.Equal(80d, await Total("MIN([Amount]; [Online] = TRUE AND [Region] = \"Sud\")"));
    }

    [Fact]
    public async Task Rows_outside_the_condition_do_not_count_in_an_average()
    {
        Assert.Equal(100d, (await ByRegion("AVERAGE([Amount]; [Online])"))["Nord"]);
        Assert.Null((await ByRegion("AVERAGE([Amount]; [Price] > 1000)"))["Nord"]);
    }

    [Fact]
    public async Task A_distinct_count_skips_missing_values()
    {
        var customers = await ByRegion("DISTINCTCOUNT([Customer])");

        Assert.Equal(2d, customers["Nord"]);
        Assert.Equal(1d, customers["Sud"]);
        Assert.Equal(1d, (await ByRegion("DISTINCTCOUNT([Customer]; [Online])"))["Nord"]);
        Assert.Equal(2d, (await ByRegion("DISTINCTCOUNT([Online])"))["Sud"]);
    }

    [Fact]
    public async Task IF_chooses_a_value_for_the_group_or_for_each_row()
    {
        var big = await ByRegion("IF([Amount] > 300; [Amount]; 0)");
        Assert.Equal((500d, 0d), (big["Nord"], big["Sud"]));
        Assert.Null((await ByRegion("IF([Amount] > 300; [Amount])"))["Sud"]);
        Assert.Equal(280d, await Total("SUM(IF([Online]; [Amount]; 0))"));
        Assert.Equal(1d, await Total("IF(\"a\" = \"a\" AND TRUE; 1; 0)"));
    }

    [Fact]
    public async Task Functions_combine_with_the_measures_of_the_model()
    {
        var share = await ByRegion("SUM([Amount]; [Online]) / [Amount]");

        Assert.Equal(200d / 500d, share["Nord"]!.Value, 10);
        Assert.Equal(80d / 200d, share["Sud"]!.Value, 10);
        Assert.Equal(500d / 7 - 100d, (await ByRegion("[AveragePrice] - MIN([Amount])"))["Nord"]!.Value, 10);
    }

    [Fact]
    public async Task Functions_are_computed_by_the_provider_in_the_same_query()
    {
        var source = new RecordingQueryable<Sale>(Sales);

        await ByRegion("SUM([Amount]; [Region] = \"Nord\") + DISTINCTCOUNT([Customer])", source);

        var query = Assert.Single(source.Log.Executed);
        Assert.Contains("Sum(r => IIF((r.Region == \"Nord\")", query);
        Assert.Contains("Distinct()", query);
    }

    // --- Checking

    [Theory]
    [InlineData("[Region]", "[Region] is a dimension: use it inside a function")]
    [InlineData("SUM([Amount]) + [Customer]", "[Customer] is a dimension")]
    [InlineData("SUM(SUM([Amount]))", "SUM(...) cannot be used inside SUM(...)")]
    [InlineData("SUM([AveragePrice])", "[AveragePrice] is computed for each group: it cannot be used inside SUM(...)")]
    [InlineData("TOTAL([Amount])", "Unknown function: “TOTAL”")]
    [InlineData("SUM()", "Write SUM(value) or SUM(value; condition).")]
    [InlineData("COUNT([Amount])", "“[Amount]” is not a condition")]
    [InlineData("IF([Amount]; 1; 0)", "“[Amount]” is not a condition")]
    [InlineData("SUM([Region])", "“[Region]” does not give a number")]
    [InlineData("SUM([Amount]; [Region] > \"Nord\")", "only compared with = or <>")]
    [InlineData("SUM([Amount]; [Price] = \"Nord\")", "“[Price]” and “\"Nord\"” cannot be compared")]
    [InlineData("SUM([Amount]; [Date] > \"hier\")", "\"hier\" is not a date")]
    [InlineData("[Amount] > 3", "“[Amount] > 3” does not give a number")]
    [InlineData("SUM([Amount]; [Region] = \"Nord)", "closing quote")]
    [InlineData("SUM([Amount]; [Region] = \"Nord\"", "closing parenthesis")]
    [InlineData("IF(1 = 1; 2; 3; 4)", "Write IF(condition; value if true; value otherwise).")]
    [InlineData("SUM([Amount] * 0,5)", "decimal")]
    public void Invalid_formulas_are_explained(string formula, string expected)
    {
        var problem = MeasureFormula.Validate(formula, Model().Metadata);

        Assert.NotNull(problem);
        Assert.Contains(expected, problem);
    }

    [Theory]
    [InlineData("SUM([Amount]; [Region] = \"Nord\") / [Amount]")]
    [InlineData("COUNT() - DISTINCTCOUNT([Customer]; [Online] AND [Date] < \"2024-03-01\")")]
    [InlineData("IF(SUM([Amount]) >= 100 OR NOT [Count] = 0; 1; -1)")]
    [InlineData("Sum([Quantity] * [Price], ([Region] = \"Sud\"))")]
    public void Valid_formulas_have_no_problem(string formula)
    {
        Assert.Null(MeasureFormula.Validate(formula, Model().Metadata));
    }

    // --- What the user reads and types

    [Fact]
    public void Dimensions_are_typed_with_their_captions_and_texts_are_left_as_written()
    {
        var metadata = Model().Metadata;
        const string stored = "SUM([Amount]; [Region] = \"[Region] \"\"N\"\"\")";

        Assert.Equal("SUM([CA]; [Région] = \"[Region] \"\"N\"\"\")", MeasureFormula.ToDisplay(stored, metadata));
        Assert.Equal(stored, MeasureFormula.FromDisplay("SUM([CA]; [Région] = \"[Region] \"\"N\"\"\")", metadata, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Problems_of_a_typed_formula_name_the_fields_as_typed()
    {
        Assert.Null(MeasureFormula.FromDisplay("[Région] / [CA]", Model().Metadata, out var error));
        Assert.StartsWith("[Région] is a dimension", error);
    }

    [Fact]
    public void Each_function_has_a_usage_and_a_description()
    {
        Assert.All(MeasureFormula.Functions, function =>
        {
            Assert.StartsWith(function.Name + "(", function.Usage);
            Assert.NotEqual("FormulaDescription" + function.Name, function.Description);
        });
    }
}
