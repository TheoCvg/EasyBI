using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;

[assembly: InternalsVisibleTo("EasyBI.Wpf.Tests")]

// Default styles of the lookless controls (DashboardItem) are in Themes/Generic.xaml
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

// One XAML namespace for every public type of the library:
// xmlns:bi="https://github.com/TheoCvg/EasyBI"
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf")]
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf.Dashboard")]
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf.Dashboard.Behaviors")]
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf.Designer")]
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf.DragAndDrop")]
[assembly: XmlnsDefinition("https://github.com/TheoCvg/EasyBI", "EasyBI.Wpf.Renderers")]
[assembly: XmlnsPrefix("https://github.com/TheoCvg/EasyBI", "bi")]
