using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// A press-drag-release gesture on a handle, in panel coordinates. It starts once the pointer has moved beyond the
/// system drag distance, and always ends exactly once: on release (committed), on Escape or on a lost capture
/// (cancelled). Behaviors open a <see cref="DashboardEdit"/> in <c>started</c> and close it in <c>ended</c>.
/// Near the edges of the dashboard's scroll viewer the view scrolls (and, with <see cref="ScrollsBands"/>, a
/// <see cref="BandMode.Scroll"/> band near its sides), <c>moved</c> being called again as the content moves.
/// </summary>
public sealed class DragGesture
{
    private readonly DashboardView _view;
    private readonly FrameworkElement _handle;
    private readonly Func<bool> _canBegin;
    private readonly Action<Point> _started;
    private readonly Action<Point, Vector> _moved;
    private readonly Action<bool> _ended;
    private Point _start;
    private bool _pressed;
    private AutoScroller? _scroller;
    private ScrollViewer? _viewer;

    /// <param name="view">The dashboard; it gets the keyboard during the gesture, for Escape.</param>
    /// <param name="handle">The element pressed to start the gesture.</param>
    /// <param name="canBegin">Whether a press may start a gesture now.</param>
    /// <param name="started">Called with the press position once the pointer moved enough.</param>
    /// <param name="moved">Called with the pointer position and its offset from the press position.</param>
    /// <param name="ended">Called after <paramref name="started"/>, with true when the gesture is committed.</param>
    public DragGesture(DashboardView view, FrameworkElement handle, Func<bool> canBegin, Action<Point> started, Action<Point, Vector> moved, Action<bool> ended)
    {
        _view = view;
        _handle = handle;
        _canBegin = canBegin;
        _started = started;
        _moved = moved;
        _ended = ended;
        handle.MouseLeftButtonDown += OnDown;
        handle.MouseMove += OnMove;
        handle.MouseLeftButtonUp += (_, _) => End(commit: true);
        handle.LostMouseCapture += (_, _) => End(commit: false);
    }

    /// <summary>True between the press and the release.</summary>
    public bool IsPressed => _pressed;

    /// <summary>True once the pointer moved enough to start the gesture.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Scrolls the view when the pointer nears its top or bottom (true by default).</summary>
    public bool AutoScroll { get; set; } = true;

    /// <summary>Also scrolls a <see cref="BandMode.Scroll"/> band when the pointer nears its sides (moving items into it).</summary>
    public bool ScrollsBands { get; set; }

    /// <summary>Ends the gesture now (Escape, behavior detached...).</summary>
    public void Cancel() => End(commit: false);

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        End(commit: false);
        if (!_canBegin()) return;
        _start = e.GetPosition(_view.Panel);
        _pressed = true;
        if (!_handle.CaptureMouse())
        {
            _pressed = false;
            return;
        }
        // Escape needs the keyboard; focusing the view (not the handle) does not scroll the dashboard
        if (!_view.IsKeyboardFocusWithin) _view.Focus();
        _view.PreviewKeyDown += OnKey;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(_view.Panel);
        var delta = position - _start;
        if (!IsStarted)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            IsStarted = true;
            _started(_start);
        }
        _moved(position, delta);
        Scroll(position);
    }

    private void Scroll(Point position)
    {
        if (!AutoScroll) return;
        var panel = _view.Panel;
        if (_scroller is null)
        {
            _viewer = ScrollViewerOf(panel);
            _scroller = new AutoScroller(ScrollBy);
            // The content moved under a still pointer: the target changes
            _scroller.Scrolled += (_, _) =>
            {
                if (IsStarted) _moved(Mouse.GetPosition(panel), Mouse.GetPosition(panel) - _start);
            };
        }
        var band = panel.BandAt(position.Y);
        var horizontal = ScrollsBands && band < panel.Bands.Count && panel.Bands[band].CanScroll;
        // Vertically the visible part of the view; horizontally the band, as wide as the panel
        var viewport = _viewer is null ? new Rect(panel.RenderSize) : AutoScroller.ViewportOf(_viewer, panel);
        _scroller.Update(new Rect(0, viewport.Top, panel.ActualWidth, viewport.Height), position, horizontal, vertical: _viewer is not null);
    }

    private bool ScrollBy(Vector offset)
    {
        var panel = _view.Panel;
        var scrolled = false;
        if (offset.Y != 0 && _viewer is { } viewer)
        {
            var before = viewer.VerticalOffset;
            viewer.ScrollToVerticalOffset(before + offset.Y);
            // Applied at the next layout pass: run it now so that the gesture sees the new position
            viewer.UpdateLayout();
            scrolled |= viewer.VerticalOffset != before;
        }
        var band = panel.BandAt(Mouse.GetPosition(panel).Y);
        if (offset.X != 0 && ScrollsBands && band < panel.Bands.Count && panel.Bands[band] is { CanScroll: true } definition)
        {
            var before = definition.HorizontalOffset;
            definition.HorizontalOffset = Compat.Clamp(before + offset.X, 0, definition.ExtentWidth - definition.ViewportWidth);
            panel.UpdateLayout();
            scrolled |= definition.HorizontalOffset != before;
        }
        return scrolled;
    }

    private static ScrollViewer? ScrollViewerOf(DependencyObject element)
    {
        for (var e = VisualTreeHelper.GetParent(element); e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is ScrollViewer viewer) return viewer;
        return null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        End(commit: false);
        e.Handled = true;
    }

    private void End(bool commit)
    {
        if (!_pressed) return;
        _pressed = false;
        _view.PreviewKeyDown -= OnKey;
        var started = IsStarted;
        IsStarted = false;
        _scroller?.Stop();
        if (_handle.IsMouseCaptured) _handle.ReleaseMouseCapture();
        if (started) _ended(commit);
    }
}
