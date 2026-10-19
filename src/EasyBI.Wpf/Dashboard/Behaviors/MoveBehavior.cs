using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Moves items with a grip shown at their top-left corner. While dragging, a picture of the item follows the pointer
/// and a placeholder shows where it will land, the other items making room live, across bands too:
/// <list type="bullet">
/// <item>near an edge of an item of a grid band, it goes next to that item, as the "+" buttons add one (see
/// <see cref="DashboardLayoutEngine.AddNextTo"/>);</item>
/// <item>between two lines of a grid band (or above the first, below the last), it becomes a line of its own;</item>
/// <item>over a free space, it fills it;</item>
/// <item>in a scrolling or wrapping band, it goes between the items under the pointer.</item>
/// </list>
/// The moved item becomes greedy in width. Near the edges of the view (and of a scrolling band), the dashboard
/// scrolls. Escape cancels. There is no global edit mode: clicking an item elsewhere keeps cross-filtering. The
/// picture and the placeholder are restyled with <see cref="DashboardOverlay.DragGhostStyleKey"/> and
/// <see cref="DashboardOverlay.DropPlaceholderStyleKey"/>.
/// </summary>
public sealed class MoveBehavior : DashboardBehavior
{
    private DragGesture? _gesture;
    private ContentControl? _grip;
    private DashboardItem? _pressed;
    private DashboardItem? _item;
    private Vector _grab;
    private Point _pointer;
    private Size _size;
    private DashboardEdit? _edit;
    private DashboardLayoutSnapshot? _snapshot;
    private DashboardPanelGeometry? _before;
    private DashboardDropTarget? _target;
    private bool _nextTo;

    /// <summary>Distance from an item's edge within which the moved item goes next to it (at most a quarter of the item).</summary>
    public double EdgeZone { get; set; } = 40;

    protected override void OnAttach()
    {
        // Added first, so that the grip and the other behaviors' controls stay above them
        AddToOverlay(Create(DashboardOverlay.DropPlaceholderStyleKey), PlacePlaceholder);
        var ghost = AddToOverlay(Create(DashboardOverlay.DragGhostStyleKey), PlaceGhost);
        _grip = new ContentControl { Focusable = false };
        _grip.SetResourceReference(FrameworkElement.StyleProperty, DashboardOverlay.GripStyleKey);
        _gesture = new DragGesture(View, _grip, CanBegin, start => OnStarted(start, ghost), OnMoved, commit => OnEnded(commit, ghost)) { ScrollsBands = true };
        AddToOverlay(_grip, PlaceGrip);
    }

    protected override void OnDetach() => _gesture?.Cancel();

    private static ContentControl Create(ComponentResourceKey styleKey)
    {
        var element = new ContentControl { Focusable = false, IsHitTestVisible = false };
        element.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return element;
    }

    private Rect? PlaceGrip(DashboardOverlay overlay, Size size)
    {
        // Hidden during another gesture (a resize); kept during its own, which needs the mouse capture
        if (!CanEdit || overlay.HoveredItem is null || (overlay.IsLocked && _item is null)) return null;
        var bounds = overlay.HoveredBounds;
        return new Rect(bounds.Left + 4, bounds.Top + 2, size.Width, size.Height);
    }

    private Rect? PlacePlaceholder(DashboardOverlay overlay, Size size) => _item is { } item ? Panel.GetItemBounds(item) : null;

    private Rect? PlaceGhost(DashboardOverlay overlay, Size size) => _item is null ? null : new Rect(_pointer - _grab, _size);

    // The item under the handle when it is pressed: the pointer may leave it before the gesture starts
    private bool CanBegin()
    {
        _pressed = CanEdit ? Overlay.HoveredItem : null;
        return _pressed is not null;
    }

    private void OnStarted(Point start, ContentControl ghost)
    {
        var item = _item = _pressed!;
        var bounds = Panel.GetItemBounds(item);
        // Where the item was grabbed, so that it does not jump under the pointer
        _grab = start - bounds.TopLeft;
        _pointer = start;
        _size = bounds.Size;
        ghost.Content = new Image { Source = Picture(item, bounds.Size), Stretch = Stretch.Fill };
        Overlay.Lock(item);
        _edit = View.BeginEdit();
        _snapshot = DashboardLayout.Capture(Panel);
        _before = Panel.CaptureGeometry();
        _target = null;
        _nextTo = false;
        // The item stays in the layout, where it will land: the placeholder shows that place
        item.Opacity = 0;
        _grip!.Opacity = 0;
    }

    private void OnMoved(Point position, Vector delta)
    {
        if (_item is not { } item || _before is not { } before) return;
        _pointer = position;
        Overlay.Refresh();

        // Targets are found on the layout from before the drag: on the live one, items making room under the pointer
        // would change the target, then change back once it is restored, endlessly. A target also holds while the
        // pointer is over the place it made, since the items around that place moved.
        if (_nextTo && Widened(Panel.GetItemBounds(item)).Contains(position)) return;
        // In the middle of a grid item, the target stays where it is
        if (DashboardDropTarget.Find(before, Panel, position, EdgeZone, item) is not { } target) return;
        Preview(target, () => target.Move(item, keepEmptyBands: true));
    }

    private Rect Widened(Rect bounds)
    {
        if (!bounds.IsEmpty) bounds.Inflate(Panel.Gap / 2, Panel.Gap / 2);
        return bounds;
    }

    private void Preview(DashboardDropTarget target, Action move)
    {
        if (target == _target) return;
        _target = target;
        // Next to an item or as a line, the place made under the pointer holds the target (see OnMoved)
        _nextTo = target.Kind != DashboardDropKind.Flow;
        // Every preview starts from the layout before the drag, so that moves do not accumulate;
        // bands scrolled during the drag stay scrolled
        var offsets = Panel.Bands.Select(b => (Band: b, b.HorizontalOffset)).ToArray();
        _snapshot!.Restore();
        foreach (var (band, offset) in offsets) band.HorizontalOffset = offset;
        move();
    }

    private void OnEnded(bool commit, ContentControl ghost)
    {
        _item?.ClearValue(UIElement.OpacityProperty);
        _grip?.ClearValue(UIElement.OpacityProperty);
        _item = null;
        ghost.Content = null;
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
        _before = null;
        Overlay.Unlock();
    }

    /// <summary>A still picture of the item, for the ghost following the pointer (a live copy would query again).</summary>
    private static BitmapSource? Picture(UIElement item, Size size)
    {
        if (size.Width < 1 || size.Height < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(item);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // Through a brush: rendering the item itself would include its offset in the panel
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new VisualBrush(item), null, new Rect(size));
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }
}
