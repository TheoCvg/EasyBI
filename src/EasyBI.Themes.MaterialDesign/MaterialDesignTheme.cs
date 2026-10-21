using System.Windows;
using EasyBI.Wpf;
using MaterialDesignThemes.Wpf;

namespace EasyBI.Themes.MaterialDesign;

/// <summary>
/// Makes EasyBI follow Material Design in XAML: merge it after Material Design's dictionaries. EasyBI's surfaces,
/// text, primary color and charts take the Material Design theme's colors, and follow its theme when
/// <see cref="PaletteHelper.SetTheme"/> switches it; standard controls inside EasyBI take Material Design's styles.
/// <code>
/// &lt;ResourceDictionary.MergedDictionaries&gt;
///     &lt;md:BundledTheme BaseTheme="Dark" PrimaryColor="DeepPurple" SecondaryColor="Lime" /&gt;
///     &lt;ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" /&gt;
///     &lt;bi:MaterialDesignTheme /&gt;
/// &lt;/ResourceDictionary.MergedDictionaries&gt;
/// </code>
/// Call <see cref="EasyBIThemeMapping.Refresh"/> if the application swaps Material Design's dictionaries instead.
/// </summary>
public sealed class MaterialDesignTheme : EasyBIThemeMapping
{
    private readonly ResourceDictionary? _resources;
    private IThemeManager? _themeManager;

    public MaterialDesignTheme() : this(null) { }

    /// <param name="resources">Where Material Design's theme lives; null for the application's resources.</param>
    internal MaterialDesignTheme(ResourceDictionary? resources) : base(Map) => _resources = resources;

    private ResourceDictionary? Resources => _resources ?? Application.Current?.Resources;

    protected override object? FindResource(object key)
    {
        FollowThemeChanges();
        return _resources is null ? base.FindResource(key) : _resources[key];
    }

    // SetTheme replaces the theme's brushes with new objects: read them again. The theme manager only exists once
    // Material Design's theme is in the resources, which may come after this dictionary is created.
    private void FollowThemeChanges()
    {
        if (_themeManager is not null || Resources is not { } resources) return;
        _themeManager = resources.GetThemeManager();
        if (_themeManager is not null) _themeManager.ThemeChanged += (_, _) => Refresh();
    }

    /// <summary>Material Design has no success or warning color (conditional formats): EasyBI's light or dark ones apply.</summary>
    public static IReadOnlySet<ComponentResourceKey> KeptKeys { get; } = new HashSet<ComponentResourceKey>
    {
        EasyBITheme.SuccessBrushKey,
        EasyBITheme.WarningBrushKey,
    };

    protected override IEnumerable<ComponentResourceKey> Kept => KeptKeys;

    /// <summary>The Material Design resource each EasyBI key takes its value from.</summary>
    public static IReadOnlyDictionary<ComponentResourceKey, object> Map { get; } = new Dictionary<ComponentResourceKey, object>
    {
        [EasyBITheme.BackgroundBrushKey] = "MaterialDesign.Brush.Background",
        [EasyBITheme.SurfaceBrushKey] = "MaterialDesign.Brush.Card.Background",
        [EasyBITheme.SurfaceAltBrushKey] = "MaterialDesign.Brush.ToolBar.Background",
        [EasyBITheme.BorderBrushKey] = "MaterialDesign.Brush.Card.Border",
        [EasyBITheme.BorderStrongBrushKey] = "MaterialDesign.Brush.TextBox.Border",
        [EasyBITheme.ForegroundBrushKey] = "MaterialDesign.Brush.Foreground",
        [EasyBITheme.MutedForegroundBrushKey] = "MaterialDesign.Brush.ForegroundLight",
        [EasyBITheme.DisabledForegroundBrushKey] = "MaterialDesign.Brush.CheckBox.Disabled",
        [EasyBITheme.AccentBrushKey] = "MaterialDesign.Brush.Primary",
        [EasyBITheme.AccentHoverBrushKey] = "MaterialDesign.Brush.Primary.Dark",
        [EasyBITheme.AccentForegroundBrushKey] = "MaterialDesign.Brush.Primary.Foreground",
        [EasyBITheme.AccentSoftBrushKey] = "MaterialDesign.Brush.Chip.Background",
        [EasyBITheme.AccentSoftBorderBrushKey] = "MaterialDesign.Brush.Chip.OutlineBorder",
        [EasyBITheme.SecondarySoftBrushKey] = "MaterialDesign.Brush.Secondary.Light",
        [EasyBITheme.SecondarySoftBorderBrushKey] = "MaterialDesign.Brush.Secondary",
        [EasyBITheme.HoverBrushKey] = "MaterialDesign.Brush.ListView.Hover",
        [EasyBITheme.ErrorBrushKey] = "MaterialDesign.Brush.ValidationError",
        [EasyBITheme.MeasureBrushKey] = "MaterialDesign.Brush.Secondary",
        [EasyBITheme.GridLineBrushKey] = "MaterialDesign.Brush.Separator.Background",
    };
}
