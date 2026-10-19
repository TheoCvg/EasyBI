using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Places an overlay element: returns its bounds in panel coordinates, or null to hide it.
/// <paramref name="desiredSize"/> is the size the element asks for.
/// </summary>
public delegate Rect? OverlayPlacement(DashboardOverlay overlay, Size desiredSize);

/// <summary>
/// Layer above a <see cref="DashboardView"/>'s panel where behaviors put their controls (grips, "+" buttons, toolbars...).
/// It tracks which item and band the pointer is over, and re-places every element when the pointer moves or the panel
/// is arranged. One overlay per view, shared by all behaviors (<see cref="For"/>).
/// </summary>
public sealed class DashboardOverlay : Adorner
{
    /// <summary>Style of the round "+" buttons (a <c>Button</c>).</summary>
    public static ComponentResourceKey PlusButtonStyleKey { get; } = new(typeof(DashboardOverlay), "PlusButton");

    /// <summary>Style of the small toolbar buttons (a <c>Button</c>).</summary>
    public static ComponentResourceKey IconButtonStyleKey { get; } = new(typeof(DashboardOverlay), "IconButton");

    /// <summary>Style of the grip used to move an item (a <c>ContentControl</c>).</summary>
    public static ComponentResourceKey GripStyleKey { get; } = new(typeof(DashboardOverlay), "Grip");

    /// <summary>Style of the handle used to resize an item (a <c>ContentControl</c>).</summary>
    public static ComponentResourceKey ResizeGripStyleKey { get; } = new(typeof(DashboardOverlay), "ResizeGrip");

    /// <summary>Style of the placeholder filling a free space, which adds an item there when clicked (a <c>Button</c>).</summary>
    public static ComponentResourceKey FreeSpaceStyleKey { get; } = new(typeof(DashboardOverlay), "FreeSpace");

    /// <summary>Style of the place where a moved item will land (a <c>ContentControl</c>, sized like the item).</summary>
    public static ComponentResourceKey DropPlaceholderStyleKey { get; } = new(typeof(DashboardOverlay), "DropPlaceholder");

    /// <summary>Style of the picture of a moved item following the pointer (a <c>ContentControl</c> holding an <c>Image</c>).</summary>
    public static ComponentResourceKey DragGhostStyleKey { get; } = new(typeof(DashboardOverlay), "DragGhost");

    /// <summary>Distance around an item within which it stays hovered (its "+" buttons sit on its edges).</summary>
    public const double HoverMargin = 14;

    /// <summary>
    /// True (default): an element hidden by its placement stays while the pointer is over it, so that it can be reached
    /// and clicked. Set it to false for elements that must disappear at once (a placeholder replaced by an item).
    /// </summary>
    public static readonly DependencyProperty StaysUnderPointerProperty = DependencyProperty.RegisterAttached(
        "StaysUnderPointer", typeof(bool), typeof(DashboardOverlay), new PropertyMetadata(true));

    public static bool GetStaysUnderPointer(DependencyObject element) => (bool)element.GetValue(StaysUnderPointerProperty);
    public static void SetStaysUnderPointer(DependencyObject element, bool value) => element.SetValue(StaysUnderPointerProperty, value);

    private static readonly DependencyProperty OverlayProperty = DependencyProperty.RegisterAttached(
        "Overlay", typeof(DashboardOverlay), typeof(DashboardOverlay), new PropertyMetadata(null));

    private static readonly Point Hidden = new(-100000, -100000);

    private readonly VisualCollection _visuals;
    private readonly List<(FrameworkElement Element, OverlayPlacement Placement)> _elements = [];
    private readonly Dictionary<FrameworkElement, Rect> _shown = [];
    private UIElement? _lockedItem;

    private DashboardOverlay(DashboardView view) : base(view.Panel)
    {
        View = view;
        _visuals = new VisualCollection(this);
        view.PreviewMouseMove += (_, e) => Track(e.GetPosition(Panel));
        view.MouseLeave += (_, _) => Track(null);
        view.Panel.Arranged += (_, _) => InvalidateArrange();
        DependencyPropertyDescriptor.FromProperty(DashboardView.IsReadOnlyProperty, typeof(DashboardView))
            .AddValueChanged(view, (_, _) => InvalidateArrange());
    }

    /// <summary>Raised when <see cref="HoveredElement"/> or <see cref="HoveredBand"/> changes.</summary>
    public event EventHandler? HoverChanged;

    public DashboardView View { get; }

    public DashboardPanel Panel => View.Panel;

    /// <summary>The child of the panel under the pointer (or locked by a gesture).</summary>
    public UIElement? HoveredElement { get; private set; }

    public DashboardItem? HoveredItem => HoveredElement as DashboardItem;

    /// <summary>Bounds of <see cref="HoveredElement"/> in panel coordinates.</summary>
    public Rect HoveredBounds => HoveredElement is { } element ? Panel.GetItemBounds(element) : Rect.Empty;

    /// <summary>The band under the pointer (including the spacing below it and the left margin), or -1.</summary>
    public int HoveredBand { get; private set; } = -1;

    /// <summary>The pointer in panel coordinates, or null when it is outside the view.</summary>
    public Point? Pointer { get; private set; }

    /// <summary>True while a gesture keeps the hover on one item (see <see cref="Lock"/>).</summary>
    public bool IsLocked => _lockedItem is not null;

    /// <summary>The overlay of a view, created on first use.</summary>
    public static DashboardOverlay For(DashboardView view)
    {
        if (view.GetValue(OverlayProperty) is DashboardOverlay overlay) return overlay;
        overlay = new DashboardOverlay(view);
        view.SetValue(OverlayProperty, overlay);
        if (!overlay.TryShow()) view.Loaded += (_, _) => overlay.TryShow();
        return overlay;
    }

    /// <summary>Adds an element placed by <paramref name="placement"/>; it gets the view's resources and routes its commands to the view.</summary>
    public void Add(FrameworkElement element, OverlayPlacement placement)
    {
        _elements.Add((element, placement));
        _visuals.Add(element);
        AddLogicalChild(element);
        InvalidateMeasure();
    }

    public void Remove(FrameworkElement element)
    {
        if (_elements.RemoveAll(e => e.Element == element) == 0) return;
        _visuals.Remove(element);
        _shown.Remove(element);
        RemoveLogicalChild(element);
        InvalidateMeasure();
    }

    /// <summary>Keeps the hover on an item during a gesture (drag, resize), whatever the pointer passes over.</summary>
    public void Lock(UIElement item)
    {
        _lockedItem = item;
        SetHover(item, Panel.BandIndexOf(item));
    }

    public void Unlock()
    {
        _lockedItem = null;
        Track(Pointer);
    }

    /// <summary>Places the elements again (after a change their placement depends on).</summary>
    public void Refresh() => InvalidateArrange();

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override System.Collections.IEnumerator LogicalChildren => _elements.Select(e => e.Element).GetEnumerator();

    protected override Size MeasureOverride(Size constraint)
    {
        foreach (var (element, _) in _elements) element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (element, placement) in _elements)
        {
            var rect = placement(this, element.DesiredSize);
            // An element under the pointer stays where it is, so that it can be reached and clicked
            if (rect is null && element.IsMouseOver && GetStaysUnderPointer(element) && _shown.TryGetValue(element, out var shown)) rect = shown;
            if (rect is { } r) _shown[element] = r;
            else _shown.Remove(element);
            // Hidden elements are moved far away rather than collapsed: changing visibility here would loop the layout
            element.Arrange(rect ?? new Rect(Hidden, element.DesiredSize));
        }
        return finalSize;
    }

    // Only the overlay's elements take the pointer: the items below stay clickable
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    private bool TryShow()
    {
        if (Parent is not null) return true;
        if (AdornerLayer.GetAdornerLayer(Panel) is not { } layer) return false;
        layer.Add(this);
        return true;
    }

    private void Track(Point? pointer)
    {
        Pointer = pointer;
        if (_lockedItem is null)
        {
            UIElement? hovered = null;
            var band = -1;
            if (pointer is { } p)
            {
                hovered = ItemAt(p);
                band = BandAt(p);
            }
            SetHover(hovered, band);
        }
        InvalidateArrange();
    }

    private void SetHover(UIElement? element, int band)
    {
        if (element == HoveredElement && band == HoveredBand) return;
        HoveredElement = element;
        HoveredBand = band;
        HoverChanged?.Invoke(this, EventArgs.Empty);
    }

    private UIElement? ItemAt(Point p)
    {
        UIElement? near = null;
        foreach (UIElement child in Panel.Children)
        {
            var bounds = Panel.GetItemBounds(child);
            if (bounds.IsEmpty || child.Visibility != Visibility.Visible) continue;
            if (bounds.Contains(p)) return child;
            var inflated = bounds;
            inflated.Inflate(HoverMargin, HoverMargin);
            if (inflated.Contains(p) && (near is null || child == HoveredElement)) near = child;
        }
        return near;
    }

    private int BandAt(Point p)
    {
        if (p.X > Panel.ActualWidth + HoverMargin) return -1;
        for (var i = 0; i < Panel.BandCount; i++)
        {
            var bounds = Panel.GetBandBounds(i);
            var bottom = bounds.Bottom + (i < Panel.BandCount - 1 ? Panel.BandSpacing : Panel.Margin.Bottom);
            if (!bounds.IsEmpty && p.Y >= bounds.Top - (i == 0 ? Panel.Margin.Top : 0) && p.Y < bottom) return i;
        }
        return -1;
    }
}
