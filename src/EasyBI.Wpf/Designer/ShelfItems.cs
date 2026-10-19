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

    /// <summary>Chart types a measure can take in a cartesian chart; null follows the visual's type.</summary>
    public static IReadOnlyList<Option<MarkType?>> SeriesMarkOptions { get; } =
    [
        new(null, () => Strings.SeriesMarkDefault),
        new(MarkType.Bar, () => Strings.MarkBar),
        new(MarkType.StackedBar, () => Strings.MarkStackedBar),
        new(MarkType.Line, () => Strings.MarkLine),
        new(MarkType.Area, () => Strings.MarkArea),
        new(MarkType.Scatter, () => Strings.MarkScatter),
    ];

    private readonly Action _changed;
    private DateGrain _grain;
    private Aggregation _aggregation;
    private MarkType? _seriesMark;
    private bool _secondaryAxis;
    private bool _showSeriesOptions;

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

    // A row count and a calculated measure (ratio...) have no other meaningful aggregation
    public bool HasAggregation => Field.Kind == FieldKind.Measure && !Field.IsCalculated && Field.DefaultAggregation != Aggregation.Count;

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

    /// <summary>Chart type of this measure (Values shelf, cartesian chart); null follows the visual's type.</summary>
    public MarkType? SeriesMark
    {
        get => _seriesMark;
        set
        {
            if (!SetProperty(ref _seriesMark, value)) return;
            OnPropertyChanged(nameof(SeriesMarkOption));
            _changed();
        }
    }

    /// <summary><see cref="SeriesMark"/> as a list option (a combo box cannot select a null value).</summary>
    public Option<MarkType?> SeriesMarkOption
    {
        get => SeriesMarkOptions.First(o => o.Value == _seriesMark);
        set => SeriesMark = value?.Value;
    }

    /// <summary>Draws this measure against the second value axis.</summary>
    public bool SecondaryAxis
    {
        get => _secondaryAxis;
        set { if (SetProperty(ref _secondaryAxis, value)) _changed(); }
    }

    /// <summary>True while the chart type and axis of this measure can be chosen (a measure of a cartesian chart).</summary>
    public bool ShowSeriesOptions
    {
        get => _showSeriesOptions;
        internal set => SetProperty(ref _showSeriesOptions, value);
    }

    public FieldRef ToFieldRef() => new(Field.Name, Grain);

    public MeasureRef ToMeasureRef() => new(Field.Name, HasAggregation && Aggregation != Field.DefaultAggregation ? Aggregation : null)
    {
        Mark = SeriesMark,
        SecondaryAxis = SecondaryAxis,
    };
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
