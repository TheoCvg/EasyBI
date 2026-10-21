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

/// <summary>The groups of the visual type gallery, by what a visual shows.</summary>
public enum MarkGroup { Comparison, Trend, Composition, IndicatorsAndTables }

public sealed class MarkOptionViewModel : Option<MarkType>
{
    private bool _isAvailable = true;
    private bool _isSelected;

    public MarkOptionViewModel(MarkType mark, MarkGroup group, Func<string> label) : base(mark, label)
    {
        Group = group;
        ChooseCommand = new RelayCommand<object?>(_ => { if (IsAvailable) Choosing?.Invoke(Mark); });
    }

    public MarkType Mark => Value;

    public MarkGroup Group { get; }

    /// <summary>False when no renderer draws this type: it is shown greyed.</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }

    /// <summary>True for the type of the visual being designed.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Makes the visual of this type (when it is available) and closes the gallery.</summary>
    public ICommand ChooseCommand { get; }

    internal Action<MarkType>? Choosing { get; set; }
}

/// <summary>A group of the visual type gallery.</summary>
public sealed class MarkGroupViewModel(MarkGroup group, IReadOnlyList<MarkOptionViewModel> options) : Option<MarkGroup>(group, () => Caption(group))
{
    public IReadOnlyList<MarkOptionViewModel> Options { get; } = options;

    private static string Caption(MarkGroup group) => group switch
    {
        MarkGroup.Comparison => Strings.MarkGroupComparison,
        MarkGroup.Trend => Strings.MarkGroupTrend,
        MarkGroup.Composition => Strings.MarkGroupComposition,
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
    private const int MaxAxisFields = 3;

    private ISemanticModel? _selectedModel;
    private string _title = "";
    private MarkType _selectedMark = MarkType.Bar;
    private VisualDefinition? _visual;
    private string _visualId = NewId();
    private bool _suspended;

    private int? _top;
    private string? _sortSummary;
    private object? _selectedItem;
    private bool _isTypeGalleryOpen;
    private object? _draggedItem;

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
        MarkOptions =
        [
            new(MarkType.Bar, MarkGroup.Comparison, () => Strings.MarkBar),
            new(MarkType.StackedBar, MarkGroup.Comparison, () => Strings.MarkStackedBar),
            new(MarkType.Scatter, MarkGroup.Comparison, () => Strings.MarkScatter),
            new(MarkType.Line, MarkGroup.Trend, () => Strings.MarkLine),
            new(MarkType.Area, MarkGroup.Trend, () => Strings.MarkArea),
            new(MarkType.Pie, MarkGroup.Composition, () => Strings.MarkPie),
            new(MarkType.Kpi, MarkGroup.IndicatorsAndTables, () => Strings.MarkKpi),
            new(MarkType.Table, MarkGroup.IndicatorsAndTables, () => Strings.MarkTable),
        ];
        MarkGroups = MarkOptions.GroupBy(o => o.Group).Select(g => new MarkGroupViewModel(g.Key, g.ToArray())).ToArray();
        foreach (var option in MarkOptions) option.Choosing = mark =>
        {
            SelectedMark = mark;
            IsTypeGalleryOpen = false;
        };
        SyncSelectedMark();
        Models = context.Models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption)).ToArray();
        RemoveCommand = new RelayCommand<object>(Remove);
        SelectCommand = new RelayCommand<object?>(item => SelectedItem = item);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        AddFieldCommand = new RelayCommand<object?>(item =>
        {
            if (Unwrap(item) is FieldDescriptor field) AddToDefaultShelf(field);
        });
        context.Changed += (_, _) =>
        {
            RefreshAvailability();
            RefreshRendererOptions(); // another renderer may draw the visual
            SyncProperties();
        };
        // Weak: a designer (a dialog's) may be shorter-lived than the shared context
        WeakEventManager<MeasureContext, EventArgs>.AddHandler(context.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
        foreach (var chips in new[] { Axis, Legend, Values }) chips.CollectionChanged += OnChipsChanged;
        AxisWell = new ShelfViewModel(this, Shelf.Axis, Axis);
        LegendWell = new ShelfViewModel(this, Shelf.Legend, Legend);
        ValuesWell = new ShelfViewModel(this, Shelf.Values, Values);
        FiltersWell = new ShelfViewModel(this, Shelf.Filters, Filters);
        Filters.CollectionChanged += (_, e) =>
        {
            foreach (var filter in e.NewItems?.OfType<FilterItemViewModel>() ?? []) filter.Selecting = item => SelectedItem = item;
        };
        RefreshAvailability();
        SelectedModel = context.Models.FirstOrDefault();
    }

    public EasyBIContext Context { get; }

    public IReadOnlyList<Option<ISemanticModel>> Models { get; }

    public IReadOnlyList<MarkOptionViewModel> MarkOptions { get; }

    /// <summary>The <see cref="MarkOptions"/> by group, as the visual type gallery shows them.</summary>
    public IReadOnlyList<MarkGroupViewModel> MarkGroups { get; }

    /// <summary>The option of the <see cref="SelectedMark"/> (shown on the button opening the gallery).</summary>
    public MarkOptionViewModel SelectedMarkOption => MarkOptions.FirstOrDefault(o => o.Mark == _selectedMark) ?? MarkOptions[0];

    /// <summary>Whether the visual type gallery is open.</summary>
    public bool IsTypeGalleryOpen
    {
        get => _isTypeGalleryOpen;
        set => SetProperty(ref _isTypeGalleryOpen, value);
    }

    public ObservableCollection<ShelfItemViewModel> Axis { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Legend { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Values { get; } = [];

    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];

    /// <summary>The wells as the visual type sees them (names, use, hints, see <see cref="ShelfViewModel"/>).</summary>
    public ShelfViewModel AxisWell { get; }

    public ShelfViewModel LegendWell { get; }

    public ShelfViewModel ValuesWell { get; }

    public ShelfViewModel FiltersWell { get; }

    private IEnumerable<ShelfViewModel> Wells => [AxisWell, LegendWell, ValuesWell, FiltersWell];

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

    public MarkType SelectedMark
    {
        get => _selectedMark;
        set
        {
            if (!SetProperty(ref _selectedMark, value)) return;
            SyncSelectedMark();
            Rebuild();
        }
    }

    private ShelfViewModel WellOf(Shelf shelf) => shelf switch
    {
        Shelf.Axis => AxisWell,
        Shelf.Legend => LegendWell,
        Shelf.Values => ValuesWell,
        _ => FiltersWell,
    };

    /// <summary>Shows only the first rows of the result (after sorting); null or less than 1 shows them all.</summary>
    public int? Top
    {
        get => _top;
        set { if (SetProperty(ref _top, value is > 0 ? value : null)) Rebuild(); }
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

    public bool CanDrop(FieldDescriptor field, Shelf shelf) => ShelfViewModel.IsUsedBy(shelf, SelectedMark) && shelf switch
    {
        Shelf.Values => field.Kind == FieldKind.Measure,
        Shelf.Axis => field.Kind == FieldKind.Dimension && Axis.Count < MaxAxisFields && Axis.All(a => a.Field != field),
        Shelf.Legend => field.Kind == FieldKind.Dimension,
        Shelf.Filters => field.Kind == FieldKind.Dimension && Filters.All(f => f.Field != field),
        _ => false,
    };

    public void Drop(FieldDescriptor field, Shelf shelf) => Drop((object)field, shelf);

    /// <summary>
    /// Whether <paramref name="item"/> can be dropped on <paramref name="shelf"/>: a field (a <see cref="FieldDescriptor"/>,
    /// or a <see cref="ModelField"/> of the selected model, see <see cref="CanDrop(FieldDescriptor, Shelf)"/>), a chip
    /// moved within its shelf, or a dimension moved between Axis and Legend.
    /// </summary>
    public bool CanDrop(object item, Shelf shelf) => ShelfViewModel.IsUsedBy(shelf, SelectedMark) && Unwrap(item) switch
    {
        FieldDescriptor field => CanDrop(field, shelf),
        // A chip of another designer (or already removed) is not ours to move
        ShelfItemViewModel chip when !Collection(chip.Shelf).Contains(chip) => false,
        ShelfItemViewModel chip when chip.Shelf == shelf => true,
        ShelfItemViewModel chip => (chip.Shelf, shelf) switch
        {
            (Shelf.Legend, Shelf.Axis) => CanDrop(chip.Field, Shelf.Axis),
            (Shelf.Axis, Shelf.Legend) => true,
            _ => false,
        },
        FilterItemViewModel filter => shelf == Shelf.Filters && Filters.Contains(filter),
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
            case FieldDescriptor field:
                Insert(Collection(shelf), index, new ShelfItemViewModel(field, shelf, Rebuild));
                break;
            case ShelfItemViewModel chip when chip.Shelf == shelf:
                Move(Collection(shelf), chip, index);
                break;
            case ShelfItemViewModel chip:
                Collection(chip.Shelf).Remove(chip);
                Insert(Collection(shelf), index, new ShelfItemViewModel(chip.Field, shelf, Rebuild) { Grain = chip.Grain, SortDirection = chip.SortDirection });
                break;
            case FilterItemViewModel filter:
                Move(Filters, filter, index);
                break;
        }
        Rebuild();
    }

    DragDropEffects IDropHandler.DragOver(DropInfo drop) =>
        drop.Key is Shelf shelf && drop.Item is { } item && CanDrop(item, shelf)
            ? Unwrap(item) is FieldDescriptor ? DragDropEffects.Copy : DragDropEffects.Move
            : DragDropEffects.None;

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
        if (CanDrop(item, shelf)) return null;
        if (!ShelfViewModel.IsUsedBy(shelf, SelectedMark)) return Strings.Format(Strings.DropNotUsed, WellOf(shelf).Label);
        if (item is ModelField field && field.Model != _selectedModel)
            return Strings.Format(Strings.DropOtherModel, field.Caption, field.Model.Metadata.Caption);
        return Unwrap(item) switch
        {
            FieldDescriptor descriptor => FieldRefusal(descriptor, shelf),
            ShelfItemViewModel chip when Collection(chip.Shelf).Contains(chip) => FieldRefusal(chip.Field, shelf)
                ?? (shelf == Shelf.Filters ? Strings.Format(Strings.DropFilterFromList, chip.Caption) : null),
            FilterItemViewModel => Strings.DropFilterStays,
            _ => null,
        };
    }

    private string? FieldRefusal(FieldDescriptor field, Shelf shelf)
    {
        if (shelf == Shelf.Values)
            return field.Kind == FieldKind.Measure ? null : Strings.DropMeasuresOnly;
        if (field.Kind != FieldKind.Dimension) return Strings.Format(Strings.DropDimensionsOnly, ShelfName(shelf));
        return shelf switch
        {
            Shelf.Axis when Axis.Any(a => a.Field == field) => Strings.Format(Strings.DropAlreadyThere, field.Caption, ShelfName(shelf)),
            Shelf.Axis when Axis.Count >= MaxAxisFields => Strings.Format(Strings.DropAxisFull, MaxAxisFields),
            Shelf.Filters when Filters.Any(f => f.Field == field) => Strings.Format(Strings.DropAlreadyThere, field.Caption, ShelfName(shelf)),
            _ => null,
        };
    }

    // The name the well shows for the visual type
    private string ShelfName(Shelf shelf) => WellOf(shelf).Label;

    /// <summary>The field of a <see cref="ModelField"/> of the selected model; other models' fields are refused (null).</summary>
    private object? Unwrap(object? item) => item is ModelField field ? field.Model == _selectedModel ? field.Field : null : item;

    private ObservableCollection<ShelfItemViewModel> Collection(Shelf shelf) => shelf switch
    {
        Shelf.Axis => Axis,
        Shelf.Legend => Legend,
        Shelf.Values => Values,
        _ => throw new ArgumentOutOfRangeException(nameof(shelf)),
    };

    private void Insert(ObservableCollection<ShelfItemViewModel> items, int index, ShelfItemViewModel item)
    {
        // Legend shows one field: a new one replaces it
        if (items == Legend) items.Clear();
        items.Insert(Math.Clamp(index, 0, items.Count), item);
    }

    private static void Insert(ObservableCollection<FilterItemViewModel> items, int index, FilterItemViewModel item) =>
        items.Insert(Math.Clamp(index, 0, items.Count), item);

    // The index is a position among the items before the move, the moved one included
    private static void Move<T>(ObservableCollection<T> items, T item, int index)
    {
        var from = items.IndexOf(item);
        var to = Math.Clamp(index > from ? index - 1 : index, 0, items.Count - 1);
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
        Filters.Clear();
        _title = "";
        OnPropertyChanged(nameof(Title));
        _visualId = NewId();
        _top = null;
        OnPropertyChanged(nameof(Top));
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
        _selectedMark = visual.Mark;
        _top = visual.Top;
        _extensions = visual.Extensions;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectedMark));
        SyncSelectedMark();
        OnPropertyChanged(nameof(Top));
        foreach (var a in visual.Axis)
            Axis.Add(new(metadata.GetField(a.Field), Shelf.Axis, Rebuild) { Grain = a.Grain });
        if (visual.Legend is { } legend)
            Legend.Add(new(metadata.GetField(legend.Field), Shelf.Legend, Rebuild) { Grain = legend.Grain });
        foreach (var v in visual.Values)
        {
            var field = Context.GetField(model, v.Measure);
            Values.Add(new(field, Shelf.Values, Rebuild)
            {
                Aggregation = v.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum,
                SeriesMark = v.Mark,
                SecondaryAxis = v.SecondaryAxis,
                Time = v.Time,
            });
        }
        // A sort on a column that is not displayed is dropped, as the query could not sort on it
        if (visual.Sort is { } sort && Axis.Concat(Legend).Concat(Values).FirstOrDefault(c => c.Key == sort.Key) is { } sorted)
            sorted.SortDirection = sort.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var f in visual.Filters)
        {
            // Filters on period keys (a clicked quarter...) or on several fields have no shelf: they are kept without being shown
            if (f.Operator != FilterOperator.AnyOf && (f.Grain == DateGrain.None || f.Operator.IsPeriod()))
                Filters.Add(new(metadata.GetField(f.Field), model, Rebuild, f.Values, f.Operator, f.Grain));
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
            case ShelfItemViewModel s: Values.Remove(s); break;
            case FilterItemViewModel f: Filters.Remove(f); break;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_suspended) return;
        // Chart types per measure only exist in cartesian charts; the settings are kept for when the user comes back
        foreach (var value in Values) value.ShowSeriesOptions = SelectedMark.IsCartesian();
        var axis = Axis.Select(a => a.ToFieldRef()).ToArray();
        var legend = Legend.FirstOrDefault()?.ToFieldRef();
        var values = Values.Select(v => v.ToMeasureRef()).ToArray();
        // The sort follows its chip: it goes with it, and its key follows the chip's aggregation or grain
        var sorted = Axis.Concat(Legend).Concat(Values).FirstOrDefault(c => c.SortDirection is not null);
        SortSummary = sorted is null ? null : Strings.Format(Strings.SortSummary, sorted.Caption,
            sorted.SortDirection == ListSortDirection.Descending ? Strings.SortDescending : Strings.SortAscending);
        Visual = _selectedModel is null ? null : new VisualDefinition
        {
            Id = _visualId,
            Title = Title,
            Model = _selectedModel.Metadata.Name,
            Mark = SelectedMark,
            Axis = axis,
            Legend = legend,
            Values = values,
            Filters = [.. Filters.Select(f => f.ToFilterSpec()).OfType<FilterSpec>(), .. _hiddenFilters],
            Sort = sorted is null ? null : new SortSpec(sorted.Key, sorted.SortDirection == ListSortDirection.Descending),
            Top = _top,
            Extensions = _extensions,
        };
        RefreshRendererOptions();
        SyncProperties();
        Record();
    }

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
        var shape = (_selectedItem, Options: RendererOptions.ToArray());
        if (_propertiesShape is ValueTuple<object?, RendererOptionViewModel[]> previous
            && Equals(previous.Item1, shape._selectedItem) && previous.Item2.SequenceEqual(shape.Options))
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
                    DesignerProperty.Choice("SeriesMark", () => Strings.PropertySeriesMark, ShelfItemViewModel.SeriesMarkOptions,
                        () => chip.SeriesMark, v => chip.SeriesMark = v, () => chip.ShowSeriesOptions, () => Strings.SeriesMarkHint),
                    DesignerProperty.Toggle("SecondaryAxis", () => Strings.PropertySecondaryAxis,
                        () => chip.SecondaryAxis, v => chip.SecondaryAxis = v, () => chip.ShowSeriesOptions, () => Strings.SecondaryAxisHint),
                    DesignerProperty.Choice("Sort", () => Strings.Sort, ShelfItemViewModel.SortOptions,
                        () => chip.SortDirection, v => chip.SortDirection = v, () => chip.CanSort, () => Strings.SortHint),
                ]));
                break;
            case FilterItemViewModel filter:
                sections.Add(new DesignerPropertySection(DesignerPropertySections.Filter, () => filter.Caption,
                    [DesignerProperty.Custom("Editor", () => filter.Caption, filter)]));
                break;
        }
        sections.Add(new DesignerPropertySection(DesignerPropertySections.Visual, () => Strings.PropertiesVisual,
        [
            DesignerProperty.Text("Title", () => Strings.VisualTitle, () => Title, v => Title = v),
            DesignerProperty.Info("Sort", () => Strings.Sort, () => SortSummary ?? Strings.NoSort),
            DesignerProperty.Number("Top", () => Strings.Top, () => Top, v => Top = v is { } top ? (int)Math.Round(top) : null, description: () => Strings.TopHint),
        ]));
        if (RendererOptions.Count > 0)
            sections.Add(new DesignerPropertySection(DesignerPropertySections.Options, () => Strings.RendererOptions, RendererOptions.Select(OptionProperty)));
        return sections;
    }

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
    /// Shows the options of the renderer resolved for the visual. The option view-models are kept while the same
    /// options apply, so that editing one (typing a number) is not interrupted by the visual it updates.
    /// </summary>
    private void RefreshRendererOptions()
    {
        var visual = Visual;
        var renderer = visual is null ? null : Context.ResolveRenderer(visual);
        var options = renderer is null ? [] : renderer.Capabilities.Options.Where(o => o.AppliesTo?.Invoke(visual!) ?? true).ToList();
        if (RendererOptions.Select(o => (o.Renderer, o.Option)).SequenceEqual(options.Select(o => (renderer!.Name, o))))
        {
            foreach (var option in RendererOptions) option.Sync(visual!.GetRendererOption(option.Renderer, option.Option));
            return;
        }
        RendererOptions.Clear();
        foreach (var option in options)
            RendererOptions.Add(new(renderer!.Name, option, visual!.GetRendererOption(renderer.Name, option), OnRendererOptionChanged));
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
        var calculated = visual.Values.Where(v => !model.Metadata.Fields.Any(f => f.Name == v.Measure)).ToArray();
        if (calculated.Length == 0) return;
        Reload(visual with { Values = visual.Values.Where(v => !calculated.Contains(v) || Context.Measures.Find(model.Metadata.Name, v.Measure) is not null).ToArray() });
    }

    // --- Keyboard ---

    // The chips in reading order: the wells from top to bottom
    private IReadOnlyList<object> Chips => [.. Axis, .. Legend, .. Values, .. Filters];

    private IList Items(Shelf shelf) => shelf == Shelf.Filters ? Filters : Collection(shelf);

    /// <summary>Selects the next chip in reading order (the previous one when <paramref name="forward"/> is false); the first one without a selection.</summary>
    public void SelectNext(bool forward = true)
    {
        var chips = Chips;
        if (chips.Count == 0) return;
        var index = _selectedItem is null ? -1 : chips.ToList().IndexOf(_selectedItem);
        SelectedItem = index < 0 ? chips[forward ? 0 : ^1] : chips[Math.Clamp(index + (forward ? 1 : -1), 0, chips.Count - 1)];
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
        var wells = down ? Enum.GetValues<Shelf>().Where(s => s > shelf) : Enum.GetValues<Shelf>().Where(s => s < shelf).Reverse();
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
        OnHistoryChanged();
    }

    private void ResetHistory()
    {
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

    private void SyncSelectedMark()
    {
        foreach (var option in MarkOptions) option.IsSelected = option.Mark == _selectedMark;
        OnPropertyChanged(nameof(SelectedMarkOption));
        // The wells take the names and the use of the new type (not built yet while the designer is constructed)
        if (AxisWell is null) return;
        foreach (var well in Wells) well.Refresh();
    }

    private void RefreshAvailability()
    {
        foreach (var option in MarkOptions)
            option.IsAvailable = Context.ResolveRenderer(option.Mark) is not null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
