using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
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

    /// <summary>How a measure can be seen through time.</summary>
    public static IReadOnlyList<Option<TimeCalculation>> TimeOptions { get; } =
    [
        new(TimeCalculation.None, () => Strings.TimeNone),
        new(TimeCalculation.PreviousYear, () => Strings.TimePreviousYear),
        new(TimeCalculation.ChangeFromPreviousYear, () => Strings.TimeChangeFromPreviousYear),
        new(TimeCalculation.PercentChangeFromPreviousYear, () => Strings.TimePercentChangeFromPreviousYear),
        new(TimeCalculation.YearToDate, () => Strings.TimeYearToDate),
    ];

    /// <summary>How the visual can be sorted on a field: not at all, ascending or descending.</summary>
    public static IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new(null, "⇅", () => Strings.SortNone),
        new(ListSortDirection.Ascending, "↑", () => Strings.SortAscending),
        new(ListSortDirection.Descending, "↓", () => Strings.SortDescending),
    ];

    private readonly Action _changed;
    private ListSortDirection? _sortDirection;
    private DateGrain _grain;
    private TimeCalculation _time;
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
        SortCommand = new RelayCommand<ListSortDirection?>(direction => SortDirection = direction);
    }

    public FieldDescriptor Field { get; }

    public Shelf Shelf { get; }

    public string Caption => Field.Caption;

    public bool HasGrain => Field.Kind == FieldKind.Dimension && Field.DataType == DataType.Date;

    /// <summary>The visual can be sorted on the fields of the Axis and Values shelves.</summary>
    public bool CanSort => Shelf is Shelf.Axis or Shelf.Values;

    /// <summary>The sort of the visual on this field, or null; the designer keeps at most one sorted field.</summary>
    public ListSortDirection? SortDirection
    {
        get => _sortDirection;
        set
        {
            if (!SetProperty(ref _sortDirection, value)) return;
            OnPropertyChanged(nameof(SortOption));
            _changed();
        }
    }

    /// <summary>Sorts on this field in the direction given as parameter (none to remove the sort).</summary>
    public ICommand SortCommand { get; }

    /// <summary>The option of <see cref="SortDirection"/>, with its symbol (a combo box cannot select a null value).</summary>
    public SortOption SortOption
    {
        get => SortOptions.First(o => o.Value == _sortDirection);
        set => SortDirection = value?.Value;
    }

    /// <summary>The column of this field in the query result: what a sort on it refers to.</summary>
    public string Key => Field.Kind == FieldKind.Measure ? ToMeasureRef().Key : ToFieldRef().Key;

    /// <summary>A measure of the Values shelf: it can be seen through time.</summary>
    public bool IsValue => Field.Kind == FieldKind.Measure && Shelf == Shelf.Values;

    public TimeCalculation Time
    {
        get => _time;
        set { if (SetProperty(ref _time, value)) _changed(); }
    }

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
        Time = Time,
    };
}

/// <summary>A sort direction: its <see cref="Symbol"/> stands for it on a chip.</summary>
public sealed class SortOption(ListSortDirection? direction, string symbol, Func<string> label) : Option<ListSortDirection?>(direction, label)
{
    public string Symbol { get; } = symbol;
}

/// <summary>
/// A filter on a dimension. Most dimensions list their values to check: "one of" (In) or, for an existing filter
/// loaded as-is, "all except" (NotIn). A date dimension filters on a period instead: between two dates, or relative to
/// today (the current, last or previous months...).
/// </summary>
public sealed class FilterItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private HashSet<string?>? _pendingSelection;
    private bool _isLoading = true;
    private Option<FilterOperator?> _periodKind;
    private DateTime? _from;
    private DateTime? _to;
    private int _count = 12;
    private Option<DateGrain> _unit;

    public FilterItemViewModel(
        FieldDescriptor field,
        ISemanticModel model,
        Action changed,
        IEnumerable<string?>? selection = null,
        FilterOperator @operator = FilterOperator.In,
        DateGrain grain = DateGrain.None)
    {
        Field = field;
        Operator = @operator;
        _changed = changed;
        PeriodKinds =
        [
            new(null, () => Strings.PeriodKindAny),
            new(FilterOperator.Between, () => Strings.PeriodKindBetween),
            new(FilterOperator.Current, () => Strings.PeriodKindCurrent),
            new(FilterOperator.Last, () => Strings.PeriodKindLast),
            new(FilterOperator.Previous, () => Strings.PeriodKindPrevious),
        ];
        Units = new[] { DateGrain.Day, DateGrain.Month, DateGrain.Quarter, DateGrain.Year }
            .Select(g => new Option<DateGrain>(g, () => DateGrains.Caption(g))).ToArray();
        _periodKind = PeriodKinds[0];
        _unit = Units[1];
        if (IsPeriod)
        {
            LoadPeriod(new FilterSpec(field.Name, @operator, selection?.ToArray() ?? [], grain));
            _isLoading = false;
            return;
        }
        _pendingSelection = selection?.ToHashSet();
        _ = LoadAsync(model);
    }

    public FieldDescriptor Field { get; }

    public FilterOperator Operator { get; }

    public string Caption => Field.Caption;

    /// <summary>A date dimension: filtered on a period rather than on a list of values.</summary>
    public bool IsPeriod => Field.DataType == DataType.Date;

    public bool IsValueList => !IsPeriod;

    public ObservableCollection<FilterValueViewModel> Values { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public IReadOnlyList<Option<FilterOperator?>> PeriodKinds { get; }

    public IReadOnlyList<Option<DateGrain>> Units { get; }

    /// <summary>How the period is chosen; "any date" (null) does not filter.</summary>
    public Option<FilterOperator?> PeriodKind
    {
        get => _periodKind;
        set
        {
            if (!SetProperty(ref _periodKind, value)) return;
            OnPropertyChanged(nameof(ShowDates));
            OnPropertyChanged(nameof(ShowCount));
            OnPropertyChanged(nameof(ShowUnit));
            OnPeriodChanged();
        }
    }

    public DateTime? From
    {
        get => _from;
        set { if (SetProperty(ref _from, value)) OnPeriodChanged(); }
    }

    public DateTime? To
    {
        get => _to;
        set { if (SetProperty(ref _to, value)) OnPeriodChanged(); }
    }

    /// <summary>How many periods (last or previous ones).</summary>
    public int Count
    {
        get => _count;
        set { if (SetProperty(ref _count, Math.Max(1, value))) OnPeriodChanged(); }
    }

    public Option<DateGrain> Unit
    {
        get => _unit;
        set { if (SetProperty(ref _unit, value)) OnPeriodChanged(); }
    }

    public bool ShowDates => PeriodKind.Value == FilterOperator.Between;

    public bool ShowCount => PeriodKind.Value is FilterOperator.Last or FilterOperator.Previous;

    public bool ShowUnit => PeriodKind.Value is FilterOperator.Current or FilterOperator.Last or FilterOperator.Previous;

    public string Summary
    {
        get
        {
            if (IsPeriod) return ToFilterSpec() is { } period ? PeriodFilters.Describe(period) : Strings.FilterAll;
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
        if (IsPeriod)
        {
            return PeriodKind.Value switch
            {
                null => null,
                FilterOperator.Between when From is null && To is null => null,
                FilterOperator.Between => PeriodFilters.Between(Field.Name, Day(From), Day(To)),
                var relative => PeriodFilters.Relative(Field.Name, relative.Value, Unit.Value, Count),
            };
        }
        // While values are loading, keep the original selection
        var selected = _pendingSelection?.ToArray()
            ?? Values.Where(v => v.IsChecked).Select(v => InvariantValue.Format(v.Value)).ToArray();
        return selected.Length == 0 ? null : new FilterSpec(Field.Name, Operator, selected);
    }

    private static DateOnly? Day(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

    private void LoadPeriod(FilterSpec filter)
    {
        if (!filter.Operator.IsPeriod()) return;
        _periodKind = PeriodKinds.First(k => k.Value == filter.Operator);
        if (filter.Operator == FilterOperator.Between)
        {
            var (from, to) = PeriodFilters.Bounds(filter);
            _from = from?.ToDateTime(TimeOnly.MinValue);
            _to = to?.ToDateTime(TimeOnly.MinValue);
            return;
        }
        _count = PeriodFilters.Count(filter);
        _unit = Units.FirstOrDefault(u => u.Value == filter.Grain) ?? _unit;
    }

    private void OnPeriodChanged()
    {
        OnPropertyChanged(nameof(Summary));
        _changed();
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
