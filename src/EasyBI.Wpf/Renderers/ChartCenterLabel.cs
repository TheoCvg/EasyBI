using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// A chart with a text in the middle of its plot area, such as the total in the hole of a donut. The connector tells
/// where its plot area is (<c>plotArea</c>, in the chart's coordinates, null while unknown); the text follows it each
/// time the chart is laid out or <see cref="Place"/> is called.
/// </summary>
public sealed class ChartCenterLabel : Grid
{
    private readonly FrameworkElement _chart;
    private readonly Func<Rect?> _plotArea;
    private readonly StackPanel _label;
    private readonly Canvas _layer = new() { IsHitTestVisible = false };

    public ChartCenterLabel(FrameworkElement chart, Func<Rect?> plotArea, string text, string caption)
    {
        _chart = chart;
        _plotArea = plotArea;
        Value = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.ForegroundBrushKey);
        Caption = new TextBlock { Text = caption, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);
        _label = new StackPanel { Children = { Value, Caption } };
        _layer.Children.Add(_label);
        Children.Add(chart);
        Children.Add(_layer);
        chart.LayoutUpdated += (_, _) => Place();
    }

    /// <summary>The text in the middle (the total).</summary>
    public TextBlock Value { get; }

    /// <summary>The line under it (what the total is).</summary>
    public TextBlock Caption { get; }

    /// <summary>Centers the text on the plot area, its size following the area's (a donut's hole).</summary>
    public void Place()
    {
        if (_plotArea() is not { Width: > 0, Height: > 0 } area)
        {
            _label.Visibility = Visibility.Hidden;
            return;
        }
        // About a third of a donut's hole, which is half of the pie
        var fontSize = Compat.Clamp(Math.Min(area.Width, area.Height) / 13, 11, 26);
        if (Value.FontSize != fontSize) Value.FontSize = fontSize;
        _label.Visibility = Visibility.Visible;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;
        var origin = _chart.TranslatePoint(new Point(), this);
        var left = origin.X + area.X + (area.Width - size.Width) / 2;
        var top = origin.Y + area.Y + (area.Height - size.Height) / 2;
        // Only move it when it moved: setting a position lays the chart out again
        if (Canvas.GetLeft(_label) is var x && !double.IsNaN(x) && Math.Abs(x - left) < 0.5 && Math.Abs(Canvas.GetTop(_label) - top) < 0.5) return;
        Canvas.SetLeft(_label, left);
        Canvas.SetTop(_label, top);
    }
}
