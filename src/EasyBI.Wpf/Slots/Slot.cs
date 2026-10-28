using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;

namespace EasyBI.Wpf;

/// <summary>Where a <see cref="Slot"/>'s content goes, relative to the default content of the slot.</summary>
public enum SlotMode
{
    /// <summary>After the default content (to its right, or below it in a vertical slot).</summary>
    After,

    /// <summary>Before the default content (to its left, or above it in a vertical slot).</summary>
    Before,

    /// <summary>Instead of the default content.</summary>
    Replace,

    /// <summary>Hides the default content; the content of the other slots with the same name stays.</summary>
    Hide,
}

/// <summary>
/// Content an application inserts in a named place of an EasyBI component (a <see cref="SlotHost"/>): before or after
/// the default content, or instead of it. Slots are declared with <see cref="Slots"/>; the names each component offers
/// are listed by its <c>...Slots</c> class (<see cref="FilterBarSlots"/>, <see cref="Designer.ReportDesignerSlots"/>).
/// </summary>
/// <remarks>
/// The content takes the data context of the place it is shown in (documented by each slot name). An element can only
/// be shown once: a slot repeated in a list (one per filter, per item...) takes a <see cref="ContentTemplate"/> instead.
/// </remarks>
[ContentProperty(nameof(Content))]
public sealed class Slot : INotifyPropertyChanged
{
    private string _name = "";
    private SlotMode _mode;
    private object? _content;
    private DataTemplate? _contentTemplate;

    public Slot()
    {
    }

    public Slot(string name, object? content = null, SlotMode mode = SlotMode.After)
    {
        _name = name;
        _content = content;
        _mode = mode;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Name of the slot, such as <see cref="FilterBarSlots.AddButton"/>.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public SlotMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    /// <summary>An element, or data shown with <see cref="ContentTemplate"/>.</summary>
    public object? Content
    {
        get => _content;
        set => Set(ref _content, value);
    }

    /// <summary>
    /// Template of the content; without <see cref="Content"/>, it is applied to the data context of the slot, which
    /// lets a slot repeated in a list show it once per item.
    /// </summary>
    public DataTemplate? ContentTemplate
    {
        get => _contentTemplate;
        set => Set(ref _contentTemplate, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>Slots of a component; a change of a slot is reported as a reset of the collection.</summary>
public sealed class SlotCollection : ObservableCollection<Slot>
{
    protected override void InsertItem(int index, Slot item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        item.PropertyChanged += OnSlotChanged;
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, Slot item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        this[index].PropertyChanged -= OnSlotChanged;
        item.PropertyChanged += OnSlotChanged;
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        this[index].PropertyChanged -= OnSlotChanged;
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        foreach (var slot in this) slot.PropertyChanged -= OnSlotChanged;
        base.ClearItems();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e) =>
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
}
