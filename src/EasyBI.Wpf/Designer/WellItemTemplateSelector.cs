using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Shows the items of a well: a filter with <see cref="FilterTemplate"/> (its implicit template is its editor, in the
/// properties panel), the other chips with the template of their type.
/// </summary>
public sealed class WellItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FilterTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is FilterItemViewModel ? FilterTemplate : base.SelectTemplate(item, container);
}
