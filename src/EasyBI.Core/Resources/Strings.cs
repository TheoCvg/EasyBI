using System.Resources;
using System.Runtime.CompilerServices;

namespace EasyBI.Core.Resources;

/// <summary>Localized texts of EasyBI.Core (Strings.resx), resolved in <see cref="EasyBILocalization.UICulture"/>.</summary>
internal static class Strings
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Strings));

    public static string GrainNone => Get();
    public static string GrainYear => Get();
    public static string GrainQuarter => Get();
    public static string GrainMonth => Get();
    public static string GrainDay => Get();
    public static string AggregationSum => Get();
    public static string AggregationAverage => Get();
    public static string AggregationMin => Get();
    public static string AggregationMax => Get();
    public static string AggregationCount => Get();
    public static string Blank => Get();
    public static string Yes => Get();
    public static string No => Get();
    public static string Total => Get();
    public static string SelectionOr => Get();
    public static string SelectionAnd => Get();
    public static string QuarterLabel => Get();
    public static string ValidationEmptyTable => Get();
    public static string ValidationNoMeasure => Get();
    public static string ValidationKpi => Get();
    public static string ValidationPie => Get();
    public static string ValidationGauge => Get();
    public static string ValidationWaterfall => Get();
    public static string ValidationNoImage => Get();
    public static string ValidationSeriesMark => Get();
    public static string ReportUntitledVisual => Get();
    public static string ReportTitledVisual => Get();
    public static string ReportFilter => Get();
    public static string ReportUnknownModel => Get();
    public static string ReportSortNotDisplayed => Get();
    public static string ReportGrainOnNonDate => Get();
    public static string ReportMissingField => Get();
    public static string ReportNotADimension => Get();
    public static string ReportNotAMeasure => Get();
    public static string FormulaEmpty => Get();
    public static string FormulaUnexpected => Get();
    public static string FormulaExpectedOperand => Get();
    public static string FormulaMissingParenthesis => Get();
    public static string FormulaMissingBracket => Get();
    public static string FormulaDecimalComma => Get();
    public static string FormulaUnknownField => Get();
    public static string FormulaMissingQuote => Get();
    public static string FormulaDimensionOutsideFunction => Get();
    public static string FormulaNotAggregatable => Get();
    public static string FormulaNestedAggregation => Get();
    public static string FormulaUsage => Get();
    public static string FormulaUnknownFunction => Get();
    public static string FormulaMismatch => Get();
    public static string FormulaEqualityOnly => Get();
    public static string FormulaNotADate => Get();
    public static string FormulaNotANumber => Get();
    public static string FormulaNotACondition => Get();
    public static string FormulaCycle => Get();
    public static string FormulaThisMeasure => Get();
    public static string FormulaAmbiguous => Get();
    public static string ReportCalculatedMeasure => Get();
    public static string ReportInvalidFormula => Get();
    public static string PeriodAny => Get();
    public static string PeriodBetween => Get();
    public static string PeriodSince => Get();
    public static string PeriodUntil => Get();
    public static string ReportPeriodOnNonDate => Get();
    public static string ReportComparisonOnNonNumber => Get();
    public static string ReportMeasureFilterOnReport => Get();
    public static string CompareGreaterThan => Get();
    public static string CompareGreaterThanOrEqual => Get();
    public static string CompareLessThan => Get();
    public static string CompareLessThanOrEqual => Get();
    public static string CompareRange => Get();

    /// <summary>A text whose key is built (PeriodLastMonth...).</summary>
    public static string Named(string key) => Get(key);

    public static string Format(string format, params object?[] args) =>
        string.Format(EasyBILocalization.FormatCulture, format, args);

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
