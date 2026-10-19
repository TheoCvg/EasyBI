using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Renderers;

/// <summary>Built-in renderer: one card per measure. No charting library needed.</summary>
public sealed class KpiRenderer : IVisualRenderer
{
    public string Name => "EasyBI KPI";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Kpi });

    public FrameworkElement Render(RenderRequest request)
    {
        var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < request.Chart.Series.Count; i++)
        {
            var series = request.Chart.Series[i];
            var value = series.Values.FirstOrDefault();
            var accent = (Color)ColorConverter.ConvertFromString(request.Theme.ColorAt(i));
            var card = new StackPanel { Margin = new Thickness(24, 12, 24, 12) };
            var text = new TextBlock
            {
                Text = value is null ? "—" : ValueFormatter.Format(value.Value, series.Format),
                FontSize = 40,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            // A change since the previous year shows its trend: up in the measures' color, down in the error color
            var time = request.Visual.Values.ElementAtOrDefault(i)?.Time;
            if (time is TimeCalculation.ChangeFromPreviousYear or TimeCalculation.PercentChangeFromPreviousYear && value is { } change && change != 0)
            {
                text.Text = (change > 0 ? "▲ " : "▼ ") + text.Text;
                text.Themed(TextBlock.ForegroundProperty, change > 0 ? EasyBITheme.MeasureBrushKey : EasyBITheme.ErrorBrushKey);
            }
            card.Children.Add(text);
            card.Children.Add(new TextBlock
            {
                Text = series.Name,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
            }.Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey));
            panel.Children.Add(card);
        }
        return new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    }
}
