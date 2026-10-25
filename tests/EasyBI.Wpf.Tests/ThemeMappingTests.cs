using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyBI.Wpf.Tests;

public class ThemeMappingTests
{
    /// <summary>A design system whose resources live in a plain dictionary (no Application in tests).</summary>
    private sealed class FakeLibraryTheme(ResourceDictionary library) : EasyBIThemeMapping(new Dictionary<ComponentResourceKey, object>
    {
        [EasyBITheme.BackgroundBrushKey] = "Lib.Background",
        [EasyBITheme.AccentBrushKey] = "Lib.Primary",
    })
    {
        protected override object? FindResource(object key) => library.Contains(key) ? library[key] : null;
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    [Fact]
    public void Mapped_keys_take_the_design_systems_resources_and_the_others_keep_easybis() => Sta.Run(() =>
    {
        var primary = Brush(0x7C, 0x3A, 0xED);
        var theme = new FakeLibraryTheme(new ResourceDictionary { ["Lib.Background"] = Brush(255, 255, 255), ["Lib.Primary"] = primary });

        Assert.Same(primary, theme[EasyBITheme.AccentBrushKey]);
        Assert.False(theme.Contains(EasyBITheme.SurfaceBrushKey)); // unmapped: EasyBI's own default applies
        Assert.Equal(Color.FromRgb(0x2E, 0x6B, 0xE6), ((ChartPalette)theme[EasyBITheme.ChartPaletteKey])[0]); // light palette
    });

    [Fact]
    public void A_dark_background_selects_the_dark_palette_and_a_refresh_follows_new_resources() => Sta.Run(() =>
    {
        var library = new ResourceDictionary { ["Lib.Background"] = Brush(255, 255, 255), ["Lib.Primary"] = Brush(1, 2, 3) };
        var theme = new FakeLibraryTheme(library);
        var element = new Border();
        element.Resources.MergedDictionaries.Add(theme);
        element.SetResourceReference(Border.BackgroundProperty, EasyBITheme.AccentBrushKey);

        // The design system switches theme: new objects under the same keys
        var dark = Brush(0x7C, 0x3A, 0xED);
        library["Lib.Background"] = Brush(0x11, 0x11, 0x11);
        library["Lib.Primary"] = dark;
        theme.Refresh();

        Assert.Same(dark, element.Background);
        Assert.Equal(Color.FromRgb(0x4C, 0x8D, 0xFF), ((ChartPalette)theme[EasyBITheme.ChartPaletteKey])[0]);
    });

    private sealed class BrandTheme(ResourceDictionary brand) : EasyBIThemeMapping(new Dictionary<ComponentResourceKey, object>
    {
        [EasyBITheme.BackgroundBrushKey] = "Brand.Background",
        [EasyBITheme.ChartPaletteKey] = "Brand.ChartColors",
    })
    {
        protected override object? FindResource(object key) => brand.Contains(key) ? brand[key] : null;
    }

    [Fact]
    public void A_design_system_with_chart_colors_maps_the_palette_to_them() => Sta.Run(() =>
    {
        var colors = new ChartPalette { Color.FromRgb(0x6F, 0x4E, 0x37), Color.FromRgb(0xC8, 0x96, 0x5A) };
        var theme = new BrandTheme(new ResourceDictionary { ["Brand.Background"] = Brush(0x11, 0x11, 0x11), ["Brand.ChartColors"] = colors });

        Assert.Same(colors, theme[EasyBITheme.ChartPaletteKey]);
    });
}
