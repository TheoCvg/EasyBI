using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Tests;

public class ThemeTests
{
    private static IEnumerable<ComponentResourceKey> Keys() =>
        typeof(EasyBITheme).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ComponentResourceKey))
            .Select(p => (ComponentResourceKey)p.GetValue(null)!);

    private static Color ColorOf(ResourceDictionary resources, ComponentResourceKey key) => ((SolidColorBrush)resources[key]).Color;

    [Fact]
    public void Light_and_dark_themes_define_every_key() => Sta.Run(() =>
    {
        foreach (var mode in Enum.GetValues<EasyBIThemeMode>())
        {
            var theme = new EasyBIThemeDictionary { Mode = mode };
            var missing = Keys().Where(k => !theme.Contains(k)).Select(k => k.ResourceId).ToArray();
            Assert.True(missing.Length == 0, $"{mode} misses: {string.Join(", ", missing)}");
        }
    });

    [Fact]
    public void Changing_the_mode_replaces_the_resources() => Sta.Run(() =>
    {
        var theme = new EasyBIThemeDictionary();
        var light = ColorOf(theme, EasyBITheme.BackgroundBrushKey);

        theme.Mode = EasyBIThemeMode.Dark;

        Assert.NotEqual(light, ColorOf(theme, EasyBITheme.BackgroundBrushKey));
        Assert.Equal(10, ((ChartPalette)theme[EasyBITheme.ChartPaletteKey]).Count);
    });

    [Fact]
    public void Charts_take_the_palette_and_colors_of_the_resources_around_them() => Sta.Run(() =>
    {
        var element = new Border();
        element.Resources.MergedDictionaries.Add(new EasyBIThemeDictionary { Mode = EasyBIThemeMode.Dark });
        element.Resources[EasyBITheme.ForegroundBrushKey] = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56));

        var theme = EasyBITheme.ChartTheme(element);

        Assert.Equal("#FF123456", theme.Foreground); // the application's override wins
        Assert.Equal("#FF4C8DFF", theme.Palette[0]);
        Assert.NotNull(theme.GridLine);
        Assert.NotNull(theme.Background);
    });

    [Fact]
    public void A_palette_set_by_the_application_on_the_context_wins_over_the_theme() => Sta.Run(() =>
    {
        var element = new Border();
        element.Resources.MergedDictionaries.Add(new EasyBIThemeDictionary { Mode = EasyBIThemeMode.Dark });
        var custom = new BiTheme(["#000000"]) { GridLine = "#FF0000" };

        var theme = EasyBITheme.ChartTheme(element, custom);

        Assert.Equal(["#000000"], theme.Palette);
        Assert.Equal("#FF0000", theme.GridLine);
        Assert.NotNull(theme.Foreground); // what it does not set still follows the theme
    });
}
