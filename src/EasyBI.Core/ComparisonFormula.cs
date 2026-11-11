using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Formulas of <see cref="ReportComparisonOutput"/>: what a comparison shows, computed from <c>[Value]</c>, the value of
/// the measure, and <c>[Compared]</c>, its value in the compared period. They are written like the formulas of
/// measures (see <see cref="MeasureFormula"/>): numbers, + - * / and parentheses, and
/// <c>IF(condition; value; otherwise)</c> with comparisons, AND, OR and NOT. A missing value or a division by zero gives
/// an empty value. Stored with <c>[Value]</c> and <c>[Compared]</c>, shown with their names in the EasyBI language.
/// </summary>
public static class ComparisonFormula
{
    /// <summary>The value of the measure, as a formula stores it.</summary>
    public const string Value = "Value";

    /// <summary>The value of the measure in the compared period, as a formula stores it.</summary>
    public const string Compared = "Compared";

    /// <summary>The name of <see cref="Value"/> in the EasyBI language.</summary>
    public static string ValueCaption => Strings.ComparisonFormulaValue;

    /// <summary>The name of <see cref="Compared"/> in the EasyBI language.</summary>
    public static string ComparedCaption => Strings.ComparisonFormulaCompared;

    /// <summary>
    /// Checks the formula typed by the user, where values are named in the EasyBI language or as stored.
    /// </summary>
    /// <returns>
    /// Null with the <paramref name="stored"/> formula when it is valid, otherwise the problem and where it is written.
    /// </returns>
    public static FormulaProblem? Check(string text, out string? stored) => MeasureFormula.CheckComparison(text, Resolve, out stored);

    /// <summary>Null when the stored formula is valid, otherwise the problem worded for the end user.</summary>
    public static string? Validate(string formula) => Check(formula, out _)?.Message;

    /// <summary>The stored formula as the user reads it: <c>[Value]</c> and <c>[Compared]</c> in the EasyBI language.</summary>
    public static string ToDisplay(string formula) =>
        MeasureFormula.Rename(formula, name => name switch
        {
            Value => ValueCaption,
            Compared => ComparedCaption,
            _ => name,
        });

    /// <summary>
    /// The formula as a function of the value and the compared one. A formula that is not valid gives empty values.
    /// </summary>
    public static Func<double?, double?, double?> Compile(string formula) =>
        MeasureFormula.CompileComparison(formula, Resolve) ?? ((_, _) => null);

    // A name typed or stored: the stored name, null when unknown
    private static string? Resolve(string name) =>
        Is(name, Value) || Is(name, ValueCaption) ? Value
        : Is(name, Compared) || Is(name, ComparedCaption) ? Compared
        : null;

    private static bool Is(string name, string reference) => string.Equals(name, reference, StringComparison.OrdinalIgnoreCase);
}
