using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Built-in editor of a dashboard visual: the report designer in a dialog.</summary>
public partial class VisualEditorWindow : Window
{
    public VisualEditorWindow(EasyBIContext context, VisualDefinition? visual)
    {
        InitializeComponent();
        Designer.Context = context;
        if (visual is not null) Designer.Edit(visual);
    }

    /// <summary>The visual confirmed with OK.</summary>
    public VisualDefinition? Visual { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Designer.Visual is not { } visual) return;
        if (VisualValidator.Validate(visual, Designer.Context?.VisualTypes) is { } problem)
        {
            Problem.Text = problem;
            return;
        }
        Visual = visual;
        DialogResult = true;
    }
}
