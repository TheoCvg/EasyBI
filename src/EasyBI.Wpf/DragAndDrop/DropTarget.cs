using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Turns an element into a drop target whose decisions belong to an <see cref="IDropHandler"/> (usually the
/// view-model). On an <see cref="ItemsControl"/>, the drop position among the items is computed and shown by an
/// insertion line. While an accepted drag is over the target, <see cref="IsDragOverProperty"/> is true (for style
/// triggers); while a refused one is, <see cref="RefusalProperty"/> holds the reason given by the handler, also shown
/// under the target. A scrollable target or ancestor scrolls when the pointer nears its edges.
/// <code>&lt;ItemsControl bi:DropTarget.Handler="{Binding}" bi:DropTarget.Key="Axis" /&gt;</code>
/// Being attached properties, they can be set by a style.
/// </summary>
public static class DropTarget
{
    /// <summary>Style of the insertion line (a <c>Border</c>).</summary>
    public static ComponentResourceKey InsertionLineStyleKey { get; } = new(typeof(DropTarget), "InsertionLine");

    /// <summary>Style of the message explaining a refused drop (a <c>ContentControl</c> whose content is the reason).</summary>
    public static ComponentResourceKey RefusalStyleKey { get; } = new(typeof(DropTarget), "Refusal");

    public static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler", typeof(IDropHandler), typeof(DropTarget), new PropertyMetadata(null, OnHandlerChanged));

    /// <summary>Passed to the handler as <see cref="DropInfo.Key"/>, to tell targets sharing one handler apart.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(object), typeof(DropTarget), new PropertyMetadata(null));

    /// <summary>How the items are laid out: lines read left to right (default) or a vertical stack.</summary>
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.RegisterAttached(
        "Orientation", typeof(Orientation), typeof(DropTarget), new PropertyMetadata(Orientation.Horizontal));

    private static readonly DependencyPropertyKey IsDragOverPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsDragOver", typeof(bool), typeof(DropTarget), new PropertyMetadata(false));

    /// <summary>True while a drag the handler accepts is over the element.</summary>
    public static readonly DependencyProperty IsDragOverProperty = IsDragOverPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey RefusalPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Refusal", typeof(string), typeof(DropTarget), new PropertyMetadata(null));

    /// <summary>
    /// While a drag the handler refuses is over the element, why (<see cref="IDropHandler.RefusalReason"/>); null otherwise.
    /// </summary>
    public static readonly DependencyProperty RefusalProperty = RefusalPropertyKey.DependencyProperty;

    public static IDropHandler? GetHandler(DependencyObject element) => (IDropHandler?)element.GetValue(HandlerProperty);
    public static void SetHandler(DependencyObject element, IDropHandler? value) => element.SetValue(HandlerProperty, value);
    public static object? GetKey(DependencyObject element) => element.GetValue(KeyProperty);
    public static void SetKey(DependencyObject element, object? value) => element.SetValue(KeyProperty, value);
    public static Orientation GetOrientation(DependencyObject element) => (Orientation)element.GetValue(OrientationProperty);
    public static void SetOrientation(DependencyObject element, Orientation value) => element.SetValue(OrientationProperty, value);
    public static bool GetIsDragOver(DependencyObject element) => (bool)element.GetValue(IsDragOverProperty);
    public static string? GetRefusal(DependencyObject element) => (string?)element.GetValue(RefusalProperty);

    // One drag at a time: the target under the pointer owns the insertion line and the scrolling
    private static UIElement? _current;
    private static FloatingAdorner? _line;
    private static FloatingAdorner? _refusal;
    private static string? _refusalText;
    private static AutoScroller? _scroller;
    private static ScrollViewer? _scrolled;

    /// <summary>The bounds of the items of a list in its own coordinates, in display order (empty for items not shown).</summary>
    public static IReadOnlyList<Rect> ItemBounds(ItemsControl items)
    {
        var bounds = new Rect[items.Items.Count];
        for (var i = 0; i < bounds.Length; i++)
        {
            bounds[i] = items.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { IsVisible: true } container
                && container.IsDescendantOf(items)
                ? container.TransformToAncestor(items).TransformBounds(new Rect(container.RenderSize))
                : Rect.Empty;
        }
        return bounds;
    }

    private static void OnHandlerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.DragEnter -= OnDragOver;
        element.DragOver -= OnDragOver;
        element.DragLeave -= OnDragLeave;
        element.Drop -= OnDrop;
        if (e.NewValue is null) return;
        element.AllowDrop = true;
        element.DragEnter += OnDragOver;
        element.DragOver += OnDragOver;
        element.DragLeave += OnDragLeave;
        element.Drop += OnDrop;
    }

    private static (DropInfo Drop, IReadOnlyList<Rect> Items) Describe(UIElement target, DragEventArgs e)
    {
        var items = target is ItemsControl list ? ItemBounds(list) : [];
        var index = target is ItemsControl ? InsertionIndex.At(items, e.GetPosition(target), GetOrientation(target)) : 0;
        var drop = new DropInfo
        {
            Item = DragSource.ItemOf(e.Data),
            Key = GetKey(target),
            Index = index,
            Data = e.Data,
            AllowedEffects = e.AllowedEffects,
        };
        return (drop, items);
    }

    private static DragDropEffects Accept(IDropHandler handler, DropInfo drop) => handler.DragOver(drop) & drop.AllowedEffects;

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var target = (UIElement)sender;
        // A nested target already answered
        if (e.Handled || GetHandler(target) is not { } handler) return;
        e.Handled = true;
        if (_current != target) Leave();
        _current = target;

        var (drop, items) = Describe(target, e);
        e.Effects = Accept(handler, drop);
        var accepted = e.Effects != DragDropEffects.None;
        target.SetValue(IsDragOverPropertyKey, accepted);
        ShowLine(target, accepted ? InsertionIndex.IndicatorBounds(items, drop.Index, GetOrientation(target)) : Rect.Empty);
        ShowRefusal(target, accepted ? null : handler.RefusalReason(drop));
        Scroll(target, e);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        // Moving onto a child of the target also raises DragLeave, immediately followed by a DragOver that shows the
        // drop again; a cancelled drag (Escape) only raises DragLeave
        if (sender == _current) Leave();
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var target = (UIElement)sender;
        if (e.Handled || GetHandler(target) is not { } handler) return;
        e.Handled = true;
        var (drop, _) = Describe(target, e);
        Leave();
        e.Effects = Accept(handler, drop);
        if (e.Effects != DragDropEffects.None) handler.Drop(drop);
    }

    private static void Leave()
    {
        _current?.ClearValue(IsDragOverPropertyKey);
        if (_current is not null) ShowRefusal(_current, null);
        _current = null;
        _line?.Remove();
        _line = null;
        _scroller?.Stop();
    }

    private static void ShowLine(UIElement target, Rect bounds)
    {
        if (bounds.IsEmpty)
        {
            _line?.Remove();
            _line = null;
            return;
        }
        if (_line is null)
        {
            var line = new Border { IsHitTestVisible = false };
            line.SetResourceReference(FrameworkElement.StyleProperty, InsertionLineStyleKey);
            _line = FloatingAdorner.Show(target, line);
        }
        _line?.Place(bounds.TopLeft, bounds.Size);
    }

    // The reason, under the target
    private static void ShowRefusal(UIElement target, string? reason)
    {
        if (reason is null) target.ClearValue(RefusalPropertyKey);
        else target.SetValue(RefusalPropertyKey, reason);
        if (reason == _refusalText && (_refusal is not null || reason is null)) return;
        _refusal?.Remove();
        _refusal = null;
        _refusalText = reason;
        if (reason is null) return;
        var message = new ContentControl { Content = reason, IsHitTestVisible = false, Focusable = false };
        message.SetResourceReference(FrameworkElement.StyleProperty, RefusalStyleKey);
        _refusal = FloatingAdorner.Show(target, message);
        _refusal?.Place(new Point(0, target.RenderSize.Height + 4));
    }

    private static void Scroll(UIElement target, DragEventArgs e)
    {
        if (ScrollViewerOf(target) is not { } viewer)
        {
            _scroller?.Stop();
            return;
        }
        if (viewer != _scrolled)
        {
            _scroller?.Stop();
            _scroller = AutoScroller.For(viewer);
            _scrolled = viewer;
        }
        _scroller!.Update(AutoScroller.ViewportOf(viewer, viewer), e.GetPosition(viewer),
            horizontal: viewer.ScrollableWidth > 0, vertical: viewer.ScrollableHeight > 0);
    }

    /// <summary>The scroll viewer of the target's template (a list box), or of an ancestor.</summary>
    private static ScrollViewer? ScrollViewerOf(UIElement target)
    {
        if (target is ItemsControl && Templated(target) is { } own) return own;
        for (var e = VisualTreeHelper.GetParent(target); e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is ScrollViewer viewer) return viewer;
        return null;
    }

    // Breadth-first in the template only: the items' own scroll viewers do not count
    private static ScrollViewer? Templated(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ScrollViewer viewer) return viewer;
                if (child is not ItemsPresenter) queue.Enqueue(child);
            }
        }
        return null;
    }
}
