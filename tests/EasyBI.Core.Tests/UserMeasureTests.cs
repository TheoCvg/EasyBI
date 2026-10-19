using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Calculated measures the end user writes as formulas, saved with the report.</summary>
public class UserMeasureTests
{
    private sealed record Row(string Region, decimal Amount, decimal Margin);

    private static readonly Row[] Rows =
    [
        new("Nord", 100m, 30m),
        new("Nord", 300m, 60m),
        new("Sud", 200m, 100m),
        new("Est", 0m, 0m),
    ];

    private static QueryableSemanticModel<Row> Model(IQueryable<Row>? source = null) =>
        SemanticModel.For(source ?? Rows.AsQueryable(), "m")
            .Dimension(r => r.Region, "Région")
            .Measure(r => r.Amount, "Chiffre d'affaires")
            .Measure(r => r.Margin, "Marge")
            .Count("Nb ventes")
            .Ratio(r => r.Margin, r => r.Amount, "Taux", "Rate");

    private static CalculatedMeasure Measure(string formula, string? format = null) =>
        new() { Name = "calc_1", Caption = "Mesure", Model = "m", Formula = formula, Format = format };

    private static async Task<Dictionary<string, object?>> ByRegion(string formula)
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("calc_1")], Calculations: [Measure(formula)]));
        return result.Rows.ToDictionary(r => (string)r[0]!, r => r[1]);
    }

    // --- Computing

    [Fact]
    public async Task A_formula_combines_the_aggregated_measures_of_each_group()
    {
        var rates = await ByRegion("[Margin] / [Amount]");

        Assert.Equal(90d / 400d, (double)rates["Nord"]!, 10);
        Assert.Equal(0.5, (double)rates["Sud"]!, 10);
    }

    [Fact]
    public async Task A_division_by_zero_gives_an_empty_value()
    {
        Assert.Null((await ByRegion("[Margin] / [Amount]"))["Est"]);
    }

    [Fact]
    public async Task Formulas_follow_operator_precedence_parentheses_numbers_and_negation()
    {
        var nord = (await ByRegion("([Amount] - [Margin]) * 2 + -[Count] / .5"))["Nord"];

        Assert.Equal((400d - 90d) * 2 + -2d / 0.5, (double)nord!, 10);
    }

    [Fact]
    public async Task Formulas_use_each_measure_as_the_model_declares_it()
    {
        var values = await ByRegion("[Amount] / [Count] + [Rate]");

        Assert.Equal(400d / 2 + 90d / 400d, (double)values["Nord"]!, 10);
    }

    [Fact]
    public async Task The_result_column_takes_the_caption_and_format_of_the_measure()
    {
        var result = await Model().ExecuteAsync(new([], [new("calc_1")], Calculations: [Measure("[Margin] / [Amount]", "P1")]));

        var column = Assert.Single(result.Columns);
        Assert.Equal(("Mesure", "P1"), (column.Caption, column.Format));
    }

    [Fact]
    public async Task Formulas_are_computed_by_the_provider_in_the_same_query()
    {
        var source = new RecordingQueryable<Row>(Rows);

        await Model(source).ExecuteAsync(new([new("Region")], [new("Amount"), new("calc_1")], Calculations: [Measure("[Margin] / [Amount]")]));

        Assert.Contains("Sum(r => r.Margin)", Assert.Single(source.Log.Executed));
    }

    [Fact]
    public void A_visual_query_carries_only_the_calculated_measures_it_shows()
    {
        var shown = Measure("[Margin] / [Amount]");
        var other = shown with { Name = "calc_2" };
        var visual = new VisualDefinition { Model = "m", Values = [new("Amount"), new("calc_1")] };

        Assert.Equal([shown], visual.ToQuery([shown, other]).Calculations!);
        Assert.Null((visual with { Values = [new("Amount")] }).ToQuery([shown, other]).Calculations);
    }

    // --- Checking

    [Theory]
    [InlineData("", "empty")]
    [InlineData("[Margin] /", "position 11")]
    [InlineData("([Margin] / [Amount]", "parenthesis")]
    [InlineData("[Margin / [Amount]", "unknown measure")]
    [InlineData("[Margin] / [Amount", "bracket")]
    [InlineData("[Margin] % [Amount]", "%")]
    [InlineData("[Margin] * 0,5", "decimal")]
    [InlineData("[Revenue] / [Amount]", "unknown measure: “Revenue”")]
    [InlineData("[Region] / [Amount]", "“Region” is not a measure")]
    public void Invalid_formulas_are_explained(string formula, string expected)
    {
        var problem = MeasureFormula.Validate(formula, Model().Metadata);

        Assert.NotNull(problem);
        Assert.Contains(expected, problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_formulas_have_no_problem()
    {
        Assert.Null(MeasureFormula.Validate("([Margin] - [Count]) / [Amount] * 100", Model().Metadata));
    }

    // --- What the user reads and types

    [Fact]
    public void The_user_reads_and_types_captions_while_the_report_keeps_field_names()
    {
        var metadata = Model().Metadata;

        Assert.Equal("[Marge] / [Chiffre d'affaires]", MeasureFormula.ToDisplay("[Margin] / [Amount]", metadata));
        Assert.Equal("[Margin] / [Amount]", MeasureFormula.FromDisplay("[marge] / [Chiffre d'affaires]", metadata, out var error));
        Assert.Null(error);
        Assert.Equal("[Margin]*2", MeasureFormula.FromDisplay("[Margin]*2", metadata, out _)); // names are accepted too
    }

    [Fact]
    public void Typed_formulas_that_cannot_be_resolved_explain_why()
    {
        var ambiguous = SemanticModel.For(Rows, "m").Measure(r => r.Amount, "CA").Measure(r => r.Margin, "CA", name: "Other").Metadata;

        Assert.Null(MeasureFormula.FromDisplay("[CA] * 2", ambiguous, out var error));
        Assert.Contains("Several measures", error);
        Assert.Null(MeasureFormula.FromDisplay("[Ventes]", Model().Metadata, out error));
        Assert.Contains("Unknown measure", error);
    }

    [Fact]
    public void Brackets_inside_a_caption_are_doubled()
    {
        var metadata = SemanticModel.For(Rows, "m").Measure(r => r.Amount, "CA [HT]").Metadata;

        Assert.Equal("[CA [HT]]]", MeasureFormula.ToDisplay("[Amount]", metadata));
        Assert.Equal("[Amount]", MeasureFormula.FromDisplay("[CA [HT]]]", metadata, out _));
    }

    // --- Saving and opening

    [Fact]
    public void Reports_save_and_restore_their_calculated_measures()
    {
        var report = new ReportDefinition { Measures = [Measure("[Margin] / [Amount]", "P1")] };

        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(report.Measures, restored.Measures);
    }

    [Fact]
    public void Reports_without_calculated_measures_are_saved_as_before()
    {
        var json = ReportSerializer.Serialize(new ReportDefinition { Title = "R" });

        Assert.DoesNotContain("measures", json);
        Assert.Empty(ReportSerializer.Deserialize(json).Measures);
    }

    [Fact]
    public void Opening_a_report_checks_its_calculated_measures()
    {
        var report = new ReportDefinition
        {
            Visuals = [new VisualDefinition { Title = "V", Model = "m", Mark = MarkType.Kpi, Values = [new("calc_1"), new("calc_2")] }],
            Measures = [Measure("[Margin] / [Revenue]"), Measure("[Amount]") with { Name = "calc_2", Model = "gone" }],
        };

        var problems = ReportValidator.Validate(report, [Model().Metadata]);

        Assert.Collection(problems,
            p => Assert.Contains("“V”: field 'calc_2' is not in model 'm'", p), // its measure belongs to another model
            p => Assert.Contains("Calculated measure “Mesure”: Unknown measure: “Revenue”", p),
            p => Assert.Contains("unknown model 'gone'", p));
    }

    // --- Sharing

    [Fact]
    public void The_measure_context_notifies_every_change()
    {
        var context = new MeasureContext();
        var changes = 0;
        context.Changed += (_, _) => changes++;

        context.Set(Measure("[Margin]"));
        context.Set(Measure("[Amount]"));
        context.Remove("calc_1");
        context.Load([Measure("[Margin]")]);

        Assert.Equal(4, changes);
        Assert.Equal("[Margin]", Assert.Single(context.Measures).Formula);
        Assert.True(MeasureContext.ToField(context.Measures[0]).IsCalculated);
        Assert.Equal([context.Measures[0]], context.UsedBy(new VisualDefinition { Model = "m", Values = [new("calc_1")] }));
    }
}
