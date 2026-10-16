using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace EasyBI.Sample;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Les bindings WPF utilisent en-US par défaut : on aligne sur la culture de l'utilisateur.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        base.OnStartup(e);
    }
}
