using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Markup;
using EasyBI.Core;
using EasyBI.Themes.MaterialDesign;
using EasyBI.Themes.WpfUi;
using EasyBI.Wpf;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace EasyBI.Sample;

public partial class App : Application
{
    /// <summary>The demo's own texts and model captions (Resources/Strings.resx).</summary>
    public static ResourceManager TextResources { get; } = new("EasyBI.Sample.Resources.Strings", typeof(App).Assembly);

    /// <summary>Bindable texts for XAML: {bi:Localize Key, Source={x:Static local:App.Texts}}.</summary>
    public static LocalizedStrings Texts { get; } = new(TextResources);

    /// <summary>Languages offered by the demo.</summary>
    public static IReadOnlyList<CultureInfo> Languages { get; } = [CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("fr-FR")];

    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF bindings default to en-US: align them with the user's culture.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // Start in the user's language when the demo supports it, English otherwise
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        SetLanguage(Languages.FirstOrDefault(l => l.TwoLetterISOLanguageName == language) ?? Languages[0]);
        SetTheme(ThemeLibrary.EasyBI, EasyBIThemeMode.Light);
        base.OnStartup(e);
    }

    /// <summary>Switches the whole application, EasyBI included, to another language at runtime.</summary>
    public static void SetLanguage(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        EasyBILocalization.Culture = culture;
    }

    private static ThemeLibrary? _library;
    private static readonly EasyBIThemeDictionary BuiltInTheme = new();
    // One instance each: the mappings listen to their library's theme changes for the application's lifetime, and
    // Material Design's theme manager belongs to its BundledTheme
    private static readonly Lazy<WpfUiTheme> WpfUiMapping = new();
    private static readonly Lazy<ResourceDictionary[]> MaterialDesign = new(() =>
    [
        new BundledTheme { BaseTheme = BaseTheme.Light, PrimaryColor = PrimaryColor.DeepPurple, SecondaryColor = SecondaryColor.Lime },
        new() { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") },
        new MaterialDesignTheme(),
    ]);

    /// <summary>
    /// Switches the theme at runtime: every view follows, charts included. A new library replaces the merged
    /// dictionaries; light/dark then goes through the library's own API, which EasyBI follows.
    /// </summary>
    public static void SetTheme(ThemeLibrary library, EasyBIThemeMode mode)
    {
        var dark = mode == EasyBIThemeMode.Dark;
        if (library != _library)
        {
            _library = library;
            var merged = Current.Resources.MergedDictionaries;
            merged.Clear();
            ResourceDictionary[] dictionaries = library switch
            {
                ThemeLibrary.WpfUi =>
                [
                    new ThemesDictionary { Theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light },
                    new ControlsDictionary(),
                    WpfUiMapping.Value,
                ],
                ThemeLibrary.MaterialDesign => MaterialDesign.Value,
                _ => [BuiltInTheme],
            };
            foreach (var dictionary in dictionaries) merged.Add(dictionary);
        }

        switch (library)
        {
            case ThemeLibrary.WpfUi:
                ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None);
                break;
            case ThemeLibrary.MaterialDesign:
                var palette = new PaletteHelper();
                var theme = palette.GetTheme();
                theme.SetBaseTheme(dark ? BaseTheme.Dark : BaseTheme.Light);
                palette.SetTheme(theme);
                break;
            default:
                BuiltInTheme.Mode = mode;
                break;
        }
    }
}

/// <summary>The design systems the demo can switch between.</summary>
public enum ThemeLibrary { EasyBI, WpfUi, MaterialDesign }
