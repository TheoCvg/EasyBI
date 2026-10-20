using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// The editors of the properties panel, one <see cref="DataTemplate"/> per <see cref="PropertyKind"/> (their data
/// context is the <see cref="DesignerProperty"/>). Define a template with one of these keys to replace an editor.
/// </summary>
public static class DesignerPropertyTemplates
{
    public static ComponentResourceKey ToggleTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Toggle));
    public static ComponentResourceKey ChoiceTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Choice));
    public static ComponentResourceKey NumberTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Number));
    public static ComponentResourceKey TextTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Text));
    public static ComponentResourceKey InfoTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Info));
    public static ComponentResourceKey CustomTemplateKey { get; } = new(typeof(DesignerPropertyTemplates), nameof(PropertyKind.Custom));

    public static ComponentResourceKey KeyOf(PropertyKind kind) => kind switch
    {
        PropertyKind.Toggle => ToggleTemplateKey,
        PropertyKind.Choice => ChoiceTemplateKey,
        PropertyKind.Number => NumberTemplateKey,
        PropertyKind.Text => TextTemplateKey,
        PropertyKind.Info => InfoTemplateKey,
        _ => CustomTemplateKey,
    };
}

/// <summary>Picks the editor of a <see cref="DesignerProperty"/> by its kind (see <see cref="DesignerPropertyTemplates"/>).</summary>
public sealed class DesignerPropertyTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is DesignerProperty property && container is FrameworkElement element
            ? element.TryFindResource(DesignerPropertyTemplates.KeyOf(property.Kind)) as DataTemplate
            : null;
}
