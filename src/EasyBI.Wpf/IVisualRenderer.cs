using System.Windows;
using EasyBI.Abstractions;

namespace EasyBI.Wpf;

/// <summary>
/// Contrat qu'implémente un connecteur graphique (LiveCharts2, OxyPlot, ScottPlot...).
/// Il reçoit des données déjà requêtées et pivotées : son seul travail est de dessiner.
/// </summary>
public interface IVisualRenderer
{
    string Name { get; }

    RendererCapabilities Capabilities { get; }

    FrameworkElement Render(RenderRequest request);
}
