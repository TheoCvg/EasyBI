using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Shows one element above the adorned element, at bounds given in its coordinates (a drag preview, an insertion
/// line). It never takes the pointer, so that drop targets below keep receiving the drag.
/// </summary>
internal sealed class FloatingAdorner : Adorner
{
    private readonly FrameworkElement _child;
    private readonly AdornerLayer _layer;
    private Point _position;
    private Size? _size;

    private FloatingAdorner(UIElement adorned, AdornerLayer layer, FrameworkElement child) : base(adorned)
    {
        _layer = layer;
        _child = child;
        IsHitTestVisible = false;
        AddVisualChild(child);
        AddLogicalChild(child);
        layer.Add(this);
    }

    /// <summary>Shows <paramref name="child"/> above <paramref name="adorned"/>; null when it has no adorner layer.</summary>
    public static FloatingAdorner? Show(UIElement adorned, FrameworkElement child) =>
        AdornerLayer.GetAdornerLayer(adorned) is { } layer ? new FloatingAdorner(adorned, layer, child) : null;

    /// <summary>Moves the element; <paramref name="size"/> null keeps the size it asks for.</summary>
    public void Place(Point position, Size? size = null)
    {
        if (_position == position && _size == size) return;
        _position = position;
        _size = size;
        InvalidateArrange();
    }

    public void Remove()
    {
        _layer.Remove(this);
        RemoveVisualChild(_child);
        RemoveLogicalChild(_child);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _child;

    protected override System.Collections.IEnumerator LogicalChildren => new[] { _child }.GetEnumerator();

    protected override Size MeasureOverride(Size constraint)
    {
        _child.Measure(_size ?? new Size(double.PositiveInfinity, double.PositiveInfinity));
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _child.Arrange(new Rect(_position, _size ?? _child.DesiredSize));
        return finalSize;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;
}
