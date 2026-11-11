using System.Resources;
using System.Runtime.CompilerServices;
using EasyBI.Core;

namespace EasyBI.Wpf.Navigation.Resources;

/// <summary>Localized texts of EasyBI.Wpf.Navigation (Strings.resx), resolved in <see cref="EasyBILocalization.UICulture"/>.</summary>
internal static class Strings
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Strings));

    public static string GoTo => Get();
    public static string Back => Get();
    public static string BackTooltip => Get();

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
