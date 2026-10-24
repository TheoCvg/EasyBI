using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Help to type formulas: problems located in the text, coloring, the function at the caret, completion.</summary>
public class FormulaEditingTests
{
    private sealed record Row(string Region, decimal Amount);

    private static readonly EasyBI.Abstractions.ModelMetadata Metadata =
        SemanticModel.For(Array.Empty<Row>(), "m").Dimension(r => r.Region, "Région").Measure(r => r.Amount, "CA").Metadata;

    private static string Located(string text)
    {
        var problem = MeasureFormula.Check(text, Metadata, out var stored);
        Assert.Null(stored);
        return text.Substring(problem!.Start, problem.Length);
    }

    [Fact]
    public void A_problem_tells_where_it_is_written()
    {
        Assert.Equal("[Région]", Located("[CA] + [Région]"));
        Assert.Equal("[Ventes]", Located("SUM([Ventes])"));
        Assert.Equal("TOTAL", Located("TOTAL([CA])"));
        Assert.Equal("%", Located("[CA] % 2"));
        Assert.Equal("[Région] > \"Nord\"", Located("SUM([CA]; [Région] > \"Nord\")"));
        Assert.Equal("[CA", Located("[CA"));
        Assert.Equal("", Located("[CA] +"));
    }

    [Fact]
    public void The_message_without_a_place_still_comes_with_the_text()
    {
        Assert.Null(MeasureFormula.FromDisplay("[CA] +", Metadata, out string? error));
        Assert.Contains("position 7", error);
    }

    [Fact]
    public void A_formula_being_typed_is_split_for_coloring()
    {
        const string text = "SUM([CA]; [Région] = \"Nord\" AND x) >= 2.5 % [Ré";

        var tokens = FormulaEditing.Tokenize(text).Select(t => (t.Kind, text.Substring(t.Start, t.Length)));

        Assert.Equal(
        [
            (FormulaTokenKind.Function, "SUM"), (FormulaTokenKind.Punctuation, "("), (FormulaTokenKind.Field, "[CA]"),
            (FormulaTokenKind.Punctuation, ";"), (FormulaTokenKind.Field, "[Région]"), (FormulaTokenKind.Operator, "="),
            (FormulaTokenKind.Text, "\"Nord\""), (FormulaTokenKind.Keyword, "AND"), (FormulaTokenKind.Unknown, "x"),
            (FormulaTokenKind.Punctuation, ")"), (FormulaTokenKind.Operator, ">="), (FormulaTokenKind.Number, "2.5"),
            (FormulaTokenKind.Unknown, "%"), (FormulaTokenKind.Field, "[Ré"),
        ], tokens);
    }

    [Theory]
    [InlineData("SUM(|", "SUM", 0)]
    [InlineData("SUM([CA]; |", "SUM", 1)]
    [InlineData("sum([CA]; IF([CA] > 1; 2; |", "IF", 2)]
    [InlineData("SUM(IF([CA] > 1; 2; 3); |)", "SUM", 1)]
    [InlineData("COUNT([Région] = \"a;b(\"|", "COUNT", 0)]
    public void The_function_at_the_caret_and_its_argument_are_known(string typed, string function, int argument)
    {
        var call = FormulaEditing.CallAt(typed.Replace("|", ""), typed.IndexOf('|'));

        Assert.Equal((function, argument), (call?.Function.Name, call?.Argument));
    }

    [Theory]
    [InlineData("[CA] + |")]
    [InlineData("(|")]
    [InlineData("SUM([CA])|")]
    public void Outside_a_function_there_is_no_call(string typed)
    {
        Assert.Null(FormulaEditing.CallAt(typed.Replace("|", ""), typed.IndexOf('|')));
    }

    [Theory]
    [InlineData("SUM([Ré|", 4, 3, "Ré", true)]
    [InlineData("SUM([Ré|gion])", 4, 8, "Ré", true)]
    [InlineData("[|", 0, 1, "", true)]
    [InlineData("[CA] + su|", 7, 2, "su", false)]
    [InlineData("di|stinct", 0, 8, "di", false)]
    public void What_is_being_typed_can_be_completed(string typed, int start, int length, string prefix, bool isField)
    {
        var completion = FormulaEditing.CompletionAt(typed.Replace("|", ""), typed.IndexOf('|'));

        Assert.Equal(new FormulaCompletion(start, length, prefix, isField), completion);
    }

    [Theory]
    [InlineData("[CA]|")]
    [InlineData("[CA] + |")]
    [InlineData("\"No|")]
    [InlineData("12|")]
    [InlineData("|")]
    public void Nothing_is_completed_outside_a_name(string typed)
    {
        Assert.Null(FormulaEditing.CompletionAt(typed.Replace("|", ""), typed.IndexOf('|')));
    }
}
