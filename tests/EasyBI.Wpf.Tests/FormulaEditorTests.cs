using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>What the calculated measure editor offers to type a formula: catalog, operators, insertion, suggestions, help.</summary>
public class FormulaEditorTests
{
    private sealed record Sale(string Region, bool Online, DateTime Date, decimal Price, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("Nord", true, new(2024, 1, 5), 10m, 100m),
        new("Sud", false, new(2024, 2, 1), 20m, 200m),
        new("Nord", false, new(2024, 2, 1), 5m, 50m),
    ];

    private static CalculatedMeasureEditor Editor(string formula = "")
    {
        var model = SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Online, "En ligne")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Price, "Prix")
            .Measure(s => s.Amount, "Chiffre d'affaires")
            .Ratio(s => s.Amount, s => 1, "Panier moyen", "Basket");
        var context = EasyBIContext.Create().AddModel(model);
        return new CalculatedMeasureEditor(context, context.Models[0]) { Caption = "Mesure", Formula = formula };
    }

    // Suggestions for a formula typed up to "|"
    private static IReadOnlyList<string> Suggested(CalculatedMeasureEditor editor, string typed, bool all = false)
    {
        editor.Formula = typed.Replace("|", "");
        editor.Suggest(typed.IndexOf('|'), all);
        return editor.Suggestions.Select(s => s.Caption).ToArray();
    }

    [Fact]
    public async Task Typing_the_name_and_the_formula_throws_no_exception()
    {
        // A debugger slows down, or stops, at every exception thrown, even caught: once per key typed
        var thread = Environment.CurrentManagedThreadId;
        var thrown = new List<string>();
        void OnThrown(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread) thrown.Add(e.Exception.GetType().Name + ": " + e.Exception.Message);
        }
        var editor = Editor();
        editor.Caption = "";
        AppDomain.CurrentDomain.FirstChanceException += OnThrown;
        try
        {
            const string caption = "Ventes du Nord";
            for (var length = 1; length <= caption.Length; length++) editor.Caption = caption[..length];
            const string formula = "SUM([Chiffre d'affaires]; [Région] = \"Nord\") / IF([Prix] > 0,5; 1; 2) + TOTAL(";
            for (var length = 1; length <= formula.Length; length++)
            {
                editor.Formula = formula[..length];
                editor.CaretIndex = length;
                editor.Suggest(length);
                await editor.SuggestionsTask;
                await editor.PreviewTask;
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnThrown;
        }
        Assert.Empty(thrown);
    }

    // --- Catalog and operators

    [Fact]
    public void Functions_measures_and_dimensions_each_have_their_tab_filtered_by_the_search()
    {
        var editor = Editor();
        Assert.Equal(["Functions", "Measures", "Dimensions"], editor.Tabs.Select(t => t.Header));
        Assert.Equal(["SUM", "AVERAGE", "MIN", "MAX", "COUNT", "DISTINCTCOUNT", "IF"], editor.Tabs[0].Items.Select(i => i.Caption));
        Assert.Equal(["Chiffre d'affaires", "Panier moyen"], editor.Tabs[1].Items.Select(i => i.Caption));
        Assert.Equal("Sum", editor.Tabs[0].Items[0].Summary);

        // The open tab has no result: the first one with results opens
        editor.Search = "rég";
        Assert.Equal(["Région"], editor.Tabs[2].Items.Select(i => i.Caption));
        Assert.Empty(editor.Tabs[0].Items);
        Assert.Same(editor.Tabs[2], editor.SelectedTab);
    }

    [Fact]
    public void Operators_are_grouped_by_calculation_comparison_and_logic()
    {
        var groups = Editor().OperatorGroups;

        Assert.Equal(["CALCULATION", "COMPARISON", "LOGIC"], groups.Select(g => g.Title));
        Assert.Equal(["+", "−", "( )", "×", "÷"], groups[0].Items.Select(i => i.Caption));
        Assert.Equal(["=", "≠", "<", "≤", ">", "≥"], groups[1].Items.Select(i => i.Caption));
        Assert.Equal(["AND", "OR", "NOT"], groups[2].Items.Select(i => i.Caption));
    }

    [Fact]
    public void Every_item_explains_itself()
    {
        var editor = Editor();
        var operators = editor.OperatorGroups.SelectMany(g => g.Items).ToArray();

        Assert.All(editor.Catalog.Concat(operators), item => Assert.False(string.IsNullOrWhiteSpace(item.Title)));
        Assert.All(editor.Catalog, item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
        var region = editor.Catalog.Single(i => i.Caption == "Région");
        Assert.Equal(("[Région]", "DISTINCTCOUNT([Région])"), (region.Text, region.Example));
        Assert.Contains("[Région]", region.Description);
        Assert.Equal("Divide", operators.Single(o => o.Text == " / ").Title);
    }

    [Fact]
    public void Inserting_puts_the_item_at_the_caret_and_wraps_the_selection_in_a_function()
    {
        var editor = Editor("[Chiffre d'affaires] * 2");
        var sum = editor.Catalog.Single(i => i.Caption == "SUM");

        var caret = editor.Insert(sum, 0, 20);
        Assert.Equal(("SUM([Chiffre d'affaires]) * 2", 25), (editor.Formula, caret));

        caret = editor.Insert(editor.OperatorGroups[0].Items.Single(o => o.Text == " + "), editor.Formula.Length, 0);
        caret = editor.Insert(editor.Catalog.Single(i => i.Caption == "COUNT"), caret, 0);
        Assert.Equal(("SUM([Chiffre d'affaires]) * 2 + COUNT()", editor.Formula.Length - 1), (editor.Formula, caret));
        Assert.Equal("COUNT", editor.CurrentCall?.Function.Name);
    }

    // --- Smart suggestions

    [Fact]
    public void Only_what_can_be_written_where_the_caret_is_is_suggested()
    {
        var editor = Editor();

        // A value of the group: functions and measures, no dimension
        Assert.Equal(["SUM", "AVERAGE", "MIN", "MAX", "COUNT", "DISTINCTCOUNT", "IF", "Chiffre d'affaires", "Panier moyen"], Suggested(editor, "|", all: true));
        // The value of a sum: measures read on rows and number dimensions, not a ratio
        Assert.Equal(["Chiffre d'affaires", "Prix", "IF"], Suggested(editor, "SUM(|"));
        // A condition: dimensions first
        Assert.Equal(["Région", "En ligne", "Date", "Prix", "Chiffre d'affaires", "NOT"], Suggested(editor, "SUM([Chiffre d'affaires]; |"));
        Assert.Equal(["Région", "En ligne", "Date", "Prix", "Chiffre d'affaires"], Suggested(editor, "DISTINCTCOUNT(|"));
        // After a value in a condition: AND, OR
        Assert.Equal(["AND"], Suggested(editor, "COUNT([En ligne] a|"));
        // A field being typed after "[": the allowed fields containing it, those starting with it first
        Assert.Equal(["Région", "Chiffre d'affaires"], Suggested(editor, "COUNT([ré|"));
        Assert.Equal(["Chiffre d'affaires"], Suggested(editor, "SUM([ré|"));
    }

    [Fact]
    public async Task After_a_comparison_the_values_of_the_compared_field_are_suggested()
    {
        var editor = Editor();

        Suggested(editor, "SUM([Chiffre d'affaires]; [Région] = |");
        await editor.SuggestionsTask;
        Assert.Equal(["\"Nord\"", "\"Sud\""], editor.Suggestions.Select(s => s.Text));

        Assert.Equal(["Sud"], Suggested(editor, "COUNT([Région] <> \"S|"));
        editor.Accept(editor.Suggestions[0]);
        Assert.Equal("COUNT([Région] <> \"Sud\"", editor.Formula);

        Suggested(editor, "COUNT([En ligne] = |");
        await editor.SuggestionsTask;
        Assert.Equal(["FALSE", "TRUE"], editor.Suggestions.Select(s => s.Text));

        Suggested(editor, "COUNT([Date] >= |");
        await editor.SuggestionsTask;
        Assert.Equal("\"2024-01-05\"", editor.Suggestions[0].Text);
    }

    [Fact]
    public void Accepting_replaces_what_is_typed_and_spaces_words()
    {
        var editor = Editor();

        Suggested(editor, "COUNT([ré|");
        editor.Accept(editor.Suggestions[0]);
        Assert.Equal("COUNT([Région]", editor.Formula);

        Suggested(editor, "COUNT([En ligne] o|");
        editor.Accept(editor.Suggestions[0]);
        Assert.Equal("COUNT([En ligne] OR ", editor.Formula);

        // A caption typed without brackets is completed as a field
        Assert.Equal(["Chiffre d'affaires"], Suggested(editor, "chiff|"));
        var caret = editor.Accept(editor.Suggestions[0]);
        Assert.Equal(("[Chiffre d'affaires]", 20), (editor.Formula, caret));
    }

    [Fact]
    public void Nothing_is_suggested_for_a_word_typed_in_full_or_after_an_operator_unless_asked()
    {
        var editor = Editor();

        Assert.Empty(Suggested(editor, "SUM|"));
        Assert.Empty(Suggested(editor, "[Chiffre d'affaires] + |"));
        Assert.Null(editor.Completion);
        Assert.NotEmpty(Suggested(editor, "[Chiffre d'affaires] + |", all: true));
    }

    [Fact]
    public void The_problem_is_located_in_the_formula_and_an_empty_formula_has_none()
    {
        var editor = Editor("[Chiffre d'affaires] + [Région]");
        Assert.Equal("[Région]", editor.Formula.Substring(editor.Problem!.Start, editor.Problem.Length));
        Assert.StartsWith("[Région] is a dimension", editor.Error);

        editor.Formula = "  ";
        Assert.Null(editor.Problem);
        Assert.Equal("The formula is empty.", editor.Error);
    }
}
