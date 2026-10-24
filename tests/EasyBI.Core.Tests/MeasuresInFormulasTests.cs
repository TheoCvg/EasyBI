using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Calculated measures of the report used in other formulas, as the report defines them.</summary>
public class MeasuresInFormulasTests
{
    private sealed record Row(string Region, decimal Amount, decimal Margin);

    private static readonly Row[] Rows = [new("Nord", 100m, 30m), new("Nord", 300m, 60m), new("Sud", 200m, 100m)];

    private static QueryableSemanticModel<Row> Model() =>
        SemanticModel.For(Rows, "m")
            .Dimension(r => r.Region, "Région")
            .Measure(r => r.Amount, "CA")
            .Measure(r => r.Margin, "Marge")
            .Formula("[Amount] * 2", "Double CA", "DoubleAmount");

    private static CalculatedMeasure Measure(string name, string caption, string formula) =>
        new() { Name = name, Caption = caption, Model = "m", Formula = formula };

    private static readonly CalculatedMeasure Cost = Measure("calc_cost", "Coût", "[Amount] - [Margin]");
    private static readonly CalculatedMeasure CostRate = Measure("calc_rate", "Taux de coût", "[calc_cost] / [Amount]");
    private static readonly CalculatedMeasure CostPoints = Measure("calc_points", "Points de coût", "[calc_rate] * 100");

    private static async Task<Dictionary<string, double?>> ByRegion(string measure, params CalculatedMeasure[] measures)
    {
        var visual = new VisualDefinition { Model = "m", Mark = MarkType.Bar, Axis = [new("Region")], Values = [new(measure)] };
        var model = Model();
        var result = await model.ExecuteAsync(visual.ToQuery(measures, model.Metadata));
        return result.Rows.ToDictionary(r => (string)r[0]!, r => r[1] is null ? null : (double?)Convert.ToDouble(r[1]));
    }

    // --- Computing

    [Fact]
    public async Task A_formula_uses_other_measures_of_the_report_through_several_levels()
    {
        var points = await ByRegion("calc_points", Cost, CostRate, CostPoints);

        Assert.Equal((400d - 90d) / 400d * 100, points["Nord"]!.Value, 10);
        Assert.Equal(50d, points["Sud"]!.Value, 10);
    }

    [Fact]
    public void A_query_carries_the_measures_its_measures_use_and_no_other()
    {
        var unused = Measure("calc_unused", "Autre", "[Margin]");
        var visual = new VisualDefinition { Model = "m", Values = [new("calc_points")] };

        Assert.Equal([Cost, CostRate, CostPoints], visual.ToQuery([unused, Cost, CostRate, CostPoints]).Calculations!);

        var context = new MeasureContext();
        context.Load([unused, Cost, CostRate, CostPoints]);
        Assert.Equal([Cost, CostRate, CostPoints], context.UsedBy(visual));
        Assert.Equal([CostRate], context.Using("m", "calc_cost"));
    }

    [Fact]
    public async Task Other_formulas_read_a_redefined_measure_as_the_report_defines_it()
    {
        var average = Measure("Amount", "", "AVERAGE([Amount])");
        var share = Measure("calc_share", "Part", "[Margin] / [Amount]");

        // The redefinition reads the model's Amount under its own name; the share and the model's formula read the redefinition
        Assert.Equal(200d, (await ByRegion("Amount", average))["Nord"]);
        Assert.Equal(90d / 200d, (await ByRegion("calc_share", average, share))["Nord"]!.Value, 10);
        Assert.Equal(400d, (await ByRegion("DoubleAmount", average))["Nord"]);
        Assert.Equal(800d, (await ByRegion("DoubleAmount"))["Nord"]);
    }

    // --- Checking

    [Fact]
    public void A_measure_cannot_use_itself_even_through_others()
    {
        var metadata = Model().Metadata;
        var first = Measure("calc_a", "A", "[calc_b] + 1");
        var second = Measure("calc_b", "B", "[calc_c] * 2");
        var third = Measure("calc_c", "C", "[calc_a] - 1");

        Assert.Equal("A measure cannot use itself: this measure → [B] → [C] → this measure.",
            MeasureFormula.Validate(first.Formula, metadata, [first, second, third], first.Name));
        Assert.Contains("Unknown field: “calc_a”", MeasureFormula.Validate("[calc_a] * 2", metadata, [first], "calc_a"));
        Assert.Null(MeasureFormula.Validate(second.Formula, metadata, [second, third], second.Name));

        var problem = MeasureFormula.Check("[CA] + [B]", metadata, out _, [first, second, third], "calc_a");
        Assert.Equal((7, 3), (problem!.Start, problem.Length)); // [B] is where the loop starts
    }

    [Fact]
    public void A_redefinition_reads_the_models_measure_under_its_own_name_without_looping()
    {
        var metadata = Model().Metadata;
        var average = Measure("Amount", "", "AVERAGE([Amount])");
        var share = Measure("calc_share", "Part", "[Margin] / [Amount]");

        Assert.Null(MeasureFormula.Validate("[Amount] * [calc_share]", metadata, [average, share], "Amount"));
        Assert.Null(MeasureFormula.Validate("[DoubleAmount] / 2", metadata, [average], "Amount"));
    }

    [Fact]
    public void A_report_measure_is_computed_for_each_group_and_cannot_be_read_row_by_row()
    {
        var problem = MeasureFormula.Validate("SUM([calc_cost])", Model().Metadata, [Cost]);

        Assert.Contains("[calc_cost] is computed for each group", problem);
    }

    [Fact]
    public void Report_measures_are_typed_and_read_with_their_captions()
    {
        var metadata = Model().Metadata;
        var average = Measure("Amount", "CA moyen", "AVERAGE([Amount])");

        Assert.Equal("[Coût] / [CA moyen]", MeasureFormula.ToDisplay("[calc_cost] / [Amount]", metadata, [Cost, average]));
        Assert.Equal("[calc_cost] / [Amount]", MeasureFormula.FromDisplay("[coût] / [CA moyen]", metadata, out var error, [Cost, average]));
        Assert.Null(error);
        // In the redefinition itself, the measure is the model's
        Assert.Equal("AVERAGE([CA])", MeasureFormula.ToDisplay(average.Formula, metadata, [Cost, average], "Amount"));
    }

    [Fact]
    public void Opening_a_report_finds_measures_that_use_themselves()
    {
        var report = new ReportDefinition { Measures = [Measure("calc_a", "A", "[calc_b]"), Measure("calc_b", "B", "[calc_a]")] };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, p => Assert.Contains("cannot use itself", p));
    }

    // --- Copying

    [Fact]
    public void Pasted_measures_follow_those_they_use_when_renamed()
    {
        var visual = new VisualDefinition { Id = "v", Model = "m", Mark = MarkType.Kpi, Values = [new("calc_rate")] };
        var source = new MeasureContext();
        source.Load([Cost, CostRate]);
        var json = VisualTransfer.Copy([visual], source);

        // The target uses calc_cost for another definition: the pasted cost is renamed, and the rate follows it
        var target = new MeasureContext();
        target.Set(Cost with { Formula = "[Margin]" });
        VisualTransfer.Paste(json, target, _ => true);

        var pastedCost = target.Measures.Single(m => m.Caption == "Coût" && m.Name != "calc_cost");
        Assert.Equal($"[{pastedCost.Name}] / [Amount]", target.Measures.Single(m => m.Name == "calc_rate").Formula);
    }
}
