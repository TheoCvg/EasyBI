using System.Windows;
using System.Windows.Input;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// A press-drag-release gesture on a handle, in panel coordinates. It starts once the pointer has moved beyond the
/// system drag distance, and always ends exactly once: on release (committed), on Escape or on a lost capture
/// (cancelled). Behaviors open a <see cref="DashboardEdit"/> in <c>started</c> and close it in <c>ended</c>.
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
        if (_handle.IsMouseCaptured) _handle.ReleaseMouseCapture();
        if (started) _ended(commit);
    }
}
