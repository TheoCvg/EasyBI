using System.Collections.ObjectModel;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

public enum Shelf { Axis, Legend, Values, Filters }

/// <summary>A field dropped on the Axis, Legend or Values shelf.</summary>
public sealed class ShelfItemViewModel : ObservableObject
{
    public static IReadOnlyList<Option<DateGrain>> GrainOptions { get; } =
        Enum.GetValues<DateGrain>().Select(g => new Option<DateGrain>(g, () => DateGrains.Caption(g))).ToArray();

    public static IReadOnlyList<Option<Aggregation>> AggregationOptions { get; } =
        new[] { Aggregation.Sum, Aggregation.Average, Aggregation.Min, Aggregation.Max, Aggregation.Count }
            .Select(a => new Option<Aggregation>(a, () => Aggregations.Caption(a)))
            .ToArray();

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

/// <summary>
/// A filter on a dimension, with the list of checkable values: "one of" (In)
/// or, for an existing filter loaded as-is, "all except" (NotIn).
/// </summary>
public sealed class FilterItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private HashSet<string?>? _pendingSelection;
    private bool _isLoading = true;

    public FilterItemViewModel(
        FieldDescriptor field,
        ISemanticModel model,
        Action changed,
        IEnumerable<string?>? selection = null,
        FilterOperator @operator = FilterOperator.In)
    {
        Field = field;
        Operator = @operator;
        _changed = changed;
        _pendingSelection = selection?.ToHashSet();
        _ = LoadAsync(model);
    }

    public FieldDescriptor Field { get; }

    public FilterOperator Operator { get; }

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
            var summary = selected.Count switch
            {
                0 => Strings.FilterAll,
                <= 2 => string.Join(", ", selected),
                _ => $"{selected[0]}, {selected[1]} +{selected.Count - 2}",
            };
            return Operator == FilterOperator.NotIn && selected.Count > 0 ? Strings.Format(Strings.FilterExcept, summary) : summary;
        }
    }

    public FilterSpec? ToFilterSpec()
    {
        // While values are loading, keep the original selection
        var selected = _pendingSelection?.ToArray()
            ?? Values.Where(v => v.IsChecked).Select(v => InvariantValue.Format(v.Value)).ToArray();
        return selected.Length == 0 ? null : new FilterSpec(Field.Name, Operator, selected);
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
