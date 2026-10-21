using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Edits a <see cref="ValueFormatViewModel"/> (its DataContext): fixed style, rules, color scale, data bars and icons of
/// a measure. Lookless: its default template is in <c>Themes/ValueFormatEditor.xaml</c>.
/// </summary>
public class ValueFormatEditor : Control
{
    static ValueFormatEditor() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ValueFormatEditor), new FrameworkPropertyMetadata(typeof(ValueFormatEditor)));
}
