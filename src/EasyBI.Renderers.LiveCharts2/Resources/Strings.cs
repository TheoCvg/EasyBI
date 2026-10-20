using System.Resources;
using System.Runtime.CompilerServices;
using EasyBI.Core;

namespace EasyBI.Renderers.LiveCharts2.Resources;

/// <summary>Localized texts of the LiveCharts2 connector (Strings.resx), resolved in <see cref="EasyBILocalization.UICulture"/>.</summary>
internal static class Strings
{
    public static ResourceManager ResourceManager { get; } = new(typeof(Strings));

    public static string Scrollable => Get();
    public static string ScrollableHint => Get();

    private static string Get([CallerMemberName] string name = "") =>
        ResourceManager.GetString(name, EasyBILocalization.UICulture) ?? name;
}
