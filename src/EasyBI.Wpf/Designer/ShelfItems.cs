using System.Collections.ObjectModel;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

public enum Shelf { Axis, Legend, Values, Filters }

/// <summary>Un champ déposé sur l'étagère Axe, Légende ou Valeurs.</summary>
public sealed class ShelfItemViewModel : ObservableObject
{
    public static IReadOnlyList<Option<DateGrain>> GrainOptions { get; } =
        Enum.GetValues<DateGrain>().Select(g => new Option<DateGrain>(g, DateGrains.Caption(g))).ToArray();

    public static IReadOnlyList<Option<Aggregation>> AggregationOptions { get; } =
    [
        new(Aggregation.Sum, "Somme"),
        new(Aggregation.Average, "Moyenne"),
        new(Aggregation.Min, "Min"),
        new(Aggregation.Max, "Max"),
        new(Aggregation.Count, "Nombre"),
    ];

    private readonly Action _changed;
    private DateGrain _grain;
    private Aggregation _aggregation;

    public ShelfItemViewModel(FieldDescriptor field, Shelf shelf, Action changed)
    {
        Field = field;
        Shelf = shelf;
        _changed = changed;
        _grain = HasGrain ? DateGrain.Month : DateGrain.None;
        _aggregation = field.DefaultAggregation ?? Aggregation.Sum;
    }

    public FieldDescriptor Field { get; }

    public Shelf Shelf { get; }

    public string Caption => Field.Caption;

    public bool HasGrain => Field.Kind == FieldKind.Dimension && Field.DataType == DataType.Date;

    public bool HasAggregation => Field.Kind == FieldKind.Measure && Field.DefaultAggregation != Aggregation.Count;

    public DateGrain Grain
    {
        get => _grain;
        set { if (SetProperty(ref _grain, value)) _changed(); }
    }

    public Aggregation Aggregation
    {
        get => _aggregation;
        set { if (SetProperty(ref _aggregation, value)) _changed(); }
    }

    public FieldRef ToFieldRef() => new(Field.Name, Grain);

    public MeasureRef ToMeasureRef() => new(Field.Name, Aggregation == Field.DefaultAggregation ? null : Aggregation);
}

/// <summary>Un filtre "valeur parmi" sur une dimension, avec la liste des valeurs cochables.</summary>
public sealed class FilterItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private HashSet<string>? _pendingSelection;
    private bool _isLoading = true;

    public FilterItemViewModel(FieldDescriptor field, ISemanticModel model, Action changed, IEnumerable<string>? selection = null)
    {
        Field = field;
        _changed = changed;
        _pendingSelection = selection?.ToHashSet();
        _ = LoadAsync(model);
    }

    public FieldDescriptor Field { get; }

    public string Caption => Field.Caption;

    public ObservableCollection<FilterValueViewModel> Values { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string Summary
    {
        get
        {
            var selected = Values.Where(v => v.IsChecked).Select(v => v.Label).ToList();
            return selected.Count switch
            {
                0 => "(tout)",
                <= 2 => string.Join(", ", selected),
                _ => $"{selected[0]}, {selected[1]} +{selected.Count - 2}",
            };
        }
    }

    public FilterSpec? ToFilterSpec()
    {
        // Pendant le chargement des valeurs, on conserve la sélection d'origine
        var selected = _pendingSelection?.ToArray()
            ?? Values.Where(v => v.IsChecked).Select(v => InvariantValue.Format(v.Value)).ToArray();
        return selected.Length == 0 ? null : new FilterSpec(Field.Name, FilterOperator.In, selected);
    }

    private async Task LoadAsync(ISemanticModel model)
    {
        try
        {
            foreach (var value in await model.GetDistinctValuesAsync(Field.Name))
            {
                var isChecked = _pendingSelection?.Contains(InvariantValue.Format(value)) == true;
                Values.Add(new FilterValueViewModel(value, ValueFormatter.Display(value), isChecked, OnValueToggled));
            }
        }
        finally
        {
            _pendingSelection = null;
            IsLoading = false;
            OnPropertyChanged(nameof(Summary));
        }
    }

    private void OnValueToggled()
    {
        OnPropertyChanged(nameof(Summary));
        _changed();
    }
}

public sealed class FilterValueViewModel(object? value, string label, bool isChecked, Action toggled) : ObservableObject
{
    private bool _isChecked = isChecked;

    public object? Value { get; } = value;

    public string Label { get; } = label;

    public bool IsChecked
    {
        get => _isChecked;
        set { if (SetProperty(ref _isChecked, value)) toggled(); }
    }
}
