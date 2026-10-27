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

    [Theory]
    [InlineData("SUM([CA]; [Région] = \"Nord\") / IF([CA] > 0,5; 1; 2)")]
    [InlineData("TOTAL([Inconnu]) + [Région] % (1.2.3 - NOT [CA]")]
    [InlineData("DISTINCTCOUNT([Région] = \"Nord\"; [CA] > \"x\") + MIN(SUM([CA]))")]
    public void Checking_a_formula_as_it_is_typed_throws_no_exception(string formula)
    {
        // A debugger slows down, or stops, at every exception thrown, even caught: once per key typed
        var thread = Environment.CurrentManagedThreadId;
        var thrown = new List<string>();
        void OnThrown(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread) thrown.Add(e.Exception.Message);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnThrown;
        try
        {
            for (var length = 0; length <= formula.Length; length++)
                MeasureFormula.Check(formula[..length], Metadata, out _);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnThrown;
        }
        Assert.Empty(thrown);
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

    [Theory]
    [InlineData("|", FormulaSlot.Value)]
    [InlineData("[CA] / |", FormulaSlot.Value)]
    [InlineData("SUM(|", FormulaSlot.RowValue)]
    [InlineData("SUM([CA] * |", FormulaSlot.RowValue)]
    [InlineData("SUM([CA]; |", FormulaSlot.Condition)]
    [InlineData("SUM([CA]; [Région] = \"Nord\" AND |", FormulaSlot.Condition)]
    [InlineData("SUM([CA]; ([Prix] > 1 OR |", FormulaSlot.Condition)]
    [InlineData("COUNT(|", FormulaSlot.Condition)]
    [InlineData("DISTINCTCOUNT(|", FormulaSlot.DistinctField)]
    [InlineData("DISTINCTCOUNT([Client]; |", FormulaSlot.Condition)]
    [InlineData("IF(|", FormulaSlot.Condition)]
    [InlineData("IF([CA] > 1; |", FormulaSlot.Value)]
    [InlineData("SUM(IF([Région] = \"Nord\"; |", FormulaSlot.RowValue)]
    [InlineData("[CA] |", FormulaSlot.AfterOperand)]
    [InlineData("SUM([CA]; [Région] = \"Nord\" |", FormulaSlot.AfterOperand)]
    [InlineData("SUM([CA]) |", FormulaSlot.AfterOperand)]
    public void The_place_of_the_caret_tells_what_can_be_written(string typed, FormulaSlot slot)
    {
        Assert.Equal(slot, FormulaEditing.ContextAt(typed.Replace("|", ""), typed.IndexOf('|')).Slot);
    }

    [Fact]
    public void After_a_comparison_of_a_field_its_values_are_expected_and_a_typed_value_is_replaced()
    {
        var context = FormulaEditing.ContextAt("SUM([CA]; [Région] = ", 21);
        Assert.Equal((FormulaSlot.ComparedValue, "Région", true, 21, 0, ""), (context.Slot, context.ComparedField, context.InCondition, context.Start, context.Length, context.Prefix));

        context = FormulaEditing.ContextAt("SUM([CA]; [Région] <> \"No", 26);
        Assert.Equal((FormulaSlot.ComparedValue, 22, 3, "No"), (context.Slot, context.Start, context.Length, context.Prefix));

        context = FormulaEditing.ContextAt("SUM([CA]; [Région] = \"Nord\"", 27);
        Assert.Equal(FormulaSlot.AfterOperand, context.Slot);
    }

    [Fact]
    public void A_word_or_a_field_being_typed_is_what_a_suggestion_replaces()
    {
        var context = FormulaEditing.ContextAt("SUM([CA]; [Ré", 13);
        Assert.Equal((FormulaSlot.Condition, true, 10, 3, "Ré"), (context.Slot, context.IsField, context.Start, context.Length, context.Prefix));

        context = FormulaEditing.ContextAt("SUM([CA]; [Région] = \"Nord\" a", 29);
        Assert.Equal((FormulaSlot.AfterOperand, true, "a"), (context.Slot, context.InCondition, context.Prefix));
    }
}
