using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>What the calculated measure editor offers to type a formula: catalog, insertion, suggestions, help.</summary>
public class FormulaEditorTests
{
    private sealed record Sale(string Region, bool Online, DateTime Date, decimal Amount);

    private static readonly Sale[] Sales = [new("Nord", true, new(2024, 1, 5), 100m), new("Sud", false, new(2024, 2, 1), 200m), new("Nord", false, new(2024, 2, 1), 50m)];

    private static CalculatedMeasureEditor Editor(string formula = "")
    {
        var model = SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Online, "En ligne")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "Chiffre d'affaires");
        var context = EasyBIContext.Create().AddModel(model);
        return new CalculatedMeasureEditor(context, context.Models[0]) { Caption = "Mesure", Formula = formula };
    }

    [Fact]
    public void The_catalog_lists_functions_measures_and_dimensions_and_filters_them()
    {
        var editor = Editor();

        Assert.Equal(["SUM", "AVERAGE", "MIN", "MAX", "COUNT", "DISTINCTCOUNT", "IF", "Chiffre d'affaires", "Région", "En ligne", "Date"],
            editor.Catalog.Select(i => i.Caption));
        Assert.Equal(["FUNCTIONS", "MEASURES", "DIMENSIONS"], editor.Catalog.Select(i => i.Group).Distinct());

        editor.Search = "rég";
        Assert.Equal(["Région"], editor.Catalog.Select(i => i.Caption));
    }

    [Fact]
    public void Every_item_explains_itself()
    {
        var editor = Editor();

        Assert.All(editor.Catalog.Concat(editor.Operators), item => Assert.False(string.IsNullOrWhiteSpace(item.Title)));
        Assert.All(editor.Catalog.Where(i => i.Kind != FormulaItemKind.Operator), item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
        var region = editor.Catalog.Single(i => i.Caption == "Région");
        Assert.Equal(("[Région]", "DISTINCTCOUNT([Région])"), (region.Text, region.Example));
        Assert.Contains("[Région]", region.Description);
        Assert.Equal("Divide", editor.Operators.Single(o => o.Text == " / ").Title);
    }

    [Fact]
    public void Inserting_puts_the_item_at_the_caret_and_wraps_the_selection_in_a_function()
    {
        var editor = Editor("[Chiffre d'affaires] * 2");
        var sum = editor.Catalog.Single(i => i.Caption == "SUM");

        var caret = editor.Insert(sum, 0, 20);
        Assert.Equal(("SUM([Chiffre d'affaires]) * 2", 25), (editor.Formula, caret));

        caret = editor.Insert(editor.Operators.Single(o => o.Text == " + "), editor.Formula.Length, 0);
        caret = editor.Insert(editor.Catalog.Single(i => i.Caption == "COUNT"), caret, 0);
        Assert.Equal(("SUM([Chiffre d'affaires]) * 2 + COUNT()", editor.Formula.Length - 1), (editor.Formula, caret));
        Assert.Equal("COUNT", editor.CurrentCall?.Function.Name);
    }

    [Fact]
    public async Task A_selected_dimension_offers_its_values_written_as_formulas_compare_them()
    {
        var editor = Editor();

        editor.SelectedItem = editor.Catalog.Single(i => i.Caption == "Région");
        await editor.ValuesTask;
        Assert.Equal(["\"Nord\"", "\"Sud\""], editor.Values.Select(v => v.Text));

        editor.SelectedItem = editor.Catalog.Single(i => i.Caption == "En ligne");
        await editor.ValuesTask;
        Assert.Equal(["FALSE", "TRUE"], editor.Values.Select(v => v.Text));

        editor.SelectedItem = editor.Catalog.Single(i => i.Caption == "Date");
        await editor.ValuesTask;
        Assert.Equal("\"2024-01-05\"", editor.Values[0].Text);

        editor.SelectedItem = editor.Catalog.Single(i => i.Caption == "SUM");
        Assert.Empty(editor.Values);
    }

    [Fact]
    public void Typing_suggests_fields_after_a_bracket_and_functions_or_words_otherwise()
    {
        var editor = Editor("SUM([ré");
        editor.Suggest(editor.Formula.Length);
        Assert.Equal(["Région", "Chiffre d'affaires"], editor.Suggestions.Select(s => s.Caption)); // starting with it first
        editor.Accept(editor.Suggestions[0]);
        Assert.Equal("SUM([Région]", editor.Formula);

        editor.Formula = "SUM([Chiffre d'affaires]; [En ligne] a";
        editor.Suggest(editor.Formula.Length);
        Assert.Equal(["AVERAGE", "AND"], editor.Suggestions.Select(s => s.Caption));
        editor.Accept(editor.Suggestions[1]);
        Assert.Equal("SUM([Chiffre d'affaires]; [En ligne] AND ", editor.Formula);

        // A caption typed without brackets is completed as a field
        editor.Formula = "chiff";
        editor.Suggest(editor.Formula.Length);
        Assert.Equal(["Chiffre d'affaires"], editor.Suggestions.Select(s => s.Caption));
        var caret = editor.Accept(editor.Suggestions[0]);
        Assert.Equal(("[Chiffre d'affaires]", 20), (editor.Formula, caret));
    }

    [Fact]
    public void Nothing_is_suggested_for_a_word_typed_in_full_or_outside_a_name_unless_asked()
    {
        var editor = Editor("SUM");
        editor.Suggest(3);
        Assert.Empty(editor.Suggestions);

        editor.Formula = "[Chiffre d'affaires] + ";
        editor.Suggest(editor.Formula.Length);
        Assert.Empty(editor.Suggestions);
        Assert.Null(editor.Completion);

        editor.Suggest(editor.Formula.Length, all: true);
        Assert.Equal(11 + FormulaEditing.Keywords.Count, editor.Suggestions.Count);
        editor.Accept(editor.Suggestions.Single(s => s.Caption == "Région"));
        Assert.Equal("[Chiffre d'affaires] + [Région]", editor.Formula);
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
