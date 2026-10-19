using System.Windows;
using System.Windows.Media;

namespace EasyBI.Wpf;

/// <summary>
/// Makes EasyBI follow another design system: each <see cref="EasyBITheme"/> key is mapped to a resource key of that
/// system (a control library, an application's own theme), resolved in the application's resources each time it is
/// read. Merge the mapping after the system's dictionaries; call <see cref="Refresh"/> after the system switched to
/// dictionaries holding other objects (the libraries' theme packages do it for you). Keys left unmapped keep EasyBI's
/// light theme values, and the chart palette follows the mapped background (EasyBI's light or dark palette).
/// <code>
/// public sealed class MyTheme : EasyBIThemeMapping
/// {
///     public MyTheme() : base(new Dictionary&lt;ComponentResourceKey, object&gt;
///     {
///         [EasyBITheme.AccentBrushKey] = "MyPrimaryBrush",
///         [EasyBITheme.SurfaceBrushKey] = "MyCardBrush",
///     }) { }
/// }
/// </code>
/// </summary>
public abstract class EasyBIThemeMapping : ResourceDictionary
{
    private static readonly Lazy<ResourceDictionary> Light = new(() => Load(EasyBIThemeMode.Light));
    private static readonly Lazy<ResourceDictionary> Dark = new(() => Load(EasyBIThemeMode.Dark));

    private readonly IReadOnlyDictionary<ComponentResourceKey, object> _map;

    /// <param name="map">For each EasyBI key, the key of the resource it takes its value from.</param>
    protected EasyBIThemeMapping(IReadOnlyDictionary<ComponentResourceKey, object> map)
    {
        _map = map;
        Refresh();
    }

    /// <summary>
    /// Makes every EasyBI view read the mapped resources again: call it after the design system replaced its
    /// resources with new objects (a theme switch that swaps dictionaries).
    /// </summary>
    public void Refresh()
    {
        // Setting the entries again notifies the dynamic references, which read them through OnGettingValue
        foreach (var key in _map.Keys) this[key] = new Mapped(_map[key]);
        this[EasyBITheme.ChartPaletteKey] = new Mapped(EasyBITheme.ChartPaletteKey);
    }

    /// <summary>Finds a resource of the design system (in the application's resources by default).</summary>
    protected virtual object? FindResource(object key) => Application.Current?.TryFindResource(key);

    /// <summary>True when the mapped background is dark: the dark chart palette then applies.</summary>
    protected virtual bool IsDark => Resolve(EasyBITheme.BackgroundBrushKey) is SolidColorBrush { Color: var c }
        && (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;

    protected override void OnGettingValue(object key, ref object value, out bool canCache)
    {
        base.OnGettingValue(key, ref value, out canCache);
        if (value is not Mapped) return;
        // Resolved at each read: the design system may change its resources at any time
        canCache = false;
        value = Equals(key, EasyBITheme.ChartPaletteKey)
            ? (IsDark ? Dark : Light).Value[EasyBITheme.ChartPaletteKey]
            : Resolve(key) ?? Light.Value[key];
    }

    private object? Resolve(object key) => _map.TryGetValue((ComponentResourceKey)key, out var source) ? FindResource(source) : null;

    private static ResourceDictionary Load(EasyBIThemeMode mode) => new EasyBIThemeDictionary { Mode = mode };

    /// <summary>A mapped entry, replaced by the design system's resource when read.</summary>
    private sealed record Mapped(object Source);
}
