using System.Reflection;
using System.Windows;
using System.Windows.Media;
using EasyBI.Themes.MaterialDesign;
using EasyBI.Themes.WpfUi;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace EasyBI.Wpf.Tests;

/// <summary>
/// A misspelled library key would silently fall back to EasyBI's light theme: every mapped key must exist in the
/// library's real dictionaries, in light and dark.
/// </summary>
public class LibraryThemeTests
{
    static LibraryThemeTests() => _ = Application.Current; // Application's static constructor registers pack://

    private static readonly PropertyInfo[] BrushKeys = typeof(EasyBITheme).GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.Name.EndsWith("BrushKey"))
        .ToArray();

    // kept: the keys a library has no color for, declared as such
    private static void AssertMapsEveryBrush(IReadOnlyDictionary<ComponentResourceKey, object> map, ResourceDictionary library, string name,
        IReadOnlySet<ComponentResourceKey>? kept = null)
    {
        var unmapped = BrushKeys
            .Select(p => (p.Name, Key: (ComponentResourceKey)p.GetValue(null)!))
            .Where(k => !map.ContainsKey(k.Key) && kept?.Contains(k.Key) != true)
            .Select(k => k.Name)
            .ToArray();
        Assert.True(unmapped.Length == 0, $"{name} maps no value for: {string.Join(", ", unmapped)}");
        var missing = map.Values.Where(source => library[source] is not Brush).ToArray();
        Assert.True(missing.Length == 0, $"{name} has no brush named: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData(BaseTheme.Light)]
    [InlineData(BaseTheme.Dark)]
    public void Material_design_defines_every_mapped_resource(BaseTheme baseTheme) => Sta.Run(() =>
    {
        var library = new ResourceDictionary();
        library.MergedDictionaries.Add(new BundledTheme { BaseTheme = baseTheme, PrimaryColor = PrimaryColor.DeepPurple, SecondaryColor = SecondaryColor.Lime });
        library.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") });

        AssertMapsEveryBrush(MaterialDesignTheme.Map, library, $"Material Design {baseTheme}", MaterialDesignTheme.KeptKeys);
    });

    [Fact]
    public void Material_design_theme_switches_are_followed() => Sta.Run(() =>
    {
        var library = new ResourceDictionary();
        library.MergedDictionaries.Add(new BundledTheme { BaseTheme = BaseTheme.Light, PrimaryColor = PrimaryColor.DeepPurple, SecondaryColor = SecondaryColor.Lime });
        var element = new System.Windows.Controls.Border();
        element.Resources.MergedDictionaries.Add(new MaterialDesignTheme(library));
        element.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, EasyBITheme.BackgroundBrushKey);
        var light = ((SolidColorBrush)element.Background).Color;

        // What PaletteHelper.SetTheme does: new (frozen) brushes under the same keys
        var theme = library.GetTheme();
        theme.SetBaseTheme(BaseTheme.Dark);
        library.SetTheme(theme);

        Assert.Equal(((SolidColorBrush)library["MaterialDesign.Brush.Background"]).Color, ((SolidColorBrush)element.Background).Color);
        Assert.NotEqual(light, ((SolidColorBrush)element.Background).Color);
    });

    [Theory]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public void Wpf_ui_defines_every_mapped_resource(ApplicationTheme theme) => Sta.Run(() =>
    {
        var library = new ResourceDictionary();
        library.MergedDictionaries.Add(new ThemesDictionary { Theme = theme });
        library.MergedDictionaries.Add(new ControlsDictionary());

        AssertMapsEveryBrush(WpfUiTheme.Map, library, $"WPF UI {theme}");
    });
}
