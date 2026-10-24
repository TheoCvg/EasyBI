using System.Windows;
using EasyBI.Wpf;
using Wpf.Ui.Appearance;

namespace EasyBI.Themes.WpfUi;

/// <summary>
/// Makes EasyBI follow WPF UI: merge it after WPF UI's dictionaries. EasyBI's surfaces, text, accent and charts take
/// WPF UI's colors, and follow its theme when <see cref="ApplicationThemeManager"/> switches it; standard controls
/// inside EasyBI take WPF UI's styles.
/// <code>
/// &lt;ResourceDictionary.MergedDictionaries&gt;
///     &lt;ui:ThemesDictionary Theme="Dark" /&gt;
///     &lt;ui:ControlsDictionary /&gt;
///     &lt;bi:WpfUiTheme /&gt;
/// &lt;/ResourceDictionary.MergedDictionaries&gt;
/// </code>
/// </summary>
public sealed class WpfUiTheme : EasyBIThemeMapping
{
    public WpfUiTheme() : base(Map)
    {
        // WPF UI swaps its theme dictionaries: the mapped brushes are new objects
        ApplicationThemeManager.Changed += (_, _) => Refresh();
    }

    /// <summary>WPF UI has no colors for the parts of a formula: EasyBI's light or dark ones apply.</summary>
    public static IReadOnlySet<ComponentResourceKey> KeptKeys { get; } = new HashSet<ComponentResourceKey>
    {
        EasyBITheme.FormulaFunctionBrushKey,
        EasyBITheme.FormulaDimensionBrushKey,
        EasyBITheme.FormulaTextBrushKey,
        EasyBITheme.FormulaNumberBrushKey,
    };

    protected override IEnumerable<ComponentResourceKey> Kept => KeptKeys;

    /// <summary>The WPF UI resource each EasyBI key takes its value from.</summary>
    public static IReadOnlyDictionary<ComponentResourceKey, object> Map { get; } = new Dictionary<ComponentResourceKey, object>
    {
        [EasyBITheme.BackgroundBrushKey] = "ApplicationBackgroundBrush",
        [EasyBITheme.SurfaceBrushKey] = "SolidBackgroundFillColorQuarternaryBrush",
        [EasyBITheme.SurfaceAltBrushKey] = "SolidBackgroundFillColorSecondaryBrush",
        [EasyBITheme.BorderBrushKey] = "CardStrokeColorDefaultBrush",
        [EasyBITheme.BorderStrongBrushKey] = "ControlStrokeColorSecondaryBrush",
        [EasyBITheme.ForegroundBrushKey] = "TextFillColorPrimaryBrush",
        [EasyBITheme.MutedForegroundBrushKey] = "TextFillColorSecondaryBrush",
        [EasyBITheme.DisabledForegroundBrushKey] = "TextFillColorDisabledBrush",
        [EasyBITheme.AccentBrushKey] = "AccentFillColorDefaultBrush",
        [EasyBITheme.AccentHoverBrushKey] = "AccentFillColorSecondaryBrush",
        [EasyBITheme.AccentForegroundBrushKey] = "TextOnAccentFillColorPrimaryBrush",
        [EasyBITheme.AccentSoftBrushKey] = "ControlFillColorSecondaryBrush",
        [EasyBITheme.AccentSoftBorderBrushKey] = "ControlStrokeColorDefaultBrush",
        [EasyBITheme.SecondarySoftBrushKey] = "SystemFillColorCautionBackgroundBrush",
        [EasyBITheme.SecondarySoftBorderBrushKey] = "ControlStrokeColorDefaultBrush",
        [EasyBITheme.HoverBrushKey] = "SubtleFillColorSecondaryBrush",
        [EasyBITheme.ErrorBrushKey] = "SystemFillColorCriticalBrush",
        [EasyBITheme.MeasureBrushKey] = "SystemFillColorSuccessBrush",
        [EasyBITheme.SuccessBrushKey] = "SystemFillColorSuccessBrush",
        [EasyBITheme.WarningBrushKey] = "SystemFillColorCautionBrush",
        [EasyBITheme.GridLineBrushKey] = "DividerStrokeColorDefaultBrush",
    };
}
