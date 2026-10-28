using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Text.Json;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.DragAndDrop;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

public sealed class VisualTypeOptionViewModel : Option<VisualType>
{
    private bool _isAvailable = true;
    private string? _unavailableReason;
    private bool _isSelected;

    public VisualTypeOptionViewModel(VisualType type, VisualTypeGroup group, Func<string> label) : base(type, label)
    {
        Group = group;
        ChooseCommand = new RelayCommand<object?>(_ => { if (IsAvailable) Choosing?.Invoke(Type); });
    }

    public VisualType Type => Value;

    public VisualTypeGroup Group { get; }

    /// <summary>False when no renderer draws this type, or a rule of the designer refuses it: it is shown greyed.</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    /// <summary>Why the type cannot be chosen (no renderer, a rule's message); null when it can.</summary>
    public string? UnavailableReason
    {
        get => _unavailableReason;
        set => SetProperty(ref _unavailableReason, value);
    }

    /// <summary>True for the type of the visual being designed.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Makes the visual of this type (when it is available) and closes the gallery.</summary>
    public ICommand ChooseCommand { get; }

    internal Action<VisualType>? Choosing { get; set; }
}

/// <summary>A group of the visual type gallery.</summary>
public sealed class VisualTypeGroupViewModel(VisualTypeGroup group, IReadOnlyList<VisualTypeOptionViewModel> options) : Option<VisualTypeGroup>(group, () => Caption(group))
{
    public IReadOnlyList<VisualTypeOptionViewModel> Options { get; } = options;

    private static string Caption(VisualTypeGroup group) => group switch
    {
        VisualTypeGroup.Comparison => Strings.MarkGroupComparison,
        VisualTypeGroup.Trend => Strings.MarkGroupTrend,
        VisualTypeGroup.Composition => Strings.MarkGroupComposition,
        _ => Strings.MarkGroupIndicators,
    };
}

/// <summary>
/// Designer state. Every shelf change rebuilds a <see cref="VisualDefinition"/>,
/// the only object passed to the preview (and saved). As the shelves' <see cref="IDropHandler"/>, it accepts fields
/// from the field lists and chips moved within or between shelves.
/// </summary>
public sealed class DesignerViewModel : ObservableObject, IDropHandler
{
    private ISemanticModel? _selectedModel;
    private string _title = "";
    private VisualType _selectedType = VisualType.Bar;
    private VisualDefinition? _visual;
    private string _visualId = NewId();
    private bool _suspended;

    private int? _top;
    private string? _topVariable;
    private GaugeSettings? _gauge;
    private TooltipSettings _tooltip = new();
    private ChartLegend _legendPosition;
    private string? _sortSummary;
    private object? _selectedItem;
    private bool _isTypeGalleryOpen;
    private object? _draggedItem;
    private string? _refusal;

    // History of the visual (undo/redo): the states before and after the current one, recorded at each change
    private readonly List<VisualDefinition> _undo = [];
    private readonly List<VisualDefinition> _redo = [];
    private VisualDefinition? _current;
    private string? _currentJson;
    private int _historyHold;
    private object? _propertiesShape;
    private readonly Dictionary<string, bool> _expandedSections = [];

    // Visual settings the designer does not edit (yet): kept as-is when editing
    private IReadOnlyDictionary<string, string>? _extensions;
    private IReadOnlyList<FilterSpec> _hiddenFilters = [];

    public DesignerViewModel(EasyBIContext context)
    {
        Context = context;
        TypeOptions = context.VisualTypes.Definitions
            .Where(d => d.Group is not null)
            .Select(d => new VisualTypeOptionViewModel(d.Type, d.Group!.Value, d.Caption))
            .ToArray();
        TypeGroups = TypeOptions.GroupBy(o => o.Group).Select(g => new VisualTypeGroupViewModel(g.Key, g.ToArray())).ToArray();
        foreach (var option in TypeOptions) option.Choosing = type =>
        {
            SelectedType = type;
            IsTypeGalleryOpen = false;
        };
        SyncSelectedType();
        Models = context.Models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption)).ToArray();
        RemoveCommand = new RelayCommand<object>(Remove, item => item is not null && RemoveRefusal(item) is null);
        SelectCommand = new RelayCommand<object?>(item => SelectedItem = item);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        AddFieldCommand = new RelayCommand<object?>(item =>
        {
            if (Unwrap(item) is FieldDescriptor field) AddToDefaultShelf(field);
        });
        // Weak: a designer (a dialog's) may be shorter-lived than the shared context
        WeakEventManager<EasyBIContext, EventArgs>.AddHandler(context, nameof(EasyBIContext.Changed), OnContextChanged);
        WeakEventManager<MeasureContext, EventArgs>.AddHandler(context.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
        foreach (var chips in new[] { Axis, Legend, Values, TooltipValues }) chips.CollectionChanged += OnChipsChanged;
        AxisWell = new ShelfViewModel(this, Shelf.Axis, Axis);
        LegendWell = new ShelfViewModel(this, Shelf.Legend, Legend);
        ValuesWell = new ShelfViewModel(this, Shelf.Values, Values);
        TooltipWell = new ShelfViewModel(this, Shelf.Tooltip, TooltipValues);
        FiltersWell = new ShelfViewModel(this, Shelf.Filters, Filters);
        Filters.CollectionChanged += (_, e) =>
        {
            foreach (var filter in e.NewItems?.OfType<FilterItemViewModel>() ?? []) filter.Selecting = item => SelectedItem = item;
        };
        Rules.CollectionChanged += (_, _) => RefreshAvailability();
        RefreshAvailability();
        SelectedModel = context.Models.FirstOrDefault();
    }

    private void OnContextChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
        RefreshRendererOptions(); // another renderer may draw the visual
        SyncProperties();
    }

    public EasyBIContext Context { get; }

    /// <summary>
    /// The rules every change of the visual must pass, whatever the action of the end user (a type picked, a field
    /// dropped, a property edited...): each returns null to accept the change, or the message telling why it is refused.
    /// The first refusal wins: the visual stays as it was and <see cref="Refusal"/> shows the message. What a rule
    /// would refuse is shown unavailable beforehand (the types of the gallery, the wells while a field is dragged, the
    /// remove buttons). A rule is called to try changes: it must be quick and have no side effect.
    /// <see cref="Load"/>, <see cref="Clear"/>, <see cref="Undo"/> and <see cref="Redo"/> do not go through the rules.
    /// </summary>
    public ObservableCollection<Func<VisualChange, string?>> Rules { get; } = [];

    /// <summary>The message of the last change a rule refused; null once a change is accepted.</summary>
    public string? Refusal
    {
        get => _refusal;
        private set => SetProperty(ref _refusal, value);
    }

    public IReadOnlyList<Option<ISemanticModel>> Models { get; }

    public IReadOnlyList<VisualTypeOptionViewModel> TypeOptions { get; }

    /// <summary>The <see cref="TypeOptions"/> by group, as the visual type gallery shows them.</summary>
    public IReadOnlyList<VisualTypeGroupViewModel> TypeGroups { get; }

    /// <summary>The option of the <see cref="SelectedType"/> (shown on the button opening the gallery).</summary>
    public VisualTypeOptionViewModel SelectedTypeOption => TypeOptions.FirstOrDefault(o => o.Type == _selectedType) ?? TypeOptions[0];

    /// <summary>Whether the visual type gallery is open.</summary>
    public bool IsTypeGalleryOpen
    {
        get => _isTypeGalleryOpen;
        set => SetProperty(ref _isTypeGalleryOpen, value);
    }

    public ObservableCollection<ShelfItemViewModel> Axis { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Legend { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Values { get; } = [];

    /// <summary>The measures shown in the tooltips of a chart, besides its values.</summary>
    public ObservableCollection<ShelfItemViewModel> TooltipValues { get; } = [];

    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];

    /// <summary>The wells as the visual type sees them (names, use, hints, see <see cref="ShelfViewModel"/>).</summary>
    public ShelfViewModel AxisWell { get; }

    public ShelfViewModel LegendWell { get; }

    public ShelfViewModel ValuesWell { get; }

    public ShelfViewModel TooltipWell { get; }

    public ShelfViewModel FiltersWell { get; }

    private IEnumerable<ShelfViewModel> Wells => [AxisWell, LegendWell, ValuesWell, TooltipWell, FiltersWell];

    /// <summary>
    /// The item being dragged (a field or a chip), set by the designer's view while a drag is in progress: each well
    /// tells whether it accepts it (<see cref="ShelfViewModel.IsDropAllowed"/>).
    /// </summary>
    public object? DraggedItem
    {
        get => _draggedItem;
        set
        {
            if (!SetProperty(ref _draggedItem, value)) return;
            foreach (var well in Wells) well.IsDropAllowed = value is null ? null : CanDrop(value, well.Kind);
        }
    }

    /// <summary>Options of the renderer that draws the visual (<see cref="RendererCapabilities.Options"/>) that apply to it.</summary>
    public ObservableCollection<RendererOptionViewModel> RendererOptions { get; } = [];

    public bool HasRendererOptions => RendererOptions.Count > 0;

    public ICommand RemoveCommand { get; }

    /// <summary>Puts the visual back as it was before the last change (Ctrl+Z).</summary>
    public ICommand UndoCommand { get; }

    /// <summary>Makes again the change <see cref="UndoCommand"/> undid (Ctrl+Y).</summary>
    public ICommand RedoCommand { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>How many changes can be undone (100 by default).</summary>
    public int MaxHistory { get; set; } = 100;

    /// <summary>Selects a chip (the parameter; null selects the visual itself), whose settings the properties panel shows.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>
    /// The selected chip (a <see cref="ShelfItemViewModel"/> or a <see cref="FilterItemViewModel"/>), or null when the
    /// visual itself is: the properties panel shows its settings (<see cref="Properties"/>).
    /// </summary>
    public object? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null && !Contains(value)) value = null;
            if (Equals(_selectedItem, value)) return;
            SetSelected(_selectedItem, false);
            _selectedItem = value;
            SetSelected(value, true);
            OnPropertyChanged();
            SyncProperties();
        }
    }

    /// <summary>The sections of the properties panel for the <see cref="SelectedItem"/> (see <see cref="PropertiesBuilding"/>).</summary>
    public ObservableCollection<DesignerPropertySection> Properties { get; } = [];

    /// <summary>
    /// Raised when the properties panel gets its sections (another selection, other renderer options), to change them:
    /// add sections of the application, add or remove properties.
    /// </summary>
    public event EventHandler<DesignerPropertiesEventArgs>? PropertiesBuilding;

    /// <summary>Adds a field (or a <see cref="ModelField"/> of the selected model) to its default shelf (see <see cref="AddToDefaultShelf"/>); bound to a double-click.</summary>
    public ICommand AddFieldCommand { get; }

    public IEnumerable<FieldDescriptor> Dimensions => _selectedModel?.Metadata.Dimensions ?? [];

    public IEnumerable<FieldDescriptor> Measures => _selectedModel is { } model ? Context.MeasuresOf(model) : [];

    public ISemanticModel? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (!SetProperty(ref _selectedModel, value)) return;
            OnPropertyChanged(nameof(Dimensions));
            OnPropertyChanged(nameof(Measures));
            Clear();
        }
    }

    public string Title
    {
        get => _title;
        set { if (SetProperty(ref _title, value)) Rebuild(); }
    }

    public VisualType SelectedType
    {
        get => _selectedType;
        set
        {
            if (!SetProperty(ref _selectedType, value)) return;
            SyncSelectedType();
            Rebuild();
        }
    }

    private ShelfViewModel WellOf(Shelf shelf) => shelf switch
    {
        Shelf.Axis => AxisWell,
        Shelf.Legend => LegendWell,
        Shelf.Values => ValuesWell,
        Shelf.Tooltip => TooltipWell,
        _ => FiltersWell,
    };

    /// <summary>Shows only the first rows of the result (after sorting); null or less than 1 shows them all.</summary>
    public int? Top
    {
        get => _top;
        set { if (SetProperty(ref _top, value is > 0 ? value : null)) Rebuild(); }
    }

    /// <summary>A number variable of the report giving <see cref="Top"/>; null for <see cref="Top"/> itself.</summary>
    public string? TopVariable
    {
        get => _topVariable;
        set { if (SetProperty(ref _topVariable, value)) Rebuild(); }
    }

    /// <summary>The number variables that can give <see cref="Top"/>, after "a number".</summary>
    private IReadOnlyList<Option<string?>> TopVariableOptions() =>
    [
        new(null, () => Strings.TopFixed),
        .. Context.Variables.Values.Where(v => VariableFormula.Definition(v).DataType == DataType.Number).Select(v => new Option<string?>(v.Name, () => v.Name)),
    ];

    /// <summary>The minimum of a gauge; null: 0.</summary>
    public double? GaugeMinimum
    {
        get => _gauge?.Minimum;
        set => SetGauge((_gauge ?? new()) with { Minimum = value });
    }

    /// <summary>The maximum of a gauge; null: a round number above the value and the target.</summary>
    public double? GaugeMaximum
    {
        get => _gauge?.Maximum;
        set => SetGauge((_gauge ?? new()) with { Maximum = value });
    }

    /// <summary>The target marked on a gauge; null: none.</summary>
    public double? GaugeTarget
    {
        get => _gauge?.Target;
        set => SetGauge((_gauge ?? new()) with { Target = value });
    }

    /// <summary>Where the legend of a chart goes (cartesian charts, pies and donuts).</summary>
    public ChartLegend LegendPosition
    {
        get => _legendPosition;
        set
        {
            if (SetProperty(ref _legendPosition, value)) Rebuild();
        }
    }

    public static IReadOnlyList<Option<ChartLegend>> LegendPositionOptions { get; } =
    [
        new(ChartLegend.Auto, () => Strings.LegendAuto),
        new(ChartLegend.Right, () => Strings.LegendRight),
        new(ChartLegend.Bottom, () => Strings.LegendBottom),
        new(ChartLegend.Top, () => Strings.LegendTop),
        new(ChartLegend.Left, () => Strings.LegendLeft),
        new(ChartLegend.Hidden, () => Strings.LegendHidden),
    ];

    private bool HasLegend() => SelectedType.IsCartesian() || SelectedType.IsCircular();

    /// <summary>Whether the chart shows tooltips.</summary>
    public bool ShowTooltips
    {
        get => !_tooltip.IsHidden;
        set => SetTooltip(_tooltip with { IsHidden = !value });
    }

    /// <summary>Whether tooltips show the category (their title).</summary>
    public bool TooltipShowsCategory
    {
        get => !_tooltip.HideCategory;
        set => SetTooltip(_tooltip with { HideCategory = !value });
    }

    /// <summary>Whether tooltips show the names of the series.</summary>
    public bool TooltipShowsSeries
    {
        get => !_tooltip.HideSeries;
        set => SetTooltip(_tooltip with { HideSeries = !value });
    }

    /// <summary>Whether tooltips show each value's share of its series' total.</summary>
    public bool TooltipShowsPercent
    {
        get => _tooltip.ShowPercent;
        set => SetTooltip(_tooltip with { ShowPercent = value });
    }

    /// <summary>A text replacing the tooltip's content, with fields between braces ({category}, {value}...); empty for none.</summary>
    public string TooltipTemplate
    {
        get => _tooltip.Template ?? "";
        set => SetTooltip(_tooltip with { Template = string.IsNullOrWhiteSpace(value) ? null : value });
    }

    /// <summary>The fields a tooltip text can use: the category, the hovered series and value, and the measures' captions.</summary>
    public IReadOnlyList<string> TooltipFields =>
        ["category", "series", "value", "percent", .. Values.Concat(TooltipValues).Select(c => c.Caption).Distinct()];

    private void SetTooltip(TooltipSettings tooltip)
    {
        _tooltip = tooltip;
        OnPropertyChanged(nameof(ShowTooltips));
        OnPropertyChanged(nameof(TooltipShowsCategory));
        OnPropertyChanged(nameof(TooltipShowsSeries));
        OnPropertyChanged(nameof(TooltipShowsPercent));
        OnPropertyChanged(nameof(TooltipTemplate));
        Rebuild();
    }

    private void SetGauge(GaugeSettings gauge)
    {
        _gauge = gauge is { Minimum: null, Maximum: null, Target: null } ? null : gauge;
        OnPropertyChanged(nameof(GaugeMinimum));
        OnPropertyChanged(nameof(GaugeMaximum));
        OnPropertyChanged(nameof(GaugeTarget));
        Rebuild();
    }

    /// <summary>The active sort in words ("Amount, descending"), or null when the visual is not sorted.</summary>
    public string? SortSummary
    {
        get => _sortSummary;
        private set => SetProperty(ref _sortSummary, value);
    }

    /// <summary>The current visual, or null while no model is selected.</summary>
    public VisualDefinition? Visual
    {
        get => _visual;
        private set => SetProperty(ref _visual, value);
    }

    public bool CanDrop(FieldDescriptor field, Shelf shelf) => Accepts(field, shelf) && DropRefusal(field, shelf) is null;

    // What the wells take, before the rules
    private bool Accepts(FieldDescriptor field, Shelf shelf) => ShelfViewModel.IsUsedBy(shelf, SelectedType, Context.VisualTypes) && shelf switch
    {
        Shelf.Values => field.Kind == FieldKind.Measure && Values.Count < Limit(shelf),
        Shelf.Tooltip => field.Kind == FieldKind.Measure && TooltipValues.All(t => t.Field != field),
        Shelf.Axis => field.Kind == FieldKind.Dimension && Axis.Count < Limit(shelf) && Axis.All(a => a.Field != field),
        Shelf.Legend => field.Kind == FieldKind.Dimension,
        // A measure filters the visual's groups by comparing its value
        Shelf.Filters => Filters.All(f => f.Field != field),
        _ => false,
    };

    public void Drop(FieldDescriptor field, Shelf shelf) => Drop((object)field, shelf);

    /// <summary>
    /// Whether <paramref name="item"/> can be dropped on <paramref name="shelf"/>: a field (a <see cref="FieldDescriptor"/>,
    /// or a <see cref="ModelField"/> of the selected model, see <see cref="CanDrop(FieldDescriptor, Shelf)"/>), a chip
    /// moved within its shelf, or a dimension moved between Axis and Legend.
    /// </summary>
    public bool CanDrop(object item, Shelf shelf) => Accepts(item, shelf) && DropRefusal(item, shelf) is null;

    private bool Accepts(object item, Shelf shelf) => ShelfViewModel.IsUsedBy(shelf, SelectedType, Context.VisualTypes) && Unwrap(item) switch
    {
        FieldDescriptor field => Accepts(field, shelf),
        // A chip of another designer (or already removed) is not ours to move
        ShelfItemViewModel chip when !Collection(chip.Shelf).Contains(chip) => false,
        ShelfItemViewModel chip when chip.Shelf == shelf => true,
        ShelfItemViewModel chip => (chip.Shelf, shelf) switch
        {
            (Shelf.Legend, Shelf.Axis) => Accepts(chip.Field, Shelf.Axis),
            (Shelf.Axis, Shelf.Legend) => true,
            _ => false,
        },
        FilterItemViewModel filter => shelf == Shelf.Filters && Filters.Contains(filter),
        // A set: like its dimension, once per well
        VariableItem set => SetField(set) is not null && shelf switch
        {
            Shelf.Axis => Axis.Count < Limit(shelf) && Axis.All(a => a.Set != set.Variable.Name),
            Shelf.Legend => true,
            Shelf.Filters => Filters.All(f => f.Set != set.Variable.Name),
            _ => false,
        },
        _ => false,
    };

    /// <summary>
    /// Drops <paramref name="item"/> on <paramref name="shelf"/> before the item at <paramref name="index"/>
    /// (appended past the end). A chip moved to another shelf keeps its settings; Legend holds one field.
    /// </summary>
    public void Drop(object item, Shelf shelf, int index = int.MaxValue)
    {
        if (_selectedModel is null || !CanDrop(item, shelf)) return;
        item = Unwrap(item)!;
        switch (item)
        {
            case FieldDescriptor field when shelf == Shelf.Filters:
                Insert(Filters, index, new FilterItemViewModel(field, _selectedModel, Rebuild));
                break;
            case VariableItem set when shelf == Shelf.Filters:
                Insert(Filters, index, new FilterItemViewModel(SetField(set)!, _selectedModel, Rebuild, set: set.Variable.Name));
                break;
            case VariableItem set:
                Insert(Collection(shelf), index, new ShelfItemViewModel(SetField(set)!, shelf, Rebuild) { Set = set.Variable.Name });
                break;
            case FieldDescriptor field:
                Insert(Collection(shelf), index, new ShelfItemViewModel(field, shelf, Rebuild));
                break;
            case ShelfItemViewModel chip when chip.Shelf == shelf:
                Move(Collection(shelf), chip, index);
                break;
            case ShelfItemViewModel chip:
                Collection(chip.Shelf).Remove(chip);
                Insert(Collection(shelf), index, new ShelfItemViewModel(chip.Field, shelf, Rebuild) { Grain = chip.Grain, SortDirection = chip.SortDirection, Labels = chip.Labels, Set = chip.Set });
                break;
            case FilterItemViewModel filter:
                Move(Filters, filter, index);
                break;
        }
        Rebuild();
    }

    DragDropEffects IDropHandler.DragOver(DropInfo drop) =>
        drop.Key is Shelf shelf && drop.Item is { } item && CanDrop(item, shelf)
            ? Unwrap(item) is FieldDescriptor or VariableItem ? DragDropEffects.Copy : DragDropEffects.Move
            : DragDropEffects.None;

    // The set a filter keeps or leaves out (a set dropped on Filters): "one of" or "none of" its values
    private string? SetFilter(FilterSpec filter) =>
        filter is { Operator: FilterOperator.In or FilterOperator.NotIn, Values: [var value] } && VariableReferences.NameOf(value) is { } name
        && Context.Variables.Definition(name) is SetDefinition { } set && set.Field == filter.Field
            ? name
            : null;

    // The dimension of a set of the selected model, null for another variable
    private FieldDescriptor? SetField(VariableItem item) =>
        VariableFormula.Definition(item.Variable) is SetDefinition set && item.Variable.Model == _selectedModel?.Metadata.Name
            ? _selectedModel.Metadata.Fields.FirstOrDefault(f => f.Name == set.Field && f.Kind == FieldKind.Dimension)
            : null;

    void IDropHandler.Drop(DropInfo drop)
    {
        if (drop.Key is Shelf shelf && drop.Item is { } item) Drop(item, shelf, drop.Index);
    }

    string? IDropHandler.RefusalReason(DropInfo drop) =>
        drop.Key is Shelf shelf && drop.Item is { } item ? RefusalReason(item, shelf) : null;

    /// <summary>
    /// Why <paramref name="item"/> cannot be dropped on <paramref name="shelf"/>, in words for the end user; null when it
    /// can, or when there is nothing useful to say (see <see cref="CanDrop(object, Shelf)"/>).
    /// </summary>
    public string? RefusalReason(object item, Shelf shelf)
    {
        if (Accepts(item, shelf)) return DropRefusal(item, shelf);
        if (!ShelfViewModel.IsUsedBy(shelf, SelectedType, Context.VisualTypes)) return Strings.Format(Strings.DropNotUsed, WellOf(shelf).Label);
        if (item is ModelField field && field.Model != _selectedModel)
            return Strings.Format(Strings.DropOtherModel, field.Caption, field.Model.Metadata.Caption);
        return Unwrap(item) switch
        {
            FieldDescriptor descriptor => FieldRefusal(descriptor, shelf),
            ShelfItemViewModel chip when Collection(chip.Shelf).Contains(chip) => FieldRefusal(chip.Field, shelf)
                ?? (shelf == Shelf.Filters ? Strings.Format(Strings.DropFilterFromList, chip.Caption) : null),
            FilterItemViewModel => Strings.DropFilterStays,
            VariableItem { IsSet: false } value => Strings.Format(Strings.DropVariableValue, value.Variable.Name),
            VariableItem set when set.Variable.Model != _selectedModel?.Metadata.Name => Strings.DropSetOtherModel,
            VariableItem set when shelf is Shelf.Values or Shelf.Tooltip => Strings.DropMeasuresOnly,
            VariableItem set => Strings.Format(Strings.DropAlreadyThere, set.Caption, ShelfName(shelf)),
            _ => null,
        };
    }

    private string? FieldRefusal(FieldDescriptor field, Shelf shelf)
    {
        if (shelf is Shelf.Values or Shelf.Tooltip)
            return field.Kind != FieldKind.Measure ? Strings.DropMeasuresOnly
                : shelf == Shelf.Tooltip && TooltipValues.Any(t => t.Field == field) ? Strings.Format(Strings.DropAlreadyThere, field.Caption, ShelfName(shelf))
                : shelf == Shelf.Values && Values.Count >= Limit(shelf) ? WellFull(shelf)
                : null;
        if (field.Kind != FieldKind.Dimension && shelf != Shelf.Filters) return Strings.Format(Strings.DropDimensionsOnly, ShelfName(shelf));
        return shelf switch
        {
            Shelf.Axis when Axis.Any(a => a.Field == field) => Strings.Format(Strings.DropAlreadyThere, field.Caption, ShelfName(shelf)),
            Shelf.Axis when Axis.Count >= Limit(shelf) => WellFull(shelf),
            Shelf.Filters when Filters.Any(f => f.Field == field) => Strings.Format(Strings.DropAlreadyThere, field.Caption, ShelfName(shelf)),
            _ => null,
        };
    }

    // The fields the visual type takes in a well
    private int Limit(Shelf shelf) => ShelfViewModel.DefinitionOf(shelf, SelectedType, Context.VisualTypes)?.MaxFields ?? int.MaxValue;

    private string WellFull(Shelf shelf) => Limit(shelf) == 1
        ? Strings.Format(Strings.DropWellSingleField, ShelfName(shelf))
        : Strings.Format(Strings.DropWellFull, ShelfName(shelf), Limit(shelf));

    // The name the well shows for the visual type
    private string ShelfName(Shelf shelf) => WellOf(shelf).Label;

    /// <summary>The field of a <see cref="ModelField"/> of the selected model; other models' fields are refused (null).</summary>
    private object? Unwrap(object? item) => item is ModelField field ? field.Model == _selectedModel ? field.Field : null : item;

    private ObservableCollection<ShelfItemViewModel> Collection(Shelf shelf) => shelf switch
    {
        Shelf.Axis => Axis,
        Shelf.Legend => Legend,
        Shelf.Values => Values,
        Shelf.Tooltip => TooltipValues,
        _ => throw new ArgumentOutOfRangeException(nameof(shelf)),
    };

    private void Insert(ObservableCollection<ShelfItemViewModel> items, int index, ShelfItemViewModel item)
    {
        // Legend shows one field: a new one replaces it
        if (items == Legend) items.Clear();
        items.Insert(Compat.Clamp(index, 0, items.Count), item);
    }

    private static void Insert(ObservableCollection<FilterItemViewModel> items, int index, FilterItemViewModel item) =>
        items.Insert(Compat.Clamp(index, 0, items.Count), item);

    // The index is a position among the items before the move, the moved one included
    private static void Move<T>(ObservableCollection<T> items, T item, int index)
    {
        var from = items.IndexOf(item);
        var to = Compat.Clamp(index > from ? index - 1 : index, 0, items.Count - 1);
        if (from != to) items.Move(from, to);
    }

    /// <summary>Double-click: dimension → Axis, measure → Values.</summary>
    public void AddToDefaultShelf(FieldDescriptor field) =>
        Drop(field, field.Kind == FieldKind.Measure ? Shelf.Values : Shelf.Axis);

    /// <summary>Starts a new visual (a new history of changes).</summary>
    public void Clear()
    {
        ClearCore();
        ResetHistory();
    }

    private void ClearCore()
    {
        _historyHold++;
        _suspended = true;
        Axis.Clear();
        Legend.Clear();
        Values.Clear();
        TooltipValues.Clear();
        Filters.Clear();
        _title = "";
        OnPropertyChanged(nameof(Title));
        _visualId = NewId();
        _top = null;
        _topVariable = null;
        _gauge = null;
        _tooltip = new();
        _legendPosition = ChartLegend.Auto;
        OnPropertyChanged(nameof(LegendPosition));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(TopVariable));
        _extensions = null;
        _hiddenFilters = [];
        _suspended = false;
        Rebuild();
        _historyHold--;
    }

    /// <summary>Loads an existing visual into the designer to edit it (a new history of changes).</summary>
    public void Load(VisualDefinition visual)
    {
        LoadCore(visual);
        ResetHistory();
    }

    private void LoadCore(VisualDefinition visual)
    {
        _historyHold++;
        var model = Context.GetModel(visual.Model);
        SelectedModel = model;
        ClearCore();
        _suspended = true;
        var metadata = model.Metadata;
        _visualId = visual.Id;
        _title = visual.Title;
        _selectedType = visual.Type;
        _top = visual.Top;
        _topVariable = visual.TopVariable;
        _gauge = visual.Gauge;
        _tooltip = (visual.Tooltip ?? new()) with { Values = [] };
        _legendPosition = visual.LegendPosition;
        OnPropertyChanged(nameof(LegendPosition));
        _extensions = visual.Extensions;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectedType));
        SyncSelectedType();
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(TopVariable));
        foreach (var a in visual.Axis)
            Axis.Add(new(metadata.GetField(a.Field), Shelf.Axis, Rebuild) { Grain = a.Grain, Labels = a.Labels, Set = a.Set });
        if (visual.Legend is { } legend)
            Legend.Add(new(metadata.GetField(legend.Field), Shelf.Legend, Rebuild) { Grain = legend.Grain, Labels = legend.Labels, Set = legend.Set });
        foreach (var v in visual.Values)
        {
            var field = Context.GetField(model, v.Measure);
            var chip = new ShelfItemViewModel(field, Shelf.Values, Rebuild)
            {
                Aggregation = v.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum,
                SeriesType = v.SeriesType,
                SecondaryAxis = v.SecondaryAxis,
                Time = v.Time,
            };
            chip.LoadFormat(v.Format);
            Values.Add(chip);
        }
        foreach (var t in visual.Tooltip?.Values ?? [])
        {
            var field = Context.GetField(model, t.Measure);
            TooltipValues.Add(new ShelfItemViewModel(field, Shelf.Tooltip, Rebuild) { Aggregation = t.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum });
        }
        // A sort on a column that is not displayed is dropped, as the query could not sort on it
        if (visual.Sort is { } sort && Axis.Concat(Legend).Concat(Values).FirstOrDefault(c => c.Key == sort.Key) is { } sorted)
            sorted.SortDirection = sort.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var f in visual.Filters)
        {
            // Filters on period keys (a clicked quarter...), on several fields or on a variable's value (written in code) have
            // no shelf: they are kept without being shown
            if (SetFilter(f) is { } set)
                Filters.Add(new(Context.GetField(model, f.Field), model, Rebuild, f.Values, f.Operator, set: set));
            else if (f.Operator != FilterOperator.AnyOf && (f.Grain == DateGrain.None || f.Operator.IsPeriod())
                     && !f.Values.Any(v => VariableReferences.NameOf(v) is not null))
                Filters.Add(new(Context.GetField(model, f.Field), model, Rebuild, f.Values, f.Operator, f.Grain));
            else
                _hiddenFilters = [.. _hiddenFilters, f];
        }
        _suspended = false;
        Rebuild();
        _historyHold--;
    }

    private void Remove(object item)
    {
        switch (item)
        {
            case ShelfItemViewModel s when s.Shelf == Shelf.Axis: Axis.Remove(s); break;
            case ShelfItemViewModel s when s.Shelf == Shelf.Legend: Legend.Remove(s); break;
            case ShelfItemViewModel s when s.Shelf == Shelf.Tooltip: TooltipValues.Remove(s); break;
            case ShelfItemViewModel s: Values.Remove(s); break;
            case FilterItemViewModel f: Filters.Remove(f); break;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_suspended) return;
        // Chart types per measure only exist in cartesian charts; the settings are kept for when the user comes back
        foreach (var value in Values) value.ShowSeriesOptions = SelectedType.IsCartesian();
        var sorted = Axis.Concat(Legend).Concat(Values).FirstOrDefault(c => c.SortDirection is not null);
        SortSummary = sorted is null ? null : Strings.Format(Strings.SortSummary, sorted.Caption,
            sorted.SortDirection == ListSortDirection.Descending ? Strings.SortDescending : Strings.SortAscending);
        var visual = Build(SelectedType, Axis, Legend, Values, TooltipValues, Filters);
        // A change a rule refuses: the designer goes back to the visual as it was
        if (_historyHold == 0 && _current is { } current && visual is not null && RefusalOf(current, visual) is { } refusal)
        {
            Restore(current);
            Refusal = refusal;
            return;
        }
        Visual = visual;
        RefreshRendererOptions();
        SyncProperties();
        Record();
    }

    /// <summary>
    /// The visual the designer shows with these chips and this type: what <see cref="Rebuild"/> makes, and what the
    /// rules are asked about before the user makes a change.
    /// </summary>
    private VisualDefinition? Build(VisualType type, IEnumerable<ShelfItemViewModel> axisChips, IEnumerable<ShelfItemViewModel> legendChips,
        IEnumerable<ShelfItemViewModel> valueChips, IEnumerable<ShelfItemViewModel> tooltipChips, IEnumerable<FilterItemViewModel> filters)
    {
        if (_selectedModel is null) return null;
        // Saved only when it differs from the default; kept for when the user comes back to a chart
        var tooltip = _tooltip with { Values = [.. tooltipChips.Select(t => t.ToMeasureRef())] };
        var hasTooltip = Tooltips.Has(type, Context.VisualTypes) && (tooltip.Values.Count > 0 || _tooltip is not { IsHidden: false, HideCategory: false, HideSeries: false, ShowPercent: false, Template: null });
        // The sort follows its chip: it goes with it, and its key follows the chip's aggregation or grain
        var sorted = axisChips.Concat(legendChips).Concat(valueChips).FirstOrDefault(c => c.SortDirection is not null);
        return new VisualDefinition
        {
            Id = _visualId,
            Title = Title,
            Model = _selectedModel.Metadata.Name,
            Type = type,
            Axis = [.. axisChips.Select(a => a.ToFieldRef())],
            Legend = legendChips.FirstOrDefault()?.ToFieldRef(),
            Values = [.. valueChips.Select(v => v.ToMeasureRef())],
            Filters = [.. filters.Select(f => f.ToFilterSpec()).OfType<FilterSpec>(), .. _hiddenFilters],
            Sort = sorted is null ? null : new SortSpec(sorted.Key, sorted.SortDirection == ListSortDirection.Descending),
            Top = _top,
            TopVariable = _topVariable,
            Gauge = type == VisualType.Gauge ? _gauge : null,
            Tooltip = hasTooltip ? tooltip : null,
            LegendPosition = type.IsCartesian() || type.IsCircular() ? _legendPosition : ChartLegend.Auto,
            Extensions = _extensions,
        };
    }

    // --- Rules ---

    private string? RefusalOf(VisualDefinition before, VisualDefinition after)
    {
        if (Rules.Count == 0) return null;
        var change = new VisualChange(before, after);
        return Rules.Select(rule => rule(change)).FirstOrDefault(refusal => refusal is not null);
    }

    // What the rules would say of a visual made from the current one, before the user makes it
    private string? Try(VisualDefinition? candidate) =>
        Rules.Count == 0 || Visual is not { } current || candidate is null ? null : RefusalOf(current, candidate);

    /// <summary>What the rules say of a field or a chip dropped on a well (a chip moved within its well is checked once moved).</summary>
    private string? DropRefusal(object item, Shelf shelf)
    {
        if (Rules.Count == 0 || shelf == Shelf.Filters) return null;
        var lists = Lists();
        switch (Unwrap(item))
        {
            case FieldDescriptor field:
                lists[shelf].Add(new ShelfItemViewModel(field, shelf, () => { }));
                break;
            case VariableItem set:
                lists[shelf].Add(new ShelfItemViewModel(SetField(set)!, shelf, () => { }) { Set = set.Variable.Name });
                break;
            case ShelfItemViewModel chip when chip.Shelf != shelf:
                lists[chip.Shelf].Remove(chip);
                lists[shelf].Add(new ShelfItemViewModel(chip.Field, shelf, () => { }) { Grain = chip.Grain, Labels = chip.Labels, Set = chip.Set });
                break;
            default:
                return null;
        }
        return Try(Build(SelectedType, lists[Shelf.Axis], lists[Shelf.Legend], lists[Shelf.Values], lists[Shelf.Tooltip], Filters));
    }

    /// <summary>What the rules say of a chip or a filter removed.</summary>
    private string? RemoveRefusal(object item)
    {
        if (Rules.Count == 0) return null;
        var lists = Lists();
        if (item is ShelfItemViewModel chip) lists[chip.Shelf].Remove(chip);
        return Try(Build(SelectedType, lists[Shelf.Axis], lists[Shelf.Legend], lists[Shelf.Values], lists[Shelf.Tooltip],
            item is FilterItemViewModel filter ? Filters.Where(f => f != filter) : Filters));
    }

    private Dictionary<Shelf, List<ShelfItemViewModel>> Lists() => new()
    {
        [Shelf.Axis] = [.. Axis],
        [Shelf.Legend] = [.. Legend],
        [Shelf.Values] = [.. Values],
        [Shelf.Tooltip] = [.. TooltipValues],
    };

    // --- Properties panel ---

    private bool Contains(object item) => item switch
    {
        ShelfItemViewModel chip => Collection(chip.Shelf).Contains(chip),
        FilterItemViewModel filter => Filters.Contains(filter),
        _ => false,
    };

    private static void SetSelected(object? item, bool selected)
    {
        if (item is ShelfItemViewModel chip) chip.IsSelected = selected;
        else if (item is FilterItemViewModel filter) filter.IsSelected = selected;
    }

    /// <summary>
    /// Rebuilds the sections when the selection or the renderer options changed, otherwise reads the values again (so
    /// that editing a property is not interrupted by the visual it updates).
    /// </summary>
    private void SyncProperties()
    {
        // A removed chip is no longer selected
        if (_selectedItem is not null && !Contains(_selectedItem))
        {
            SelectedItem = null;
            return;
        }
        // Only charts have a tooltips section
        var shape = (_selectedItem, Options: RendererOptions.ToArray(), Tooltips: Tooltips.Has(SelectedType, Context.VisualTypes));
        if (_propertiesShape is ValueTuple<object?, RendererOptionViewModel[], bool> previous
            && Equals(previous.Item1, shape._selectedItem) && previous.Item2.SequenceEqual(shape.Options) && previous.Item3 == shape.Tooltips)
        {
            foreach (var property in Properties.SelectMany(s => s.Properties)) property.Refresh();
            return;
        }
        _propertiesShape = shape;
        foreach (var section in Properties) _expandedSections[section.Key] = section.IsExpanded;
        var sections = BuildProperties(_selectedItem);
        PropertiesBuilding?.Invoke(this, new DesignerPropertiesEventArgs(this, _selectedItem, sections));
        Properties.Clear();
        foreach (var section in sections)
        {
            if (_expandedSections.TryGetValue(section.Key, out var expanded)) section.IsExpanded = expanded;
            Properties.Add(section);
        }
    }

    /// <summary>The built-in sections for a selection: the chip's settings first, then the visual's and the renderer's.</summary>
    private List<DesignerPropertySection> BuildProperties(object? selection)
    {
        var sections = new List<DesignerPropertySection>();
        switch (selection)
        {
            case ShelfItemViewModel chip:
                sections.Add(new DesignerPropertySection(DesignerPropertySections.Field, () => chip.Caption,
                [
                    DesignerProperty.Choice("Aggregation", () => Strings.PropertyAggregation, ShelfItemViewModel.AggregationOptions,
                        () => chip.Aggregation, v => chip.Aggregation = v, () => chip.HasAggregation),
                    DesignerProperty.Choice("Grain", () => Strings.PropertyGrain, ShelfItemViewModel.GrainOptions,
                        () => chip.Grain, v => chip.Grain = v, () => chip.HasGrain),
                    DesignerProperty.Choice("Time", () => Strings.PropertyTime, ShelfItemViewModel.TimeOptions,
                        () => chip.Time, v => chip.Time = v, () => chip.IsValue, () => Strings.TimeHint),
                    DesignerProperty.Choice("SeriesType", () => Strings.PropertySeriesMark, ShelfItemViewModel.SeriesTypeOptions,
                        () => chip.SeriesType, v => chip.SeriesType = v, () => chip.ShowSeriesOptions, () => Strings.SeriesMarkHint),
                    DesignerProperty.Toggle("SecondaryAxis", () => Strings.PropertySecondaryAxis,
                        () => chip.SecondaryAxis, v => chip.SecondaryAxis = v, () => chip.ShowSeriesOptions, () => Strings.SecondaryAxisHint),
                    DesignerProperty.Choice("Sort", () => Strings.Sort, ShelfItemViewModel.SortOptions,
                        () => chip.SortDirection, v => chip.SortDirection = v, () => chip.CanSort, () => Strings.SortHint),
                ]));
                if (chip.HasValueLabels && _selectedModel is { } model)
                    sections.Add(new DesignerPropertySection(DesignerPropertySections.ValueLabels, () => Strings.PropertiesValueLabels,
                        [DesignerProperty.Custom("Labels", () => Strings.PropertiesValueLabels, chip.ValueLabels(model))]));
                if (chip.IsValue)
                {
                    chip.Format.Target = ValueFormatViewModel.TargetOf(SelectedType, Context.VisualTypes);
                    sections.Add(new DesignerPropertySection(DesignerPropertySections.Formatting, () => Strings.PropertiesFormatting,
                        [DesignerProperty.Custom("Format", () => Strings.PropertiesFormatting, chip.Format)]));
                }
                break;
            case FilterItemViewModel filter:
                sections.Add(new DesignerPropertySection(DesignerPropertySections.Filter, () => filter.Caption,
                    [DesignerProperty.Custom("Editor", () => filter.Caption, filter)]));
                break;
        }
        sections.Add(new DesignerPropertySection(DesignerPropertySections.Visual, () => Strings.PropertiesVisual,
        [
            DesignerProperty.Text("Title", () => Strings.VisualTitle, () => Title, v => Title = v,
                description: () => Context.Variables.Variables.Count > 0 ? Strings.Format(Strings.TitleVariablesHint, "{" + Context.Variables.Variables[0].Name + "}") : ""),
            DesignerProperty.Info("Sort", () => Strings.Sort, () => SortSummary ?? Strings.NoSort),
            DesignerProperty.Choice("TopVariable", () => Strings.Top, TopVariableOptions(), () => TopVariable, v => TopVariable = v,
                () => TopVariable is not null || TopVariableOptions().Count > 1, () => Strings.TopVariableHint),
            DesignerProperty.Number("Top", () => TopVariable is null && TopVariableOptions().Count > 1 ? "" : Strings.Top, () => Top,
                v => Top = v is { } top ? (int)Math.Round(top) : null, () => TopVariable is null, () => Strings.TopHint),
            DesignerProperty.Choice("LegendPosition", () => Strings.PropertyLegendPosition, LegendPositionOptions,
                () => LegendPosition, v => LegendPosition = v, HasLegend, () => Strings.LegendPositionHint),
            DesignerProperty.Number("GaugeMinimum", () => Strings.GaugeMinimum, () => GaugeMinimum, v => GaugeMinimum = v, IsGauge, () => Strings.GaugeScaleHint),
            DesignerProperty.Number("GaugeMaximum", () => Strings.GaugeMaximum, () => GaugeMaximum, v => GaugeMaximum = v, IsGauge),
            DesignerProperty.Number("GaugeTarget", () => Strings.GaugeTarget, () => GaugeTarget, v => GaugeTarget = v, IsGauge),
        ]));
        if (Tooltips.Has(SelectedType, Context.VisualTypes))
            sections.Add(new DesignerPropertySection(DesignerPropertySections.Tooltips, () => Strings.PropertiesTooltips,
            [
                DesignerProperty.Toggle("ShowTooltips", () => Strings.TooltipShow, () => ShowTooltips, v => ShowTooltips = v),
                DesignerProperty.Toggle("TooltipCategory", () => Strings.TooltipCategory, () => TooltipShowsCategory, v => TooltipShowsCategory = v, () => ShowTooltips),
                DesignerProperty.Toggle("TooltipSeries", () => Strings.TooltipSeries, () => TooltipShowsSeries, v => TooltipShowsSeries = v, () => ShowTooltips),
                DesignerProperty.Toggle("TooltipPercent", () => Strings.TooltipPercent, () => TooltipShowsPercent, v => TooltipShowsPercent = v, () => ShowTooltips),
                DesignerProperty.Text("TooltipTemplate", () => Strings.TooltipTemplate, () => TooltipTemplate, v => TooltipTemplate = v, () => ShowTooltips,
                    () => Strings.Format(Strings.TooltipTemplateHint, string.Join(", ", TooltipFields.Select(f => $"{{{f}}}")))),
            ]));
        if (RendererOptions.Count > 0)
            sections.Add(new DesignerPropertySection(DesignerPropertySections.Options, () => Strings.RendererOptions, RendererOptions.Select(OptionProperty)));
        return sections;
    }

    private bool IsGauge() => SelectedType == VisualType.Gauge;

    private static DesignerProperty OptionProperty(RendererOptionViewModel option) => option.Option.Kind switch
    {
        RendererOptionKind.Toggle => DesignerProperty.Toggle(option.Option.Key, () => option.Label, () => option.IsOn, v => option.IsOn = v,
            description: () => option.Description ?? ""),
        RendererOptionKind.Choice => DesignerProperty.Choice(option.Option.Key, () => option.Label, option.Choices,
            () => option.Value as string ?? "", v => option.Value = v, description: () => option.Description ?? ""),
        _ => DesignerProperty.Number(option.Option.Key, () => option.Label, () => option.Value as double?, v => option.Value = v,
            description: () => option.Description ?? ""),
    };

    private void OnChipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var chip in e.NewItems?.OfType<ShelfItemViewModel>() ?? [])
        {
            chip.PropertyChanged += OnChipPropertyChanged;
            chip.Selecting = item => SelectedItem = item;
        }
        foreach (var chip in e.OldItems?.OfType<ShelfItemViewModel>() ?? []) chip.PropertyChanged -= OnChipPropertyChanged;
    }

    // The visual has one sort: sorting on a field removes the sort on the others
    private void OnChipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShelfItemViewModel.SortDirection) || sender is not ShelfItemViewModel { SortDirection: not null } sorted) return;
        var suspended = _suspended;
        _suspended = true;
        foreach (var chip in Axis.Concat(Legend).Concat(Values).Where(c => c != sorted)) chip.SortDirection = null;
        _suspended = suspended;
    }

    /// <summary>
    /// Shows the options of the visual's type (saved under its id), then those of the renderer resolved for it (saved
    /// under its name). The option view-models are kept while the same options apply, so that editing one (typing a
    /// number) is not interrupted by the visual it updates.
    /// </summary>
    private void RefreshRendererOptions()
    {
        var visual = Visual;
        var renderer = visual is null ? null : Context.ResolveRenderer(visual);
        IEnumerable<(string Owner, RendererOption Option)> declared = visual is null ? [] :
        [
            .. Context.VisualTypes.Get(visual.Type).Options.Select(o => (visual.Type.Id, o)),
            .. renderer?.Capabilities.Options.Select(o => (renderer.Name, o)) ?? [],
        ];
        var options = declared.Where(o => o.Option.AppliesTo?.Invoke(visual!) ?? true).ToList();
        if (RendererOptions.Select(o => (o.Renderer, o.Option)).SequenceEqual(options))
        {
            foreach (var option in RendererOptions) option.Sync(visual!.GetRendererOption(option.Renderer, option.Option));
            return;
        }
        RendererOptions.Clear();
        foreach (var (owner, option) in options)
            RendererOptions.Add(new(owner, option, visual!.GetRendererOption(owner, option), OnRendererOptionChanged));
        OnPropertyChanged(nameof(HasRendererOptions));
    }

    private void OnRendererOptionChanged(RendererOptionViewModel option)
    {
        // The default value is not stored: the renderer's default applies, even if it changes in a later version
        var value = Equals(option.Value, option.Option.Default) ? null : option.Value;
        _extensions = RendererSettings.WithRendererOption(_extensions, option.Renderer, option.Option.Key, value);
        Rebuild();
    }

    /// <summary>
    /// The language changed: field lists and shelves are rebuilt so that the model captions
    /// and the values shown in filters follow it. The visual itself is unchanged.
    /// </summary>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Dimensions));
        OnPropertyChanged(nameof(Measures));
        if (Visual is { } visual) Reload(visual);
    }

    /// <summary>
    /// A calculated measure was changed or removed: the visual is loaded again, so that its shelves show the new
    /// caption, and without the measures that no longer exist.
    /// </summary>
    private void OnMeasuresChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Measures));
        if (Visual is not { } visual || _selectedModel is not { } model) return;
        bool Exists(MeasureRef v) => model.Metadata.Fields.Any(f => f.Name == v.Measure) || Context.Measures.Find(model.Metadata.Name, v.Measure) is not null;
        // Reloaded when a measure it shows changed: deleted, or defined differently (renamed, redefined, restored...)
        var shown = visual.Values.Concat(visual.Tooltip?.Values ?? []).ToArray();
        if (shown.All(Exists) && Values.Concat(TooltipValues).Select(c => c.Field).SequenceEqual(shown.Select(v => Context.GetField(model, v.Measure))))
            return;
        Reload(visual with
        {
            Values = visual.Values.Where(Exists).ToArray(),
            Tooltip = visual.Tooltip is { } tooltip ? tooltip with { Values = tooltip.Values.Where(Exists).ToArray() } : null,
        });
    }

    // --- Keyboard ---

    // The chips in reading order: the wells from top to bottom
    private IReadOnlyList<object> Chips => [.. Axis, .. Legend, .. Values, .. TooltipValues, .. Filters];

    private IList Items(Shelf shelf) => shelf == Shelf.Filters ? Filters : Collection(shelf);

    /// <summary>Selects the next chip in reading order (the previous one when <paramref name="forward"/> is false); the first one without a selection.</summary>
    public void SelectNext(bool forward = true)
    {
        var chips = Chips;
        if (chips.Count == 0) return;
        var index = _selectedItem is null ? -1 : chips.ToList().IndexOf(_selectedItem);
        SelectedItem = index < 0 ? chips[forward ? 0 : ^1] : chips[Compat.Clamp(index + (forward ? 1 : -1), 0, chips.Count - 1)];
    }

    /// <summary>Selects a chip of the next well holding some (the previous one when <paramref name="down"/> is false), at the same place if it can.</summary>
    public void SelectAcross(bool down = true)
    {
        if (_selectedItem is null)
        {
            SelectNext(down);
            return;
        }
        var shelf = ShelfOf(_selectedItem);
        var index = Items(shelf).IndexOf(_selectedItem);
        var wells = down ? Compat.GetValues<Shelf>().Where(s => s > shelf) : Compat.GetValues<Shelf>().Where(s => s < shelf).Reverse();
        if (wells.Select(Items).FirstOrDefault(items => items.Count > 0) is { } next)
            SelectedItem = next[Math.Min(index, next.Count - 1)];
    }

    /// <summary>Moves the selected chip one place within its well (towards the end when <paramref name="forward"/> is true).</summary>
    public void MoveSelected(bool forward = true)
    {
        if (_selectedItem is not { } item) return;
        var items = Items(ShelfOf(item));
        var index = items.IndexOf(item);
        // Drop indexes count the item at its place before the move
        if (forward ? index < items.Count - 1 : index > 0) Drop(item, ShelfOf(item), forward ? index + 2 : index - 1);
        SelectedItem = item;
    }

    /// <summary>Removes the selected chip and selects the one taking its place (or the previous one).</summary>
    public void RemoveSelected()
    {
        if (_selectedItem is not { } item) return;
        var items = Items(ShelfOf(item));
        var index = items.IndexOf(item);
        Remove(item);
        SelectedItem = items.Count == 0 ? null : items[Math.Min(index, items.Count - 1)];
    }

    private static Shelf ShelfOf(object item) => item is ShelfItemViewModel chip ? chip.Shelf : Shelf.Filters;

    // --- History ---

    // A reload of the same visual (other language, renamed measure): its history goes on
    private void Reload(VisualDefinition visual)
    {
        LoadCore(visual);
        _current = Visual;
        _currentJson = Json(Visual);
    }

    private static string? Json(VisualDefinition? visual) => visual is null ? null : JsonSerializer.Serialize(visual, ReportSerializer.Options);

    // Called after each rebuild: a visual that changed becomes a step of the history
    private void Record()
    {
        if (_historyHold > 0 || Visual is not { } visual) return;
        var json = Json(visual);
        if (json == _currentJson) return;
        if (_current is not null)
        {
            _undo.Add(_current);
            if (_undo.Count > Math.Max(1, MaxHistory)) _undo.RemoveAt(0);
        }
        _redo.Clear();
        _current = visual;
        _currentJson = json;
        Refusal = null;
        OnHistoryChanged();
    }

    private void ResetHistory()
    {
        Refusal = null;
        _undo.Clear();
        _redo.Clear();
        _current = Visual;
        _currentJson = Json(Visual);
        OnHistoryChanged();
    }

    public void Undo()
    {
        if (!CanUndo || _current is null) return;
        _redo.Add(_current);
        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(previous);
    }

    public void Redo()
    {
        if (!CanRedo || _current is null) return;
        _undo.Add(_current);
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Restore(next);
    }

    // Shows a state of the history; the chip at the place of the selected one stays selected
    private void Restore(VisualDefinition visual)
    {
        (Shelf Shelf, int Index)? selection = _selectedItem switch
        {
            ShelfItemViewModel chip => (chip.Shelf, Collection(chip.Shelf).IndexOf(chip)),
            FilterItemViewModel filter => (Shelf.Filters, Filters.IndexOf(filter)),
            _ => null,
        };
        _historyHold++;
        LoadCore(visual);
        _historyHold--;
        _current = visual;
        _currentJson = Json(visual);
        if (selection is var (shelf, index))
        {
            IList<object> items = shelf == Shelf.Filters ? Filters.Cast<object>().ToList() : Collection(shelf).Cast<object>().ToList();
            SelectedItem = index >= 0 && index < items.Count ? items[index] : null;
        }
        OnHistoryChanged();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SyncSelectedType()
    {
        foreach (var option in TypeOptions) option.IsSelected = option.Type == _selectedType;
        OnPropertyChanged(nameof(SelectedTypeOption));
        // The wells take the names and the use of the new type (not built yet while the designer is constructed)
        if (AxisWell is null) return;
        foreach (var well in Wells) well.Refresh();
        // The format editors show what the new type uses
        foreach (var chip in Values) chip.Format.Target = ValueFormatViewModel.TargetOf(_selectedType, Context.VisualTypes);
    }

    private void RefreshAvailability()
    {
        foreach (var option in TypeOptions)
        {
            option.UnavailableReason = Context.ResolveRenderer(option.Type) is null ? Strings.NotSupportedByRenderer
                : option.Type == _selectedType ? null
                : Try(Build(option.Type, Axis, Legend, Values, TooltipValues, Filters));
            option.IsAvailable = option.UnavailableReason is null;
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
