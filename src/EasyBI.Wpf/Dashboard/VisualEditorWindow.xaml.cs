using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Built-in editor of a dashboard visual: the report designer in a dialog.</summary>
public partial class VisualEditorWindow : Window
{
    private readonly DesignerViewModel _designer;

    public VisualEditorWindow(EasyBIContext context, VisualDefinition? visual)
    {
        InitializeComponent();
        Designer.DataContext = _designer = new DesignerViewModel(context);
        if (visual is not null) _designer.Load(visual);
    }

    /// <summary>The visual confirmed with OK.</summary>
    public VisualDefinition? Visual { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_designer.Visual is not { } visual) return;
        if (VisualValidator.Validate(visual, _designer.Context.VisualTypes) is { } problem)
        {
            Problem.Text = problem;
            return;
        }
        Visual = visual;
        DialogResult = true;
    }
}
