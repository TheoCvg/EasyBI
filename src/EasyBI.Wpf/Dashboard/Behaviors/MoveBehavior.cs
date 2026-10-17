using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Moves items with a grip shown at their top-left corner. While dragging, the item snaps to the grid cell (or the
/// position in a scrolling or wrapping band) under the pointer and the others make room live, across bands too.
/// Escape cancels. There is no global edit mode: clicking an item elsewhere keeps cross-filtering.
/// </summary>
public sealed class MoveBehavior : DashboardBehavior
{
    private DragGesture? _gesture;
    private DashboardItem? _pressed;
    private DashboardItem? _item;
    private Vector _grab;
    private DashboardEdit? _edit;
    private DashboardLayoutSnapshot? _snapshot;
    private string? _target;

    protected override void OnAttach()
    {
        var grip = new ContentControl { Focusable = false };
        grip.SetResourceReference(FrameworkElement.StyleProperty, DashboardOverlay.GripStyleKey);
        _gesture = new DragGesture(View, grip, CanBegin, OnStarted, OnMoved, OnEnded);
        AddToOverlay(grip, Place);
    }

    protected override void OnDetach() => _gesture?.Cancel();

    private Rect? Place(DashboardOverlay overlay, Size size)
    {
        if (!CanEdit || overlay.HoveredItem is null) return null;
        var bounds = overlay.HoveredBounds;
        return new Rect(bounds.Left + 4, bounds.Top + 2, size.Width, size.Height);
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
        // Where the item was grabbed, so that it does not jump under the pointer
        _grab = start - Panel.GetItemBounds(item).TopLeft;
        Overlay.Lock(item);
        _edit = View.BeginEdit();
        _snapshot = DashboardLayout.Capture(Panel);
        _target = null;
        item.Opacity = 0.75;
    }

    private void OnMoved(Point position, Vector delta)
    {
        if (_item is not { } item) return;
        var band = Panel.BandAt(position.Y);
        if (Panel.ModeOf(band) == BandMode.Fit)
        {
            var span = DashboardPanel.GetIsFullRow(item) ? Panel.Columns : DashboardPanel.GetColumnSpan(item);
            var (column, row) = Panel.CellAt(band, position - _grab, span);
            Preview($"{band}:{column}:{row}", () => DashboardLayout.Move(item, band, column, row, keepEmptyBands: true));
        }
        else
        {
            // The item goes before another one: find it in the preview, then insert relative to it in the original band
            var ordered = Panel.ChildrenOf(band);
            var index = Panel.FlowIndexAt(band, position, item);
            var before = index < ordered.Count ? ordered[index] : null;
            Preview($"{band}#{(before is null ? -1 : Panel.Children.IndexOf(before))}", () =>
            {
                var original = Panel.ChildrenOf(band);
                DashboardLayout.MoveTo(item, band, before is null ? original.Count : original.ToList().IndexOf(before), keepEmptyBands: true);
            });
        }
    }

    private void Preview(string target, Action move)
    {
        if (target == _target) return;
        _target = target;
        // Every preview starts from the layout before the drag, so that moves do not accumulate
        _snapshot!.Restore();
        move();
    }

    private void OnEnded(bool commit)
    {
        _item?.ClearValue(UIElement.OpacityProperty);
        _item = null;
        if (commit)
        {
            DashboardLayout.RemoveEmptyBands(Panel);
            _edit?.Commit();
        }
        else
        {
            _edit?.Cancel();
        }
        _edit = null;
        _snapshot = null;
        Overlay.Unlock();
    }
}
