using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// Built-in renderer of a <see cref="VisualType.Gauge"/>: a half circle from the minimum to the maximum
/// (<see cref="GaugeScale"/>), filled up to the value, a mark at the target, the value and its measure below. The
/// measure's format colors the value (a rule's color) and adds its icon.
/// </summary>
public sealed class GaugeRenderer : IVisualRenderer
{
    // The drawing, in a fixed box scaled to the room: an arc of radius 96 around (120, 120)
    private const double Width = 240, Height = 170, CenterX = 120, CenterY = 120, Radius = 96, Thickness = 20;

    public string Name => "EasyBI Gauge";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<VisualType> { VisualType.Gauge });

    public FrameworkElement Render(RenderRequest request)
    {
        var series = request.Chart.Series.FirstOrDefault();
        var value = series?.Values.FirstOrDefault();
        var settings = request.Visual.Gauge;
        var (minimum, maximum) = GaugeScale.Of(settings, value);
        var format = series?.Format;
        var style = ValueStyler.For(request.Visual.Values.FirstOrDefault(), [value], request.Theme)?.Style(value);
        var color = FormattedValue.Brush(style?.Foreground) ?? FormattedValue.Brush(request.Theme.ColorAt(0))!;

        var canvas = new Canvas { Width = Width, Height = Height };
        canvas.Children.Add(Arc(0, 1).Themed(Shape.StrokeProperty, EasyBITheme.GridLineBrushKey));
        if (value is { } current)
        {
            var arc = Arc(0, GaugeScale.Position(current, minimum, maximum));
            arc.Stroke = color;
            canvas.Children.Add(arc);
        }
        if (settings?.Target is { } target)
            canvas.Children.Add(Tick(GaugeScale.Position(target, minimum, maximum)).Themed(Shape.StrokeProperty, EasyBITheme.ForegroundBrushKey));
        canvas.Children.Add(Label(ValueFormatter.Format(minimum, format), CenterX - Radius, HorizontalAlignment.Center));
        canvas.Children.Add(Label(ValueFormatter.Format(maximum, format), CenterX + Radius, HorizontalAlignment.Center));

        var text = new TextBlock { FontSize = 26, FontWeight = style?.IsBold == true ? FontWeights.Bold : FontWeights.SemiBold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center };
        if (style?.IsItalic == true) text.FontStyle = FontStyles.Italic;
        if (style?.Icon is { } icon) text.Inlines.Add(new Run(icon + " ") { Foreground = FormattedValue.Brush(style.IconColor) });
        text.Inlines.Add(new Run(value is { } shown ? ValueFormatter.Format(shown, format) : "—"));
        // A long number shrinks to stay inside the arc, narrower than the inner circle where the text stands
        var center = new StackPanel { Width = Width, Children = { new Viewbox { Child = text, MaxWidth = 2 * (Radius - Thickness) - 32, StretchDirection = StretchDirection.DownOnly } } };
        if (settings?.Target is { } goal)
        {
            center.Children.Add(new TextBlock
            {
                Text = Strings.Format(Strings.GaugeTargetLabel, ValueFormatter.Format(goal, format)),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
            }.Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey));
        }
        Canvas.SetTop(center, CenterY - 40);
        canvas.Children.Add(center);
        var caption = new TextBlock { Text = series?.Name ?? "", FontSize = 13, Width = Width, TextAlignment = TextAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);
        Canvas.SetTop(caption, CenterY + 26);
        canvas.Children.Add(caption);

        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
    }

    // The point of the arc at a position (0: the left end, 1: the right end, through the top), at a distance from the center
    private static Point At(double position, double radius)
    {
        var angle = Math.PI * (1 - position);
        return new Point(CenterX + radius * Math.Cos(angle), CenterY - radius * Math.Sin(angle));
    }

    private static Path Arc(double from, double to)
    {
        var figure = new PathFigure { StartPoint = At(from, Radius), IsFilled = false };
        figure.Segments.Add(new ArcSegment(At(to, Radius), new Size(Radius, Radius), 0, false, SweepDirection.Clockwise, true));
        return new Path { Data = new PathGeometry { Figures = { figure } }, StrokeThickness = Thickness };
    }

    private static Line Tick(double position)
    {
        var (inner, outer) = (At(position, Radius - Thickness / 2 - 4), At(position, Radius + Thickness / 2 + 4));
        return new Line { X1 = inner.X, Y1 = inner.Y, X2 = outer.X, Y2 = outer.Y, StrokeThickness = 3 };
    }

    // A value under an end of the arc
    private static TextBlock Label(string text, double x, HorizontalAlignment alignment)
    {
        var label = new TextBlock { Text = text, FontSize = 11, Width = 80, TextAlignment = TextAlignment.Center, HorizontalAlignment = alignment }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);
        Canvas.SetLeft(label, x - 40);
        Canvas.SetTop(label, CenterY + 6);
        return label;
    }
}
