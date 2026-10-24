using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>
/// Measures the developer declares as formulas, and measures of the model that a report redefines (renamed,
/// reformatted, or computed by another formula).
/// </summary>
public class ModelMeasureFormulaTests
{
    private sealed record Row(string Region, decimal Amount, decimal Margin);

    private static readonly Row[] Rows = [new("Nord", 100m, 30m), new("Nord", 300m, 60m), new("Sud", 200m, 100m)];

    private static QueryableSemanticModel<Row> Model(IQueryable<Row>? source = null) =>
        SemanticModel.For(source ?? Rows.AsQueryable(), "m")
            .Dimension(r => r.Region, "Région")
            .Measure(r => r.Amount, "Chiffre d'affaires", format: "#,##0 €")
            .Measure(r => r.Margin, "Marge")
            .Formula("[Margin] / [Amount]", "Taux de marge", "Rate", format: "P1")
            .Formula("SUM([Amount]; [Region] = \"Nord\") / [Amount]", "Part du Nord", "NorthShare");

    private static async Task<ResultSet> ByRegion(MeasureRef measure, params CalculatedMeasure[] calculations) =>
        await Model().ExecuteAsync(new([new("Region")], [measure], Calculations: calculations));

    private static Dictionary<string, double> Values(ResultSet result) =>
        result.Rows.ToDictionary(r => (string)r[0]!, r => Convert.ToDouble(r[1]));

    private static CalculatedMeasure Redefinition(string name, string formula, string caption = "", string? format = null) =>
        new() { Name = name, Caption = caption, Model = "m", Formula = formula, Format = format };

    // --- Formulas declared in code

    [Fact]
    public async Task A_formula_measure_runs_in_the_query_and_shows_its_formula()
    {
        var field = Model().Metadata.GetField("Rate");
        Assert.Equal(("[Margin] / [Amount]", true, "P1"), (field.Formula, field.IsCalculated, field.Format));

        Assert.Equal(90d / 400d, Values(await ByRegion(new("Rate")))["Nord"], 10);
        Assert.Equal(0d, Values(await ByRegion(new("NorthShare")))["Sud"]);
    }

    [Fact]
    public async Task A_formula_measure_can_use_formula_measures_declared_before_it()
    {
        var model = Model().Formula("[Rate] * 100", "Taux en points", "Points");

        var result = await model.ExecuteAsync(new([], [new("Points")]));

        Assert.Equal(190d / 600d * 100, (double)result.Rows[0][0]!, 10);
    }

    [Theory]
    [InlineData("[Later] * 2")]
    [InlineData("[Region]")]
    [InlineData("SUM([Rate])")]
    public void A_formula_that_does_not_match_the_fields_declared_so_far_is_refused(string formula)
    {
        var problem = Assert.Throws<ArgumentException>(() => Model().Formula(formula, "Mesure", "Bad"));

        Assert.StartsWith("Invalid formula for 'Bad': ", problem.Message);
    }

    // --- Redefined by a report

    [Fact]
    public async Task A_report_measure_named_like_a_model_measure_replaces_its_computation()
    {
        var result = await ByRegion(new("Amount"), Redefinition("Amount", "AVERAGE([Amount])", "CA moyen", "N0"));

        Assert.Equal(200d, Values(result)["Nord"]);
        Assert.Equal(("CA moyen", "N0"), (result.Columns[1].Caption, result.Columns[1].Format));
    }

    [Fact]
    public async Task A_redefinition_reads_the_models_definition_of_the_measure_itself()
    {
        var result = await ByRegion(new("Rate"), Redefinition("Rate", "[Rate] * 100"));

        Assert.Equal(90d / 400d * 100, Values(result)["Nord"], 10);
        Assert.Equal("Taux de marge", result.Columns[1].Caption); // an empty caption keeps the model's
    }

    [Fact]
    public async Task A_redefinition_that_is_only_the_measure_keeps_the_aggregation_chosen_by_the_visual()
    {
        var result = await ByRegion(new("Amount", Aggregation.Max), Redefinition("Amount", "([Amount])", "Ventes", "N0"));

        Assert.Equal(300d, Values(result)["Nord"]);
        Assert.StartsWith("Ventes (", result.Columns[1].Caption);
        Assert.Equal("N0", result.Columns[1].Format);
    }

    [Fact]
    public void The_report_lists_the_model_measures_as_it_redefines_them()
    {
        var metadata = Model().Metadata;
        var context = new MeasureContext();
        context.Set(Redefinition("Amount", "[Amount]", "Ventes", "N0"));
        context.Set(Redefinition("Margin", "MAX([Margin])"));
        context.Set(new CalculatedMeasure { Name = "calc_1", Caption = "Double", Model = "m", Formula = "[Amount] * 2" });

        var fields = context.MeasuresOf(metadata).ToArray();

        Assert.Equal(["Ventes", "Marge", "Taux de marge", "Part du Nord", "Double"], fields.Select(f => f.Caption));
        Assert.Equal(("Amount", "N0", false, Aggregation.Sum), (fields[0].Name, fields[0].Format, fields[0].IsCalculated, fields[0].DefaultAggregation));
        Assert.True(fields[1].IsCalculated);
        Assert.Equal(fields[1], context.GetField(metadata, "Margin"));
        Assert.Equal(metadata.GetField("Region"), context.GetField(metadata, "Region"));
    }

    [Fact]
    public void A_model_measure_is_edited_from_its_definition()
    {
        var metadata = Model().Metadata;

        Assert.Equal("[Amount]", MeasureContext.Definition(metadata.GetField("Amount"), "m").Formula);
        Assert.Equal("[Margin] / [Amount]", MeasureContext.Definition(metadata.GetField("Rate"), "m").Formula);
        Assert.Equal("#,##0 €", MeasureContext.Definition(metadata.GetField("Amount"), "m").Format);
    }

    [Fact]
    public void Measures_of_different_models_may_share_a_name()
    {
        var context = new MeasureContext();
        context.Set(Redefinition("Amount", "[Amount]", "A"));
        context.Set(Redefinition("Amount", "[Amount]", "B") with { Model = "other" });
        context.Set(Redefinition("Amount", "[Amount]", "C"));

        Assert.Equal(["C", "B"], context.Measures.Select(m => m.Caption));
        context.Remove("other", "Amount");
        Assert.Equal("C", Assert.Single(context.Measures).Caption);
    }

    [Fact]
    public void Pasting_a_visual_never_redefines_the_targets_measures()
    {
        var metadata = Model().Metadata;
        var visual = new VisualDefinition { Id = "v", Model = "m", Mark = MarkType.Kpi, Values = [new("Amount")] };
        var source = new MeasureContext();
        source.Set(Redefinition("Amount", "AVERAGE([Amount])"));
        var json = VisualTransfer.Copy([visual], source);

        var target = new MeasureContext();
        var pasted = Assert.Single(VisualTransfer.Paste(json, target, _ => true, _ => metadata)).Visual;

        var added = Assert.Single(target.Measures);
        Assert.StartsWith("calc_", added.Name);
        Assert.Equal(("AVERAGE([Amount])", "Chiffre d'affaires"), (added.Formula, added.Caption)); // the model's caption is filled in
        Assert.Equal(added.Name, pasted.Values[0].Measure);

        // A target that redefines the measure the same way keeps it
        var same = new MeasureContext();
        same.Set(Redefinition("Amount", "AVERAGE([Amount])"));
        Assert.Equal("Amount", VisualTransfer.Paste(json, same, _ => true, _ => metadata)[0].Visual.Values[0].Measure);
        Assert.Single(same.Measures);
    }

    [Fact]
    public void Opening_a_report_checks_its_redefinitions()
    {
        var report = new ReportDefinition { Measures = [Redefinition("Amount", "SUM([Missing])")] };

        var problem = Assert.Single(ReportValidator.Validate(report, [Model().Metadata]));

        Assert.Contains("“Amount”", problem);
    }
}
