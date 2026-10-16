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
/// État du designer. Chaque modification des étagères reconstruit une <see cref="VisualDefinition"/>,
/// seul objet transmis à l'aperçu (et sauvegardé).
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

    /// <summary>Le visuel courant, ou null tant qu'aucun modèle n'est sélectionné.</summary>
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

    /// <summary>Double-clic : dimension → Axe, mesure → Valeurs.</summary>
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
        _suspended = false;
        Rebuild();
    }

    /// <summary>Recharge un visuel existant dans le designer pour l'éditer.</summary>
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
            Filters.Add(new(metadata.GetField(f.Field), model, Rebuild, f.Values));
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
        Visual = _selectedModel is null ? null : new VisualDefinition
        {
            Id = _visualId,
            Title = Title,
            Model = _selectedModel.Metadata.Name,
            Mark = SelectedMark,
            Axis = Axis.Select(a => a.ToFieldRef()).ToArray(),
            Legend = Legend.FirstOrDefault()?.ToFieldRef(),
            Values = Values.Select(v => v.ToMeasureRef()).ToArray(),
            Filters = Filters.Select(f => f.ToFilterSpec()).OfType<FilterSpec>().ToArray(),
        };
    }

    private void RefreshAvailability()
    {
        foreach (var option in MarkOptions)
            option.IsAvailable = Context.ResolveRenderer(option.Mark) is not null;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
