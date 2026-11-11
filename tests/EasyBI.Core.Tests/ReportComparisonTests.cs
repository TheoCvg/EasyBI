using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Periods of comparison and presentations the end user adds to a report.</summary>
public class ReportComparisonTests
{
    private sealed record Sale(DateTime Date, string Region, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new(new(2022, 1, 10), "Nord", 80m),
        new(new(2023, 1, 10), "Nord", 100m),
        new(new(2024, 1, 10), "Nord", 150m),
        new(new(2024, 2, 10), "Sud", 0m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales.AsQueryable(), "m")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Région")
            .Measure(s => s.Amount, "CA", format: "N0");

    private static readonly ReportComparisonPeriod TwoYearsAgo = new() { Id = "report.twoYears", Caption = "Il y a 2 ans", Count = 2, Unit = TimeUnit.Year };

    private static readonly ReportComparisonOutput Index = new()
    {
        Id = "report.index",
        Caption = "Indice",
        Formula = "[Value] / [Compared] * 100",
        Format = "N1",
    };

    private static double? Num(object? value) => value is null ? null : Convert.ToDouble(value);

    // --- Formulas

    [Theory]
    [InlineData("[Value] / [Compared] * 100", 150d, 100d, 150d)]
    [InlineData("[Value] - [Compared]", 150d, 100d, 50d)]
    [InlineData("-([Compared] - [Value]) / 2", 150d, 100d, 25d)]
    [InlineData("IF([Value] >= [Compared]; 1; 0)", 90d, 100d, 0d)]
    [InlineData("IF([Compared] > 0 AND NOT [Value] = 0; [Value]; -1)", 5d, 3d, 5d)]
    public void A_presentation_computes_its_formula_from_the_value_and_the_compared_one(string formula, double value, double compared, double expected)
    {
        Assert.Null(ComparisonFormula.Validate(formula));

        Assert.Equal(expected, ComparisonFormula.Compile(formula)(value, compared));
    }

    [Theory]
    [InlineData(150d, 0d)]
    [InlineData(150d, null)]
    [InlineData(null, 100d)]
    public void A_missing_value_or_a_division_by_zero_gives_an_empty_value(double? value, double? compared)
    {
        Assert.Null(ComparisonFormula.Compile("[Value] / [Compared]")(value, compared));
    }

    [Fact]
    public void A_condition_on_a_missing_value_gives_an_empty_value()
    {
        Assert.Null(ComparisonFormula.Compile("IF([Compared] > 0; 1; 0)")(1, null));
    }

    [Theory]
    [InlineData("[Amount] / [Compared]", "Amount")]
    [InlineData("SUM([Value])", "SUM")]
    [InlineData("[Value] > [Compared]", "[Value] > [Compared]")]
    [InlineData("\"text\"", "text")]
    [InlineData("IF([Value]; 1; 0)", "[Value]")]
    [InlineData("[Value] /", "")]
    public void A_formula_using_anything_else_than_the_two_values_and_arithmetic_is_refused(string formula, string quoted)
    {
        var problem = ComparisonFormula.Check(formula, out var stored);

        Assert.NotNull(problem);
        Assert.Null(stored);
        Assert.Contains(quoted, problem!.Message);
    }

    [Fact]
    public void An_invalid_formula_gives_empty_values()
    {
        Assert.Null(ComparisonFormula.Compile("SUM([Value])")(1, 2));
    }

    // --- Catalog

    [Fact]
    public void The_entries_of_a_report_come_after_those_of_the_catalog_in_a_copy()
    {
        var catalog = new TimeCatalog();

        var withReport = catalog.With([TwoYearsAgo], [Index]);

        Assert.Equal([TimeIds.PreviousPeriod, TimeIds.LastYear, TimeIds.Offset, TwoYearsAgo.Id], withReport.Periods.Select(p => p.Id));
        Assert.Equal(Index.Id, withReport.Outputs[^1].Id);
        Assert.Null(catalog.Period(TwoYearsAgo.Id));
        Assert.Same(catalog, catalog.With([], null));
        Assert.Equal("Il y a 2 ans", withReport.Badge(new MeasureTime { Period = TwoYearsAgo.Id }));
    }

    [Fact]
    public async Task A_period_of_the_report_is_its_count_of_units_back_and_shown_by_its_name()
    {
        var time = new MeasureTime { Period = TwoYearsAgo.Id, Show = Index.Id };
        var query = new AnalyticQuery([new("Date", DateGrain.Year)], [new("Amount"), new("Amount") { Time = time }]);

        var result = await Model().ExecuteWithTimeAsync(query, new DateOnly(2025, 6, 1), new TimeCatalog().With([TwoYearsAgo], [Index]), CancellationToken.None);

        var byYear = result.Rows.ToDictionary(r => ((DatePeriod)r[0]!).Key, r => Num(r[2]));
        Assert.Equal(187.5, byYear[2024]); // 150 / 80 * 100
        Assert.Null(byYear[2023]); // no data in 2021
        Assert.Equal("CA Indice vs Il y a 2 ans", result.Columns[2].Caption);
        Assert.Equal("N1", result.Columns[2].Format);
        Assert.Null(result.Columns[2].Change);
    }

    [Fact]
    public async Task A_presentation_showing_a_change_is_colored_by_the_favorable_direction()
    {
        var change = Index with { Formula = "[Value] - [Compared]", IsChange = true };
        var query = new AnalyticQuery([], [new("Amount") { Time = new MeasureTime { Period = TimeIds.LastYear, Show = change.Id } }],
            [PeriodFilters.Between("Date", new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31))]);

        var result = await Model().ExecuteWithTimeAsync(query, new DateOnly(2025, 6, 1), new TimeCatalog().With(null, [change]), CancellationToken.None);

        Assert.Equal(50d, Num(Assert.Single(result.Rows)[0]));
        Assert.Equal(Favorable.Higher, result.Columns[0].Change);
    }

    [Fact]
    public void A_caption_with_braces_is_shown_as_it_is()
    {
        var output = TimeCatalog.ToOutput(Index with { Caption = "{x}" });

        Assert.Equal("CA {x} vs N-1", string.Format(output.ColumnCaption!(), "CA", "N-1"));
    }

    // --- Visuals

    private static readonly VisualDefinition ByYear = new()
    {
        Model = "m",
        Type = VisualType.Table,
        Axis = [new("Date", DateGrain.Year)],
        Values = [new("Amount") { Time = new MeasureTime { Period = TwoYearsAgo.Id, Show = Index.Id } }],
    };

    private static ComparisonContext Comparisons()
    {
        var comparisons = new ComparisonContext();
        comparisons.Load([TwoYearsAgo, TwoYearsAgo with { Id = "report.unused" }], [Index]);
        return comparisons;
    }

    [Fact]
    public void A_visual_sends_its_model_the_entries_of_the_report_it_uses()
    {
        var request = ByYear.ToDataRequest(Model().Metadata, comparisons: Comparisons());

        Assert.Equal([TwoYearsAgo], request.ComparisonPeriods);
        Assert.Equal([Index], request.ComparisonOutputs);
        Assert.Empty((ByYear with { Values = [new("Amount")] }).ToDataRequest(comparisons: Comparisons()).ComparisonPeriods);
    }

    [Fact]
    public async Task A_remote_model_computes_the_entries_of_the_report_it_receives()
    {
        var request = DataSerializer.DeserializeRequest(DataSerializer.Serialize(ByYear.ToDataRequest(Model().Metadata, comparisons: Comparisons())
            with { Today = new DateTime(2025, 6, 1) }));

        var data = await Model().GetDataAsync(request);

        Assert.Equal(187.5, Num(data.Result.Rows.Single(r => ((DatePeriod)r[0]!).Key == 2024)[1]));
    }

    [Fact]
    public async Task A_matrix_computes_the_entries_of_the_report()
    {
        var data = await Model().GetDataAsync(ByYear.ToDataRequest(Model().Metadata, comparisons: Comparisons()) with
        {
            Visual = ByYear with { Type = VisualType.Matrix },
            Today = new DateTime(2025, 6, 1),
        });

        Assert.Contains(187.5, data.Result.Rows.Select(r => Num(r[^1])));
    }

    [Fact]
    public void The_entries_of_a_report_are_saved_with_it_and_older_reports_have_none()
    {
        var report = new ReportDefinition { Visuals = [ByYear], ComparisonPeriods = [TwoYearsAgo], ComparisonOutputs = [Index] };

        var read = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal([TwoYearsAgo], read.ComparisonPeriods);
        Assert.Equal([Index], read.ComparisonOutputs);
        var older = ReportSerializer.Deserialize("""{ "schema": "easybi/v1", "title": "R", "visuals": [] }""");
        Assert.Empty(older.ComparisonPeriods);
        Assert.Empty(older.ComparisonOutputs);
    }

    [Fact]
    public void Copied_visuals_bring_the_entries_of_the_report_they_use()
    {
        var json = VisualTransfer.Copy([ByYear], new MeasureContext(), comparisons: Comparisons());
        var target = new ComparisonContext();
        target.Set(Index with { Caption = "Indice du rapport cible" });

        VisualTransfer.Paste(json, new MeasureContext(), _ => true, comparisons: target);

        Assert.Equal([TwoYearsAgo], target.Periods);
        // The same entry: the target keeps its own
        Assert.Equal("Indice du rapport cible", Assert.Single(target.Outputs).Caption);
    }

    [Fact]
    public void Changing_the_entries_of_a_report_is_announced()
    {
        var comparisons = new ComparisonContext();
        var changes = 0;
        comparisons.Changed += (_, _) => changes++;

        comparisons.Set(TwoYearsAgo);
        comparisons.Set(TwoYearsAgo with { Count = 3 });
        comparisons.Set(Index);
        comparisons.Remove(TwoYearsAgo.Id);
        comparisons.Remove("report.unknown");

        Assert.Equal(4, changes);
        Assert.Empty(comparisons.Periods);
        Assert.StartsWith(TimeIds.ReportPrefix, ComparisonContext.NewId());
    }
}

/// <summary>The formulas of comparisons in the language of EasyBI.</summary>
[Collection(CultureCollection.Name)]
public class ComparisonFormulaCultureTests
{
    [Fact]
    public void The_values_are_typed_and_shown_in_the_language_of_EasyBI()
    {
        using var french = TestCulture.Use("fr-FR");

        Assert.Null(ComparisonFormula.Check("[valeur] / [Comparée] * 100", out var stored));
        Assert.Equal("[Value] / [Compared] * 100", stored);
        Assert.Equal("[Valeur] / [Comparée] * 100", ComparisonFormula.ToDisplay(stored!));
        Assert.Contains("[Valeur]", ComparisonFormula.Check("[CA]", out _)!.Message);
    }
}
