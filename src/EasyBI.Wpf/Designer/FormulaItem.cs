using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>What a <see cref="FormulaItem"/> inserts into a formula.</summary>
public enum FormulaItemKind { Function, Measure, Dimension, Keyword, Operator, Value }

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
        FormulaItemKind.Measure => "Σ",
        FormulaItemKind.Dimension => Field?.DataType switch { DataType.Date => "◷", DataType.Number => "#", DataType.Boolean => "◑", _ => "Aa" },
        _ => "",
    };

    /// <summary>The group of the lists: functions, measures, dimensions.</summary>
    public string Group => Kind switch
    {
        FormulaItemKind.Function or FormulaItemKind.Keyword => Strings.FormulaFunctionsHeader,
        FormulaItemKind.Measure => Strings.MeasuresHeader,
        _ => Strings.DimensionsHeader,
    };

    public static FormulaItem For(FormulaFunction function) =>
        new(FormulaItemKind.Function, function.Name, function.Name + "()", function.Usage, function.Description);

    public static FormulaItem ForKeyword(string keyword) =>
        new(FormulaItemKind.Keyword, keyword, keyword is "TRUE" or "FALSE" ? keyword : $" {keyword} ",
            keyword, Strings.Named("FormulaKeyword" + keyword));

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

    /// <summary>The operators of the editor's toolbar.</summary>
    public static IReadOnlyList<FormulaItem> Operators() =>
    [
        Operator("+", " + ", "OperatorAdd", null, "[A] + [B]"),
        Operator("−", " - ", "OperatorSubtract", null, "[A] - [B]"),
        Operator("×", " * ", "OperatorMultiply", null, "[A] * 1.2"),
        Operator("÷", " / ", "OperatorDivide", "OperatorDivideHelp", "[A] / [B]"),
        Operator("( )", "()", "OperatorParentheses", "OperatorParenthesesHelp", "([A] - [B]) / [B]"),
        Operator("=", " = ", "OperatorEqual", "OperatorComparisonHelp", "[A] = \"x\""),
        Operator("≠", " <> ", "OperatorNotEqual", "OperatorComparisonHelp", "[A] <> \"x\""),
        Operator("<", " < ", "OperatorLess", "OperatorComparisonHelp", "[A] < 100"),
        Operator("≤", " <= ", "OperatorLessOrEqual", "OperatorComparisonHelp", "[A] <= 100"),
        Operator(">", " > ", "OperatorGreater", "OperatorComparisonHelp", "[A] > \"2024-12-31\""),
        Operator("≥", " >= ", "OperatorGreaterOrEqual", "OperatorComparisonHelp", "[A] >= 100"),
        ForKeyword("AND") with { Kind = FormulaItemKind.Operator },
        ForKeyword("OR") with { Kind = FormulaItemKind.Operator },
        ForKeyword("NOT") with { Kind = FormulaItemKind.Operator, Text = "NOT " },
    ];

    private static FormulaItem Operator(string caption, string text, string title, string? description, string example) =>
        new(FormulaItemKind.Operator, caption, text, Strings.Named(title), description is null ? null : Strings.Named(description), example);
}
