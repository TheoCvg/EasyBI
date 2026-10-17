using System.Resources;
using System.Runtime.CompilerServices;
using EasyBI.Core;

namespace EasyBI.Wpf.Resources;

/// <summary>Localized texts of EasyBI.Wpf (Strings.resx), resolved in <see cref="EasyBILocalization.UICulture"/>.</summary>
internal static class Strings
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Strings));

    public static string MarkBar => Get();
    public static string MarkLine => Get();
    public static string MarkPie => Get();
    public static string MarkKpi => Get();
    public static string MarkTable => Get();
    public static string MarkArea => Get();
    public static string MarkScatter => Get();
    public static string MarkStackedBar => Get();
    public static string SeriesMarkDefault => Get();
    public static string SeriesMarkHint => Get();
    public static string SecondaryAxis => Get();
    public static string SecondaryAxisHint => Get();
    public static string FilterAll => Get();
    public static string FilterExcept => Get();
    public static string Selection => Get();
    public static string ClearSelection => Get();
    public static string Error => Get();
    public static string NoRenderer => Get();
    public static string NoDimensionAvailable => Get();
    public static string AddVisual => Get();
    public static string AddText => Get();
    public static string AddImage => Get();
    public static string EditVisual => Get();
    public static string EditText => Get();
    public static string ChangeImage => Get();
    public static string RemoveItem => Get();
    public static string MoveItem => Get();
    public static string ResizeItem => Get();
    public static string AddRight => Get();
    public static string AddBelow => Get();
    public static string TextSmaller => Get();
    public static string TextLarger => Get();
    public static string TextBold => Get();
    public static string TextAlignment => Get();
    public static string TextPlaceholder => Get();
    public static string ImageFit => Get();
    public static string ImageFilter => Get();
    public static string ImageTooLarge => Get();
    public static string ImageUnreadable => Get();
    public static string BandOptions => Get();
    public static string BandFit => Get();
    public static string BandScroll => Get();
    public static string BandWrap => Get();
    public static string InsertBand => Get();
    public static string RemoveBand => Get();
    public static string AddBand => Get();
    public static string FullRow => Get();
    public static string FullRowWarning => Get();
    public static string FullRowProceed => Get();
    public static string EditorTitle => Get();
    public static string Ok => Get();
    public static string Cancel => Get();

    public static string Format(string format, params object?[] args) =>
        string.Format(EasyBILocalization.FormatCulture, format, args);

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
