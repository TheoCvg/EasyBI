using System.Windows.Controls;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Edits a <see cref="FilterItemViewModel"/> (its DataContext): the values to check, or the period of a date dimension.
/// </summary>
public partial class FilterEditor : UserControl
{
    public FilterEditor() => InitializeComponent();
}
