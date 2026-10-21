using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace EasyBI.Wpf;

/// <summary>
/// A named place of a component where an application inserts content (<see cref="Slots"/>): shows its default
/// <see cref="Content"/>, with the content of the matching slots before or after it, or instead of it.
/// Before and after go left and right of the default content, or above and below it with a vertical
/// <see cref="Orientation"/>; the default content takes the remaining space.
/// </summary>
[ContentProperty(nameof(Content))]
public class SlotHost : FrameworkElement
{
    public static readonly DependencyProperty SlotProperty = DependencyProperty.Register(
        nameof(Slot), typeof(string), typeof(SlotHost), new PropertyMetadata("", (d, _) => ((SlotHost)d).Rebuild()));

    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(SlotHost),
        new FrameworkPropertyMetadata(null, (d, e) => ((SlotHost)d).OnContentChanged(e.OldValue, e.NewValue)));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(SlotHost),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((SlotHost)d).Rebuild()));

    private readonly DockPanel _panel = new();
    private readonly ContentPresenter _default = new();
    // Presenters of the slots' content: emptied when they are discarded, so that the content can be shown elsewhere
    private readonly List<ContentPresenter> _contributions = [];
    private bool _unloaded;

    // Elements given to a presenter: they only become its visual children once it is laid out
    private static readonly ConditionalWeakTable<Visual, ContentPresenter> Shown = new();

    public SlotHost()
    {
        _default.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Content)) { Source = this });
        AddVisualChild(_panel);
        // An unloaded host releases the slots' content, so that another place can show it
        // (a list item recycled, an element moved to another window...)
        Loaded += (_, _) =>
        {
            _unloaded = false;
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _unloaded = true;
            Release();
        };
        Rebuild();
    }

    /// <summary>Name of the slot, such as <see cref="FilterBarSlots.AddButton"/>.</summary>
    public string Slot
    {
        get => (string)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    /// <summary>The default content, shown unless a slot replaces or hides it.</summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>The default content is not shown: hidden or replaced by a slot.</summary>
    public bool IsDefaultContentReplaced => _default.Parent is null;

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _panel : throw new ArgumentOutOfRangeException(nameof(index));

    protected override IEnumerator LogicalChildren =>
        (Content is null ? Enumerable.Empty<object>() : [Content]).GetEnumerator();

    protected override Size MeasureOverride(Size availableSize)
    {
        _panel.Measure(availableSize);
        return _panel.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _panel.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private SlotCollection? Items => (SlotCollection?)GetValue(Slots.ItemsProperty);

    internal void OnSlotsChanged(SlotCollection? old, SlotCollection? current)
    {
        // Weak: slots declared on a window must not keep the hosts of closed components alive
        if (old is not null) CollectionChangedEventManager.RemoveHandler(old, OnItemsChanged);
        if (current is not null) CollectionChangedEventManager.AddHandler(current, OnItemsChanged);
        Rebuild();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnContentChanged(object? old, object? current)
    {
        // Logical child, as in a ContentControl: names, resources and data context resolve from the host
        RemoveLogicalChild(old);
        AddLogicalChild(current);
    }

    /// <summary>
    /// Lays out the content of the matching slots around the default content. The default content stays in place
    /// when it remains shown, so that it is not unloaded and reloaded (a visual would query its data again).
    /// </summary>
    private void Rebuild()
    {
        Release();
        if (_unloaded) return;
        var slots = Items?.Where(s => s.Name == Slot).ToList() ?? [];
        var replacement = slots.LastOrDefault(s => s.Mode is SlotMode.Replace or SlotMode.Hide);
        if (replacement is not null) _panel.Children.Remove(_default);

        var horizontal = Orientation == Orientation.Horizontal;
        var index = 0;
        foreach (var slot in slots.Where(s => s.Mode == SlotMode.Before))
            _panel.Children.Insert(index++, Docked(Present(slot), horizontal ? Dock.Left : Dock.Top));
        // Docked from the far edge inwards: the first one ends up next to the default content
        foreach (var slot in slots.Where(s => s.Mode == SlotMode.After).Reverse())
            _panel.Children.Insert(index++, Docked(Present(slot), horizontal ? Dock.Right : Dock.Bottom));

        if (replacement is null)
        {
            if (_default.Parent is null) _panel.Children.Add(_default);
        }
        else if (replacement.Mode == SlotMode.Replace)
            _panel.Children.Add(Present(replacement));
        _panel.LastChildFill = replacement?.Mode != SlotMode.Hide;
    }

    private static ContentPresenter Docked(ContentPresenter presenter, Dock dock)
    {
        DockPanel.SetDock(presenter, dock);
        return presenter;
    }

    private ContentPresenter Present(Slot slot)
    {
        if (slot.Content is Visual element
            && (VisualTreeHelper.GetParent(element) is not null || Shown.TryGetValue(element, out var other) && other.Content == element))
            throw new InvalidOperationException(
                $"The content of the slot '{slot.Name}' is already shown elsewhere: an element can only be shown once, " +
                "give the slot a ContentTemplate to show it in several places.");

        var presenter = new ContentPresenter { ContentTemplate = slot.ContentTemplate };
        if (slot.Content is null && slot.ContentTemplate is not null)
            // The template gets the host's data context (a content presenter sets its own to its content)
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(DataContext)) { Source = this });
        else
            presenter.Content = slot.Content;
        if (slot.Content is Visual shown) Shown.AddOrUpdate(shown, presenter);
        _contributions.Add(presenter);
        return presenter;
    }

    private void Release()
    {
        foreach (var presenter in _contributions)
        {
            _panel.Children.Remove(presenter);
            BindingOperations.ClearBinding(presenter, ContentPresenter.ContentProperty);
            presenter.Content = null;
        }
        _contributions.Clear();
    }
}
