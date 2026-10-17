using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Resizes items with a handle at their bottom-right corner: by whole cells in a grid band, by pixels (width) and
/// rows (height) in scrolling and wrapping bands. The other items make room live; Escape cancels.
/// </summary>
public sealed class ResizeBehavior : DashboardBehavior
{
    private DragGesture? _gesture;
    private DashboardItem? _pressed;
    private DashboardItem? _item;
    private (int ColumnSpan, int RowSpan, double Width) _initial;
    private DashboardEdit? _edit;
    private DashboardLayoutSnapshot? _snapshot;
    private string? _target;

    protected override void OnAttach()
    {
        var handle = new ContentControl { Focusable = false };
        handle.SetResourceReference(FrameworkElement.StyleProperty, DashboardOverlay.ResizeGripStyleKey);
        _gesture = new DragGesture(View, handle, CanBegin, OnStarted, OnMoved, OnEnded);
        AddToOverlay(handle, Place);
    }

    protected override void OnDetach() => _gesture?.Cancel();

    private Rect? Place(DashboardOverlay overlay, Size size)
    {
        // Hidden during another gesture (a move); kept during its own, which needs the mouse capture
        if (!CanEdit || overlay.HoveredItem is null || (overlay.IsLocked && _item is null)) return null;
        var bounds = overlay.HoveredBounds;
        return new Rect(bounds.Right - size.Width, bounds.Bottom - size.Height, size.Width, size.Height);
    }

    // The item under the handle when it is pressed: the pointer may leave it before the gesture starts
    private bool CanBegin()
    {
        _pressed = CanEdit ? Overlay.HoveredItem : null;
        return _pressed is not null;
    }

    private void OnStarted(Point start)
    {
        var item = _item = _pressed!;
        _initial = (DashboardPanel.GetColumnSpan(item), DashboardPanel.GetRowSpan(item), DashboardPanel.GetItemWidth(item));
        Overlay.Lock(item);
        _edit = View.BeginEdit();
        _snapshot = DashboardLayout.Capture(Panel);
        _target = null;
    }

    private void OnMoved(Point position, Vector delta)
    {
        if (_item is not { } item) return;
        var rowSpan = Math.Max(1, _initial.RowSpan + (int)Math.Round(delta.Y / Panel.RowPitch));

        string target;
        Action resize;
        if (Panel.ModeOf(Panel.BandIndexOf(item)) == BandMode.Fit)
        {
            var columnSpan = Math.Clamp(_initial.ColumnSpan + (int)Math.Round(delta.X / Math.Max(1, Panel.ColumnPitch)), 1, Panel.Columns);
            target = $"{columnSpan}x{rowSpan}";
            resize = () => DashboardLayout.Resize(item, columnSpan, rowSpan);
        }
        else
        {
            // A full-row item keeps the band width: only its height changes
            var width = DashboardPanel.GetIsFullRow(item) ? _initial.Width : Math.Max(DashboardLayoutEngine.MinWidth, Math.Round(_initial.Width + delta.X));
            target = $"{width}x{rowSpan}";
            resize = () => DashboardLayout.ResizeWidth(item, width, rowSpan);
        }

        if (target == _target) return;
        _target = target;
        _snapshot!.Restore();
        resize();
    }

    private void OnEnded(bool commit)
    {
        _item = null;
        if (commit) _edit?.Commit();
        else _edit?.Cancel();
        _edit = null;
        _snapshot = null;
        Overlay.Unlock();
    }
}
