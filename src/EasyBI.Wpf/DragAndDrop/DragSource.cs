using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Makes the items of an <see cref="ItemsControl"/> draggable: pressing an item and moving starts a system
/// drag-and-drop carrying the item, with a preview following the pointer while the item itself is dimmed.
/// Presses on interactive parts of an item (buttons, combo boxes, text boxes) do not drag.
/// <code>&lt;ListBox bi:DragSource.IsEnabled="True" /&gt;</code>
/// Drops are handled by <see cref="DropTarget"/>, or by any WPF drop code reading <see cref="ItemFormat"/>
/// or the item's type as format.
/// </summary>
public static class DragSource
{
    /// <summary>Data format of the dragged item.</summary>
    public const string ItemFormat = "EasyBI.DragItem";

    /// <summary>Style of the preview (a <c>ContentControl</c> showing the item) following the pointer.</summary>
    public static ComponentResourceKey PreviewStyleKey { get; } = new(typeof(DragSource), "Preview");

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DragSource), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Template of the preview; by default the source's <see cref="ItemsControl.ItemTemplate"/>.</summary>
    public static readonly DependencyProperty PreviewTemplateProperty = DependencyProperty.RegisterAttached(
        "PreviewTemplate", typeof(DataTemplate), typeof(DragSource), new PropertyMetadata(null));

    /// <summary>Effects the drag allows (Copy and Move by default); the drop target picks one.</summary>
    public static readonly DependencyProperty AllowedEffectsProperty = DependencyProperty.RegisterAttached(
        "AllowedEffects", typeof(DragDropEffects), typeof(DragSource), new PropertyMetadata(DragDropEffects.Copy | DragDropEffects.Move));

    private static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
        "Press", typeof(Press), typeof(DragSource), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static DataTemplate? GetPreviewTemplate(DependencyObject element) => (DataTemplate?)element.GetValue(PreviewTemplateProperty);
    public static void SetPreviewTemplate(DependencyObject element, DataTemplate? value) => element.SetValue(PreviewTemplateProperty, value);
    public static DragDropEffects GetAllowedEffects(DependencyObject element) => (DragDropEffects)element.GetValue(AllowedEffectsProperty);
    public static void SetAllowedEffects(DependencyObject element, DragDropEffects value) => element.SetValue(AllowedEffectsProperty, value);

    /// <summary>True while a drag started by a <see cref="DragSource"/> is in progress.</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>
    /// Raised when a drag started by a <see cref="DragSource"/> ends, dropped or not (Escape, released elsewhere): drop
    /// targets showing a preview drop it then. Unsubscribe when done, the event is static.
    /// </summary>
    public static event EventHandler? DragEnded;

    /// <summary>The data a drag of <paramref name="item"/> carries.</summary>
    public static DataObject CreateData(object item)
    {
        var data = new DataObject();
        data.SetData(ItemFormat, item);
        // Also under its type, for drop code that does not know DragSource
        data.SetData(item.GetType(), item);
        return data;
    }

    /// <summary>The item carried by a drag: the <see cref="ItemFormat"/> data, or the only data of a single-format drag.</summary>
    public static object? ItemOf(IDataObject data)
    {
        if (data.GetDataPresent(ItemFormat)) return data.GetData(ItemFormat);
        var formats = data.GetFormats(autoConvert: false);
        return formats.Length == 1 ? data.GetData(formats[0]) : null;
    }

    private sealed record Press(object Item, FrameworkElement Container, Point Start);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items) return;
        items.PreviewMouseLeftButtonDown -= OnPressed;
        items.PreviewMouseMove -= OnMoved;
        items.PreviewMouseLeftButtonUp -= OnReleased;
        if (e.NewValue is not true) return;
        items.PreviewMouseLeftButtonDown += OnPressed;
        items.PreviewMouseMove += OnMoved;
        items.PreviewMouseLeftButtonUp += OnReleased;
    }

    private static void OnPressed(object sender, MouseButtonEventArgs e)
    {
        var items = (ItemsControl)sender;
        items.ClearValue(PressProperty);
        if (VisualOf(e.OriginalSource as DependencyObject) is not { } source
            || ItemsControl.ContainerFromElement(items, source) is not FrameworkElement container
            || IsInteractive(source, container)) return;
        var item = items.ItemContainerGenerator.ItemFromContainer(container);
        // A container that is its own item (UIElement children): drag it too
        if (item == DependencyProperty.UnsetValue) item = container;
        items.SetValue(PressProperty, new Press(item, container, e.GetPosition(container)));
    }

    private static void OnReleased(object sender, MouseButtonEventArgs e) => ((ItemsControl)sender).ClearValue(PressProperty);

    private static void OnMoved(object sender, MouseEventArgs e)
    {
        var items = (ItemsControl)sender;
        if (items.GetValue(PressProperty) is not Press press) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            items.ClearValue(PressProperty);
            return;
        }
        var delta = e.GetPosition(press.Container) - press.Start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        items.ClearValue(PressProperty);
        Run(items, press);
    }

    private static void Run(ItemsControl items, Press press)
    {
        var preview = CreatePreview(items, press);
        GiveFeedbackEventHandler follow = (_, _) => preview?.Follow();
        items.GiveFeedback += follow;
        var effects = GetAllowedEffects(items);
        // A moved item is dimmed where it comes from; a copied one stays as it is
        if (effects.HasFlag(DragDropEffects.Move)) press.Container.Opacity = 0.4;
        IsDragging = true;
        try
        {
            System.Windows.DragDrop.DoDragDrop(items, CreateData(press.Item), effects);
        }
        finally
        {
            IsDragging = false;
            press.Container.ClearValue(UIElement.OpacityProperty);
            items.GiveFeedback -= follow;
            preview?.Remove();
            DragEnded?.Invoke(null, EventArgs.Empty);
        }
    }

    private static Preview? CreatePreview(ItemsControl items, Press press)
    {
        // The preview lives in the window's adorner layer, so that it can travel over every drop target
        if (Window.GetWindow(items)?.Content is not UIElement root) return null;
        var content = new ContentControl
        {
            Content = press.Item,
            ContentTemplate = GetPreviewTemplate(items) ?? items.ItemTemplate,
            Width = press.Container.ActualWidth,
            IsHitTestVisible = false,
        };
        content.SetResourceReference(FrameworkElement.StyleProperty, PreviewStyleKey);
        if (FloatingAdorner.Show(root, content) is not { } adorner) return null;
        var preview = new Preview(root, adorner, press.Start);
        preview.Follow();
        return preview;
    }

    /// <summary>The preview keeps the point where the item was grabbed under the pointer.</summary>
    private sealed class Preview(UIElement root, FloatingAdorner adorner, Point grab)
    {
        public void Follow()
        {
            // Mouse events stop during a system drag: the pointer is read from the screen
            if (!GetCursorPos(out var cursor) || PresentationSource.FromVisual(root) is null) return;
            var pointer = root.PointFromScreen(new Point(cursor.X, cursor.Y));
            adorner.Place(new Point(pointer.X - grab.X, pointer.Y - grab.Y));
        }

        public void Remove() => adorner.Remove();
    }

    private static Visual? VisualOf(DependencyObject? element)
    {
        // A press on a Run reports the run: climb to the element showing it
        while (element is not null and not Visual) element = LogicalTreeHelper.GetParent(element);
        return element as Visual;
    }

    private static bool IsInteractive(DependencyObject element, DependencyObject container)
    {
        for (var e = element; e is not null && e != container; e = VisualTreeHelper.GetParent(e))
            if (e is ButtonBase or TextBoxBase or Selector or Thumb or ScrollBar or PasswordBox) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
