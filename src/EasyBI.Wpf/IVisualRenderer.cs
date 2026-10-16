using System.Windows;
using EasyBI.Abstractions;

namespace EasyBI.Wpf;

/// <summary>
/// Contract implemented by a charting connector (LiveCharts2, OxyPlot, ScottPlot...).
/// It receives data that is already queried and pivoted: its only job is to draw.
/// </summary>
public interface IVisualRenderer
{
    string Name { get; }

    RendererCapabilities Capabilities { get; }

    FrameworkElement Render(RenderRequest request);
}
