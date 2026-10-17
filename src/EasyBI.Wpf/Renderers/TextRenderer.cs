using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Renderers;

/// <summary>Built-in renderer of <see cref="MarkType.Text"/> visuals.</summary>
public sealed class TextRenderer : IVisualRenderer
{
    public string Name => "EasyBI Text";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Text });

    public FrameworkElement Render(RenderRequest request)
    {
        var text = request.Visual.Text ?? new TextContent();
        return new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontWeight = text.IsBold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = text.Alignment switch
            {
                TextAlign.Center => TextAlignment.Center,
                TextAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            TextWrapping = TextWrapping.Wrap,
        };
    }
}
