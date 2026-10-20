using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Edits a <see cref="FilterItemViewModel"/> (its DataContext): the values to check, or the period of a date dimension.
/// Lookless: its default template is in <c>Themes/FilterEditor.xaml</c>.
/// </summary>
public class FilterEditor : Control
{
    static FilterEditor() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FilterEditor), new FrameworkPropertyMetadata(typeof(FilterEditor)));
}
