using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// A well of the designer (the Axis, Legend, Values, Tooltip or Filters shelf) as the visual type sees it: its name ("Category"
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
    public string Label => Kind switch
    {
        Shelf.Axis => DefinitionOf(Kind, Designer.SelectedType, Designer.Context.VisualTypes)?.Caption?.Invoke() ?? Strings.Axis,
        Shelf.Legend => DefinitionOf(Kind, Designer.SelectedType, Designer.Context.VisualTypes)?.Caption?.Invoke() ?? Strings.Legend,
        Shelf.Values => DefinitionOf(Kind, Designer.SelectedType, Designer.Context.VisualTypes)?.Caption?.Invoke() ?? Strings.Values,
        Shelf.Tooltip => Strings.WellTooltip,
        _ => Strings.Filters,
    };

    /// <summary>Whether the visual type uses the well; a well it does not use refuses fields.</summary>
    public bool IsUsed => IsUsedBy(Kind, Designer.SelectedType, Designer.Context.VisualTypes);

    /// <summary>Shown when the type uses the well, or when it still holds chips (to remove them).</summary>
    public bool IsVisible => IsUsed || HasItems;

    /// <summary>What to drop on the well while it is empty.</summary>
    public string Hint => Kind switch
    {
        Shelf.Values => Strings.WellHintMeasure,
        Shelf.Tooltip => Strings.TooltipWellHint,
        Shelf.Filters => Strings.WellHintFilter,
        _ => Strings.WellHintDimension,
    };

    /// <summary>During a drag, whether the dragged item can be dropped on the well; null when nothing is dragged.</summary>
    public bool? IsDropAllowed
    {
        get => _isDropAllowed;
        internal set => SetProperty(ref _isDropAllowed, value);
    }

    /// <summary>
    /// The wells each type uses (its <see cref="VisualTypeDefinition"/> in <paramref name="types"/>, EasyBI's types when
    /// null); the tooltip well is for the types with tooltips.
    /// </summary>
    public static bool IsUsedBy(Shelf shelf, VisualType type, VisualTypeCatalog? types = null) =>
        shelf == Shelf.Tooltip ? Tooltips.Has(type, types) : DefinitionOf(shelf, type, types)?.IsUsed ?? true;

    /// <summary>What a type takes in a well; null for the wells that do not depend on the type (tooltip, filters).</summary>
    internal static VisualWell? DefinitionOf(Shelf shelf, VisualType type, VisualTypeCatalog? types = null)
    {
        var definition = (types ?? VisualTypeCatalog.Default).Get(type);
        return shelf switch
        {
            Shelf.Axis => definition.Axis,
            Shelf.Legend => definition.Legend,
            Shelf.Values => definition.Values,
            _ => null,
        };
    }

    internal void Refresh() => OnPropertyChanged(string.Empty);
}
