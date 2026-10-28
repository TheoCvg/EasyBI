using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>What a <see cref="FormulaItem"/> inserts into a formula.</summary>
public enum FormulaItemKind { Function, Measure, Dimension, Keyword, Operator, Value, Variable }

/// <summary>
/// Something the calculated measure editor inserts into a formula (a function, a field, an operator, a value of a
/// dimension), with what its help tooltip shows: a title, a description and an example.
/// </summary>
/// <param name="Kind">What it is.</param>
/// <param name="Caption">What the lists and buttons show.</param>
/// <param name="Text">
/// What is inserted. A text ending with <c>()</c> goes around the selection, the caret between the parentheses.
/// </param>
/// <param name="Title">The first line of its help: its syntax or its name.</param>
/// <param name="Description">What it does.</param>
/// <param name="Example">A short formula using it.</param>
public sealed record FormulaItem(FormulaItemKind Kind, string Caption, string Text, string Title, string? Description = null, string? Example = null)
{
    /// <summary>The dimension or measure, for a field.</summary>
    public FieldDescriptor? Field { get; init; }

    /// <summary>The symbol shown before the caption in the lists.</summary>
    public string Glyph => Kind switch
    {
        FormulaItemKind.Function => "ƒ",
        FormulaItemKind.Measure => IsReportMeasure ? "ƒx" : "Σ",
        FormulaItemKind.Dimension => Field?.DataType switch { DataType.Date => "◷", DataType.Number => "#", DataType.Boolean => "◑", _ => "Aa" },
        FormulaItemKind.Variable => "@",
        FormulaItemKind.Keyword => "≡",
        _ => "",
    };

    /// <summary>A calculated measure of the report, computed for each group.</summary>
    public bool IsReportMeasure { get; init; }

    /// <summary>What it does in a few words, shown next to its caption in the lists (functions).</summary>
    public string? Summary { get; init; }

    public static FormulaItem For(FormulaFunction function) =>
        new(FormulaItemKind.Function, function.Name, function.Name + "()", function.Usage, function.Description) { Summary = function.Summary };

    /// <summary>
    /// A parameter or a computed value of the report, inserted as <c>[@Name]</c>; its value replaces it when the formula
    /// is computed.
    /// </summary>
    public static FormulaItem ForVariable(ReportVariable variable)
    {
        var reference = VariableReferences.Reference(variable.Name);
        if (VariableFormula.Definition(variable).Kind == VariableKind.Computed)
            return new(FormulaItemKind.Variable, variable.Name, reference, reference, Strings.FormulaComputedVariableHelp) { Summary = "ƒ" };
        var value = VariableContext.Display(variable);
        return new(FormulaItemKind.Variable, variable.Name, reference, reference, Strings.Format(Strings.FormulaVariableHelp, value)) { Summary = value };
    }

    /// <summary>The words of the variables' formulas, with an example each.</summary>
    public static IReadOnlyList<FormulaItem> VariableKeywords() =>
    [
        Keyword("SELECT", "SELECT [A]"),
        Keyword("TOP", "SELECT TOP 10 [A]"),
        Keyword("WHERE", "SELECT [A] WHERE [B] = \"x\" AND [C] > 1000"),
        Keyword("ORDER BY", "SELECT [A] ORDER BY [C] DESC"),
        Keyword("DESC", "ORDER BY [C] DESC"),
        Keyword("ASC", "ORDER BY [C] ASC"),
        Keyword("IN", "SELECT [A] IN (\"x\"; \"y\")"),
        Keyword("BETWEEN", "10 BETWEEN 3 AND 25"),
        Keyword("STEP", "0.2 BETWEEN 0 AND 1 STEP 0.05"),
        Keyword("BY", "MAX([C] BY [A])"),
    ];

    private static FormulaItem Keyword(string keyword, string example) => ForKeyword(keyword) with { Example = example };

    public static FormulaItem ForKeyword(string keyword) =>
        new(FormulaItemKind.Keyword, keyword, keyword is "TRUE" or "FALSE" ? keyword : $" {keyword} ",
            keyword, Strings.Named("FormulaKeyword" + keyword.Replace(" ", "")));

    /// <summary>A measure or a dimension of the model, inserted by its caption.</summary>
    public static FormulaItem For(FieldDescriptor field)
    {
        var reference = "[" + field.Caption.Replace("]", "]]") + "]";
        var description = field.Kind == FieldKind.Dimension
            ? Strings.Format(Strings.FormulaDimensionHelp, reference)
            : field.IsCalculated ? Strings.FormulaCalculatedMeasureHelp : Strings.FormulaMeasureHelp;
        var example = field.Kind == FieldKind.Dimension
            ? field.DataType == DataType.Text ? $"DISTINCTCOUNT({reference})" : null
            : field.IsCalculated ? null : $"SUM({reference})";
        return new(field.Kind == FieldKind.Dimension ? FormulaItemKind.Dimension : FormulaItemKind.Measure,
            field.Caption, reference, reference, description, example) { Field = field };
    }

    /// <summary>A value of a dimension, written as the formulas compare it ("North", 12, TRUE, "2024-12-31").</summary>
    public static FormulaItem ForValue(object value)
    {
        var text = value switch
        {
            bool b => b ? "TRUE" : "FALSE",
            DateTime d => $"\"{d:yyyy-MM-dd}\"",
            DateOnly d => $"\"{d:yyyy-MM-dd}\"",
            DateTimeOffset d => $"\"{d:yyyy-MM-dd}\"",
            IConvertible c when value is not string && !value.GetType().IsEnum => c.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "\"" + value.ToString()!.Replace("\"", "\"\"") + "\"",
        };
        return new(FormulaItemKind.Value, value.ToString() ?? text, text, text);
    }

    /// <summary>The operators of the editor's keypad, by group: calculation, comparison, logic.</summary>
    public static IReadOnlyList<FormulaOperatorGroup> OperatorGroups() =>
    [
        new(Strings.OperatorsCalculation, 3,
        [
            Operator("+", " + ", "OperatorAdd", null, "[A] + [B]"),
            Operator("−", " - ", "OperatorSubtract", null, "[A] - [B]"),
            Operator("( )", "()", "OperatorParentheses", "OperatorParenthesesHelp", "([A] - [B]) / [B]"),
            Operator("×", " * ", "OperatorMultiply", null, "[A] * 1.2"),
            Operator("÷", " / ", "OperatorDivide", "OperatorDivideHelp", "[A] / [B]"),
        ]),
        new(Strings.OperatorsComparison, 3,
        [
            Operator("=", " = ", "OperatorEqual", "OperatorComparisonHelp", "[A] = \"x\""),
            Operator("≠", " <> ", "OperatorNotEqual", "OperatorComparisonHelp", "[A] <> \"x\""),
            Operator("<", " < ", "OperatorLess", "OperatorComparisonHelp", "[A] < 100"),
            Operator("≤", " <= ", "OperatorLessOrEqual", "OperatorComparisonHelp", "[A] <= 100"),
            Operator(">", " > ", "OperatorGreater", "OperatorComparisonHelp", "[A] > \"2024-12-31\""),
            Operator("≥", " >= ", "OperatorGreaterOrEqual", "OperatorComparisonHelp", "[A] >= 100"),
        ]),
        new(Strings.OperatorsLogic, 2,
        [
            ForKeyword("AND") with { Kind = FormulaItemKind.Operator },
            ForKeyword("OR") with { Kind = FormulaItemKind.Operator },
            ForKeyword("NOT") with { Kind = FormulaItemKind.Operator, Text = "NOT " },
        ]),
    ];

    private static FormulaItem Operator(string caption, string text, string title, string? description, string example) =>
        new(FormulaItemKind.Operator, caption, text, Strings.Named(title), description is null ? null : Strings.Named(description), example);
}

/// <summary>A group of the operator keypad, laid out on <paramref name="Columns"/> columns.</summary>
public sealed record FormulaOperatorGroup(string Title, int Columns, IReadOnlyList<FormulaItem> Items);

/// <summary>A tab of the editor's catalog: the functions, the measures or the dimensions matching the search.</summary>
public sealed class FormulaCatalogTab(FormulaItemKind kind, string header, string glyph, IReadOnlyList<FormulaItem> all) : ObservableObject
{
    private readonly IReadOnlyList<FormulaItem> _all = all;
    private IReadOnlyList<FormulaItem> _items = all;

    public FormulaItemKind Kind => kind;

    public string Header => header;

    public string Glyph => glyph;

    /// <summary>The items matching the search.</summary>
    public IReadOnlyList<FormulaItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    internal void Filter(string search) => Items = _all.Where(i => TextMatch.Matches(i.Caption, search)).ToArray();
}
