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
    public static string FilterAll => Get();
    public static string FilterExcept => Get();
    public static string Selection => Get();
    public static string ClearSelection => Get();
    public static string Error => Get();
    public static string NoRenderer => Get();
    public static string NoDimensionAvailable => Get();

    public static string Format(string format, params object?[] args) =>
        string.Format(EasyBILocalization.FormatCulture, format, args);

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
