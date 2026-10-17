using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Scrolls while a dragged pointer stays near (or beyond) the edges of a viewport, faster the closer it gets, and keeps
/// scrolling while the pointer does not move. The owner tells it where the pointer is (<see cref="Update"/>) and how
/// to scroll (the delegate given to the constructor); <see cref="Scrolled"/> lets a gesture recompute its target.
/// </summary>
public sealed class AutoScroller
{
    private readonly Func<Vector, bool> _scrollBy;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private Vector _velocity;

    /// <param name="scrollBy">Scrolls by an offset (pixels; positive goes right and down); returns whether anything moved.</param>
    public AutoScroller(Func<Vector, bool> scrollBy)
    {
        _scrollBy = scrollBy;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += OnTick;
    }

    /// <summary>Raised after each step that scrolled something.</summary>
    public event EventHandler? Scrolled;

    /// <summary>Depth of the zone along each edge where scrolling starts, in device-independent pixels.</summary>
    public double EdgeSize { get; set; } = 40;

    /// <summary>Speed reached at the edge and beyond, in pixels per second.</summary>
    public double MaxSpeed { get; set; } = 1200;

    /// <summary>True while the pointer is in a scrolling zone.</summary>
    public bool IsScrolling => _timer.IsEnabled;

    /// <summary>A scroller for a <see cref="ScrollViewer"/>.</summary>
    public static AutoScroller For(ScrollViewer viewer) => new(offset =>
    {
        var (h, v) = (viewer.HorizontalOffset, viewer.VerticalOffset);
        if (offset.X != 0) viewer.ScrollToHorizontalOffset(h + offset.X);
        if (offset.Y != 0) viewer.ScrollToVerticalOffset(v + offset.Y);
        // Offsets are clamped by the viewer: nothing moves at the ends
        return Math.Clamp(h + offset.X, 0, viewer.ScrollableWidth) != h || Math.Clamp(v + offset.Y, 0, viewer.ScrollableHeight) != v;
    });

    /// <summary>The visible part of a <see cref="ScrollViewer"/>'s content, in the coordinates of <paramref name="element"/>.</summary>
    public static Rect ViewportOf(ScrollViewer viewer, Visual element)
    {
        var origin = viewer.TranslatePoint(new Point(), (UIElement)element);
        return new Rect(origin, new Size(viewer.ViewportWidth, viewer.ViewportHeight));
    }

    /// <summary>
    /// Scrolling speed (pixels per second) for a pointer in <paramref name="viewport"/>: zero in the middle, growing
    /// linearly across the edge zones, <paramref name="maxSpeed"/> at and beyond the edges.
    /// </summary>
    public static Vector VelocityAt(Rect viewport, Point pointer, double edgeSize, double maxSpeed)
    {
        if (viewport.IsEmpty) return default;
        // Zones never overlap in a small viewport
        var edge = Math.Max(1, Math.Min(edgeSize, Math.Min(viewport.Width, viewport.Height) / 3));
        return new Vector(Axis(pointer.X, viewport.Left, viewport.Right), Axis(pointer.Y, viewport.Top, viewport.Bottom));

        double Axis(double p, double start, double end)
        {
            if (p < start + edge) return -maxSpeed * Math.Min(1, (start + edge - p) / edge);
            if (p > end - edge) return maxSpeed * Math.Min(1, (p - (end - edge)) / edge);
            return 0;
        }
    }

    /// <summary>The pointer moved: starts, adjusts or stops scrolling.</summary>
    /// <param name="viewport">The visible area, in the same coordinates as <paramref name="pointer"/>.</param>
    /// <param name="pointer">The dragged pointer.</param>
    /// <param name="horizontal">Whether horizontal scrolling is wanted.</param>
    /// <param name="vertical">Whether vertical scrolling is wanted.</param>
    public void Update(Rect viewport, Point pointer, bool horizontal = true, bool vertical = true)
    {
        var velocity = VelocityAt(viewport, pointer, EdgeSize, MaxSpeed);
        _velocity = new Vector(horizontal ? velocity.X : 0, vertical ? velocity.Y : 0);
        if (_velocity == default)
        {
            Stop();
        }
        else if (!_timer.IsEnabled)
        {
            _clock.Restart();
            _timer.Start();
        }
    }

    public void Stop()
    {
        _velocity = default;
        _timer.Stop();
        _clock.Reset();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Elapsed time rather than the interval: ticks are late when the UI is busy
        var seconds = Math.Min(0.1, _clock.Elapsed.TotalSeconds);
        _clock.Restart();
        if (_scrollBy(_velocity * seconds)) Scrolled?.Invoke(this, EventArgs.Empty);
    }
}
