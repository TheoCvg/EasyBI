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

    private static void AssertMapsEveryBrush(IReadOnlyDictionary<ComponentResourceKey, object> map, ResourceDictionary library, string name)
    {
        var unmapped = BrushKeys.Where(p => !map.ContainsKey((ComponentResourceKey)p.GetValue(null)!)).Select(p => p.Name).ToArray();
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

        AssertMapsEveryBrush(MaterialDesignTheme.Map, library, $"Material Design {baseTheme}");
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
