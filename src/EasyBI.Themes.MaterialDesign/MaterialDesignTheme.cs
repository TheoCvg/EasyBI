using System.Windows;
using EasyBI.Wpf;

namespace EasyBI.Themes.MaterialDesign;

/// <summary>
/// Makes EasyBI follow Material Design in XAML: merge it after Material Design's dictionaries. EasyBI's surfaces,
/// text, primary color and charts take the Material Design theme's colors (also after <c>PaletteHelper.SetTheme</c>,
/// which changes the theme's brushes in place); standard controls inside EasyBI take Material Design's styles.
/// <code>
/// &lt;ResourceDictionary.MergedDictionaries&gt;
///     &lt;md:BundledTheme BaseTheme="Dark" PrimaryColor="DeepPurple" SecondaryColor="Lime" /&gt;
///     &lt;ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" /&gt;
///     &lt;bi:MaterialDesignTheme /&gt;
/// &lt;/ResourceDictionary.MergedDictionaries&gt;
/// </code>
/// Call <see cref="EasyBIThemeMapping.Refresh"/> if the application swaps Material Design's dictionaries instead.
/// </summary>
public sealed class MaterialDesignTheme() : EasyBIThemeMapping(Map)
{
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
