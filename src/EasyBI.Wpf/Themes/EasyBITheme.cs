using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf;

/// <summary>
/// The resources every EasyBI control draws with, looked up dynamically: an application restyles EasyBI by defining
/// these keys in its resources (all of them or a few), and switches theme at runtime by replacing the dictionary that
/// defines them. Without any, the light theme applies (<see cref="EasyBIThemeDictionary"/> offers light and dark).
/// <code>
/// &lt;SolidColorBrush x:Key="{x:Static bi:EasyBITheme.AccentBrushKey}" Color="#7C3AED" /&gt;
/// </code>
/// Brushes are <c>Brush</c>es, <see cref="ChartPaletteKey"/> a <see cref="ChartPalette"/>, <see cref="CornerRadiusKey"/>
/// a <c>CornerRadius</c>. Standard controls inside EasyBI (buttons, combo boxes...) have no explicit style: they take
/// the application's implicit styles, so a control library styles them too.
/// </summary>
public static class EasyBITheme
{
    /// <summary>Behind the dashboard, the designer and the filter bar.</summary>
    public static ComponentResourceKey BackgroundBrushKey { get; } = Key("Background");

    /// <summary>Cards (dashboard items, designer preview), popups and toolbars.</summary>
    public static ComponentResourceKey SurfaceBrushKey { get; } = Key("Surface");

    /// <summary>Secondary surfaces: field lists, designer shelves, empty items.</summary>
    public static ComponentResourceKey SurfaceAltBrushKey { get; } = Key("SurfaceAlt");

    /// <summary>Borders of cards, shelves and separators.</summary>
    public static ComponentResourceKey BorderBrushKey { get; } = Key("Border");

    /// <summary>Borders that must stand out: empty items, free spaces.</summary>
    public static ComponentResourceKey BorderStrongBrushKey { get; } = Key("BorderStrong");

    /// <summary>Main text.</summary>
    public static ComponentResourceKey ForegroundBrushKey { get; } = Key("Foreground");

    /// <summary>Secondary text: headers, hints, icons, axis labels.</summary>
    public static ComponentResourceKey MutedForegroundBrushKey { get; } = Key("MutedForeground");

    /// <summary>Text of what is unavailable.</summary>
    public static ComponentResourceKey DisabledForegroundBrushKey { get; } = Key("DisabledForeground");

    /// <summary>Selection, primary actions, drop indicators.</summary>
    public static ComponentResourceKey AccentBrushKey { get; } = Key("Accent");

    /// <summary>The accent under the pointer.</summary>
    public static ComponentResourceKey AccentHoverBrushKey { get; } = Key("AccentHover");

    /// <summary>Text and icons on the accent.</summary>
    public static ComponentResourceKey AccentForegroundBrushKey { get; } = Key("AccentForeground");

    /// <summary>A light tint of the accent: field chips, highlighted places.</summary>
    public static ComponentResourceKey AccentSoftBrushKey { get; } = Key("AccentSoft");

    /// <summary>Border of <see cref="AccentSoftBrushKey"/> areas.</summary>
    public static ComponentResourceKey AccentSoftBorderBrushKey { get; } = Key("AccentSoftBorder");

    /// <summary>A second tint, for filter chips.</summary>
    public static ComponentResourceKey SecondarySoftBrushKey { get; } = Key("SecondarySoft");

    /// <summary>Border of <see cref="SecondarySoftBrushKey"/> areas.</summary>
    public static ComponentResourceKey SecondarySoftBorderBrushKey { get; } = Key("SecondarySoftBorder");

    /// <summary>Background of small buttons under the pointer.</summary>
    public static ComponentResourceKey HoverBrushKey { get; } = Key("Hover");

    /// <summary>Error messages.</summary>
    public static ComponentResourceKey ErrorBrushKey { get; } = Key("Error");

    /// <summary>The Σ of measures in field lists.</summary>
    public static ComponentResourceKey MeasureBrushKey { get; } = Key("Measure");

    /// <summary>Grid lines of charts and tables.</summary>
    public static ComponentResourceKey GridLineBrushKey { get; } = Key("GridLine");

    /// <summary>Colors of the series of charts, in order (a <see cref="ChartPalette"/>).</summary>
    public static ComponentResourceKey ChartPaletteKey { get; } = Key("ChartPalette");

    /// <summary>Rounding of cards and chips (a <c>CornerRadius</c>).</summary>
    public static ComponentResourceKey CornerRadiusKey { get; } = Key("CornerRadius");

    /// <summary>
    /// The colors charts are drawn with at <paramref name="element"/>: the theme's palette and colors, unless
    /// <paramref name="theme"/> (an application's <see cref="EasyBIContext.Theme"/>) sets its own palette or colors.
    /// </summary>
    public static BiTheme ChartTheme(FrameworkElement element, BiTheme? theme = null)
    {
        theme ??= BiTheme.Default;
        var palette = !ReferenceEquals(theme, BiTheme.Default) ? theme.Palette
            : element.TryFindResource(ChartPaletteKey) is ChartPalette { Count: > 0 } colors ? colors.Select(Hex).ToArray()
            : theme.Palette;
        string? Color(ComponentResourceKey key) => element.TryFindResource(key) is SolidColorBrush brush ? Hex(brush.Color) : null;
        return theme with
        {
            Palette = palette,
            Foreground = theme.Foreground ?? Color(ForegroundBrushKey),
            MutedForeground = theme.MutedForeground ?? Color(MutedForegroundBrushKey),
            GridLine = theme.GridLine ?? Color(GridLineBrushKey),
            Background = theme.Background ?? Color(SurfaceBrushKey),
        };
    }

    private static string Hex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static ComponentResourceKey Key(string id) => new(typeof(EasyBITheme), id);
}

/// <summary>Colors of chart series, in order; declared in XAML as a list of <c>Color</c>s.</summary>
public sealed class ChartPalette : Collection<Color>
{
}

public enum EasyBIThemeMode { Light, Dark }

/// <summary>
/// EasyBI's own themes, to merge in the application's resources; change <see cref="Mode"/> to switch at runtime.
/// <code>
/// &lt;ResourceDictionary.MergedDictionaries&gt;
///     &lt;bi:EasyBIThemeDictionary Mode="Dark" /&gt;
/// &lt;/ResourceDictionary.MergedDictionaries&gt;
/// </code>
/// </summary>
public class EasyBIThemeDictionary : ResourceDictionary
{
    private EasyBIThemeMode _mode;

    public EasyBIThemeDictionary() => Load();

    public EasyBIThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Load();
        }
    }

    // Replacing the merged dictionary notifies every dynamic reference: open views follow at once
    private void Load()
    {
        MergedDictionaries.Clear();
        // Loaded like a control's XAML: works without an Application too (tests, other hosts)
        MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(new Uri($"/EasyBI.Wpf;component/Themes/{_mode}.xaml", UriKind.Relative)));
    }
}
