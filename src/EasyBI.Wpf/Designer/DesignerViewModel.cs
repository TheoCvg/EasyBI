using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.DragAndDrop;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

public sealed class MarkOptionViewModel(MarkType mark, Func<string> label) : Option<MarkType>(mark, label)
{
    private bool _isAvailable = true;

    public MarkType Mark => Value;

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }
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

    // Visual settings the designer does not edit (yet): kept as-is when editing
    private SortSpec? _sort;
    private int? _top;
    private IReadOnlyDictionary<string, string>? _extensions;
    private IReadOnlyList<FilterSpec> _hiddenFilters = [];

    public DesignerViewModel(EasyBIContext context)
    {
        Context = context;
        MarkOptions =
        [
            new(MarkType.Bar, () => Strings.MarkBar),
            new(MarkType.StackedBar, () => Strings.MarkStackedBar),
            new(MarkType.Line, () => Strings.MarkLine),
            new(MarkType.Area, () => Strings.MarkArea),
            new(MarkType.Scatter, () => Strings.MarkScatter),
            new(MarkType.Pie, () => Strings.MarkPie),
            new(MarkType.Kpi, () => Strings.MarkKpi),
            new(MarkType.Table, () => Strings.MarkTable),
        ];
        Models = context.Models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption)).ToArray();
        RemoveCommand = new RelayCommand<object>(Remove);
        AddFieldCommand = new RelayCommand<object?>(item =>
        {
            if (Unwrap(item) is FieldDescriptor field) AddToDefaultShelf(field);
        });
        context.Changed += (_, _) =>
        {
            RefreshAvailability();
            RefreshRendererOptions(); // another renderer may draw the visual
        };
        // Weak: a designer (a dialog's) may be shorter-lived than the shared context
        WeakEventManager<MeasureContext, EventArgs>.AddHandler(context.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
        RefreshAvailability();
        SelectedModel = context.Models.FirstOrDefault();
    }

    public EasyBIContext Context { get; }

    public IReadOnlyList<Option<ISemanticModel>> Models { get; }

    public IReadOnlyList<MarkOptionViewModel> MarkOptions { get; }

    public ObservableCollection<ShelfItemViewModel> Axis { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Legend { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Values { get; } = [];

    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];

    /// <summary>Options of the renderer that draws the visual (<see cref="RendererCapabilities.Options"/>) that apply to it.</summary>
    public ObservableCollection<RendererOptionViewModel> RendererOptions { get; } = [];

    public bool HasRendererOptions => RendererOptions.Count > 0;

    public ICommand RemoveCommand { get; }

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
        set { if (SetProperty(ref _selectedMark, value)) Rebuild(); }
    }

    /// <summary>The current visual, or null while no model is selected.</summary>
    public VisualDefinition? Visual
    {
        get => _visual;
        private set => SetProperty(ref _visual, value);
    }

    public bool CanDrop(FieldDescriptor field, Shelf shelf) => shelf switch
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
    public bool CanDrop(object item, Shelf shelf) => Unwrap(item) switch
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
                Insert(Collection(shelf), index, new ShelfItemViewModel(chip.Field, shelf, Rebuild) { Grain = chip.Grain });
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

    public void Clear()
    {
        _suspended = true;
        Axis.Clear();
        Legend.Clear();
        Values.Clear();
        Filters.Clear();
        _title = "";
        OnPropertyChanged(nameof(Title));
        _visualId = NewId();
        _sort = null;
        _top = null;
        _extensions = null;
        _hiddenFilters = [];
        _suspended = false;
        Rebuild();
    }

    /// <summary>Loads an existing visual into the designer to edit it.</summary>
    public void Load(VisualDefinition visual)
    {
        var model = Context.GetModel(visual.Model);
        SelectedModel = model;
        Clear();
        _suspended = true;
        var metadata = model.Metadata;
        _visualId = visual.Id;
        _title = visual.Title;
        _selectedMark = visual.Mark;
        _sort = visual.Sort;
        _top = visual.Top;
        _extensions = visual.Extensions;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SelectedMark));
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
        foreach (var f in visual.Filters)
        {
            // Filters on period keys (a clicked quarter...) have no shelf: they are kept without being shown
            if (f.Grain == DateGrain.None || f.Operator.IsPeriod())
                Filters.Add(new(metadata.GetField(f.Field), model, Rebuild, f.Values, f.Operator, f.Grain));
            else
                _hiddenFilters = [.. _hiddenFilters, f];
        }
        _suspended = false;
        Rebuild();
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
        // The sort only makes sense while the sorted column is still displayed
        var keys = axis.Append(legend).OfType<FieldRef>().Select(f => f.Key).Concat(values.Select(v => v.Key));
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
            Sort = _sort is not null && keys.Contains(_sort.Key) ? _sort : null,
            Top = _top,
            Extensions = _extensions,
        };
        RefreshRendererOptions();
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
        if (Visual is { } visual) Load(visual);
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
        Load(visual with { Values = visual.Values.Where(v => !calculated.Contains(v) || Context.Measures.Find(model.Metadata.Name, v.Measure) is not null).ToArray() });
    }

    private void RefreshAvailability()
    {
        foreach (var option in MarkOptions)
            option.IsAvailable = Context.ResolveRenderer(option.Mark) is not null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
