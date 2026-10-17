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
    public static string QuarterLabel => Get();
    public static string ValidationEmptyTable => Get();
    public static string ValidationNoMeasure => Get();
    public static string ValidationKpi => Get();
    public static string ValidationPie => Get();
    public static string ValidationNoImage => Get();
    public static string ReportUntitledVisual => Get();
    public static string ReportTitledVisual => Get();
    public static string ReportFilter => Get();
    public static string ReportUnknownModel => Get();
    public static string ReportSortNotDisplayed => Get();
    public static string ReportGrainOnNonDate => Get();
    public static string ReportMissingField => Get();
    public static string ReportNotADimension => Get();
    public static string ReportNotAMeasure => Get();

    public static string Format(string format, params object?[] args) =>
        string.Format(EasyBILocalization.FormatCulture, format, args);

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
