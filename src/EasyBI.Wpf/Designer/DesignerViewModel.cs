using System.Collections.ObjectModel;
using System.Windows.Input;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

public sealed class MarkOptionViewModel(MarkType mark, string label) : ObservableObject
{
    private bool _isAvailable = true;

    public MarkType Mark { get; } = mark;

    public string Label { get; } = label;

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetProperty(ref _isAvailable, value);
    }
}

/// <summary>
/// Designer state. Every shelf change rebuilds a <see cref="VisualDefinition"/>,
/// the only object passed to the preview (and saved).
/// </summary>
public sealed class DesignerViewModel : ObservableObject
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
            new(MarkType.Bar, "Barres"),
            new(MarkType.Line, "Courbes"),
            new(MarkType.Pie, "Secteurs"),
            new(MarkType.Kpi, "KPI"),
            new(MarkType.Table, "Table"),
        ];
        RemoveCommand = new RelayCommand<object>(Remove);
        context.Changed += (_, _) => RefreshAvailability();
        RefreshAvailability();
        SelectedModel = context.Models.FirstOrDefault();
    }

    public EasyBIContext Context { get; }

    public IReadOnlyList<ISemanticModel> Models => Context.Models;

    public IReadOnlyList<MarkOptionViewModel> MarkOptions { get; }

    public ObservableCollection<ShelfItemViewModel> Axis { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Legend { get; } = [];

    public ObservableCollection<ShelfItemViewModel> Values { get; } = [];

    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];

    public ICommand RemoveCommand { get; }

    public IEnumerable<FieldDescriptor> Dimensions => _selectedModel?.Metadata.Dimensions ?? [];

    public IEnumerable<FieldDescriptor> Measures => _selectedModel?.Metadata.Measures ?? [];

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
        Shelf.Filters => field.Kind == FieldKind.Dimension && field.DataType != DataType.Date && Filters.All(f => f.Field != field),
        _ => false,
    };

    public void Drop(FieldDescriptor field, Shelf shelf)
    {
        if (_selectedModel is null || !CanDrop(field, shelf)) return;
        switch (shelf)
        {
            case Shelf.Axis: Axis.Add(new(field, shelf, Rebuild)); break;
            case Shelf.Legend: Legend.Clear(); Legend.Add(new(field, shelf, Rebuild)); break;
            case Shelf.Values: Values.Add(new(field, shelf, Rebuild)); break;
            case Shelf.Filters: Filters.Add(new(field, _selectedModel, Rebuild)); break;
        }
        Rebuild();
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
            var field = metadata.GetField(v.Measure);
            Values.Add(new(field, Shelf.Values, Rebuild) { Aggregation = v.Aggregation ?? field.DefaultAggregation ?? Aggregation.Sum });
        }
        foreach (var f in visual.Filters)
        {
            // Period filters (with a granularity) have no shelf: they are kept without being shown
            if (f.Grain == DateGrain.None)
                Filters.Add(new(metadata.GetField(f.Field), model, Rebuild, f.Values, f.Operator));
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
    }

    private void RefreshAvailability()
    {
        foreach (var option in MarkOptions)
            option.IsAvailable = Context.ResolveRenderer(option.Mark) is not null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
