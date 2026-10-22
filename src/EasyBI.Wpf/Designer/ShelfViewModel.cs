using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using EasyBI.Abstractions;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// A well of the designer (the Axis, Legend, Values or Filters shelf) as the visual type sees it: its name ("Category"
/// for a pie, "Rows" and "Columns" for a matrix), whether the type uses it, the hint of an empty well, and whether the item being
/// dragged can be dropped on it.
/// </summary>
public sealed class ShelfViewModel : ObservableObject
{
    private readonly INotifyCollectionChanged _items;
    private bool? _isDropAllowed;

    internal ShelfViewModel(DesignerViewModel designer, Shelf kind, IEnumerable items)
    {
        Designer = designer;
        Kind = kind;
        Items = items;
        _items = (INotifyCollectionChanged)items;
        _items.CollectionChanged += (_, _) => Refresh();
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, (_, _) => Refresh(), string.Empty);
    }

    /// <summary>The designer, which decides what the well accepts (its drop handler).</summary>
    public DesignerViewModel Designer { get; }

    public Shelf Kind { get; }

    /// <summary>The chips of the well.</summary>
    public IEnumerable Items { get; }

    public bool HasItems => Items.Cast<object>().Any();

    /// <summary>The name of the well for the visual type.</summary>
    public string Label => (Kind, Designer.SelectedMark) switch
    {
        (Shelf.Axis, MarkType.Pie or MarkType.Donut) => Strings.WellCategory,
        (Shelf.Axis, MarkType.Table or MarkType.Matrix) => Strings.WellRows,
        (Shelf.Axis, _) => Strings.Axis,
        (Shelf.Legend, MarkType.Matrix) => Strings.WellColumns,
        (Shelf.Legend, _) => Strings.Legend,
        (Shelf.Values, MarkType.Pie or MarkType.Donut) => Strings.WellValue,
        (Shelf.Values, _) => Strings.Values,
        _ => Strings.Filters,
    };

    /// <summary>Whether the visual type uses the well; a well it does not use refuses fields.</summary>
    public bool IsUsed => IsUsedBy(Kind, Designer.SelectedMark);

    /// <summary>Shown when the type uses the well, or when it still holds chips (to remove them).</summary>
    public bool IsVisible => IsUsed || HasItems;

    /// <summary>What to drop on the well while it is empty.</summary>
    public string Hint => Kind switch
    {
        Shelf.Values => Strings.WellHintMeasure,
        Shelf.Filters => Strings.WellHintFilter,
        _ => Strings.WellHintDimension,
    };

    /// <summary>During a drag, whether the dragged item can be dropped on the well; null when nothing is dragged.</summary>
    public bool? IsDropAllowed
    {
        get => _isDropAllowed;
        internal set => SetProperty(ref _isDropAllowed, value);
    }

    /// <summary>The wells each type uses: a KPI only shows values, a pie and a table have no legend.</summary>
    public static bool IsUsedBy(Shelf shelf, MarkType mark) => (shelf, mark) switch
    {
        (Shelf.Axis, MarkType.Kpi) => false,
        (Shelf.Legend, MarkType.Kpi or MarkType.Pie or MarkType.Donut or MarkType.Table) => false,
        _ => true,
    };

    internal void Refresh() => OnPropertyChanged(string.Empty);
}
