using System.ComponentModel;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;
using EasyBI.Core;

namespace EasyBI.Wpf;

/// <summary>
/// Bindable view of a <see cref="ResourceManager"/>: <c>strings["Key"]</c> returns the text in the current
/// EasyBI language and bindings refresh when <see cref="EasyBILocalization.Culture"/> changes.
/// Create one instance per resource set and keep it (typically in a static property).
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs IndexerChanged = new(Binding.IndexerName);

    private readonly ResourceManager _resources;

    public LocalizedStrings(ResourceManager resources)
    {
        _resources = resources;
        EasyBILocalization.CultureChanged += (_, _) => PropertyChanged?.Invoke(this, IndexerChanged);
    }

    /// <summary>EasyBI's own texts (designer, filter bar, messages).</summary>
    public static LocalizedStrings EasyBI { get; } = new(Resources.Strings.ResourceManager);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _resources.GetString(key, EasyBILocalization.UICulture) ?? key;
}

/// <summary>
/// XAML text that follows the EasyBI language: <c>{bi:Localize Filters}</c> for EasyBI's texts,
/// or <c>{bi:Localize Key, Source={x:Static local:App.Strings}}</c> for an application's own resources.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    /// <summary>Resource set to read from; EasyBI's own texts when not set.</summary>
    public LocalizedStrings? Source { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Source ?? LocalizedStrings.EasyBI, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
