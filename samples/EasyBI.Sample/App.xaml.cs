using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Markup;
using EasyBI.Core;
using EasyBI.Wpf;

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
        base.OnStartup(e);
    }

    /// <summary>Switches the whole application, EasyBI included, to another language at runtime.</summary>
    public static void SetLanguage(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        EasyBILocalization.Culture = culture;
    }

    /// <summary>Switches EasyBI's theme at runtime: every view follows, charts included.</summary>
    public static void SetTheme(EasyBIThemeMode mode)
    {
        foreach (var theme in Current.Resources.MergedDictionaries.OfType<EasyBIThemeDictionary>()) theme.Mode = mode;
    }
}
