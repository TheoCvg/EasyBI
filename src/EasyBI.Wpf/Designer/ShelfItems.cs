using System.Globalization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

public enum Shelf { Axis, Legend, Values, Tooltip, Filters }

/// <summary>A field dropped on the Axis, Legend, Values or Tooltip shelf.</summary>
public sealed class ShelfItemViewModel : ObservableObject
{
    public static IReadOnlyList<Option<DateGrain>> GrainOptions { get; } =
        Compat.GetValues<DateGrain>().Select(g => new Option<DateGrain>(g, () => DateGrains.Caption(g))).ToArray();

    public static IReadOnlyList<Option<Aggregation>> AggregationOptions { get; } =
        new[] { Aggregation.Sum, Aggregation.Average, Aggregation.Min, Aggregation.Max, Aggregation.Count }
            .Select(a => new Option<Aggregation>(a, () => Aggregations.Caption(a)))
            .ToArray();

    /// <summary>Chart types a measure can take in a cartesian chart; null follows the visual's type.</summary>
    public static IReadOnlyList<Option<VisualType?>> SeriesTypeOptions { get; } =
    [
        new(null, () => Strings.SeriesMarkDefault),
        .. new[] { VisualType.Bar, VisualType.StackedBar, VisualType.Line, VisualType.Area, VisualType.Scatter }
            .Select(type => new Option<VisualType?>(type, VisualTypeCatalog.Default.Get(type).Caption)),
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
    private ValueFormatViewModel? _format;
    private bool _isSelected;
    private ListSortDirection? _sortDirection;
    private DateGrain _grain;
    private IReadOnlyDictionary<string, string>? _labels;
    private ValueLabelsViewModel? _valueLabels;
    private TimeCalculation _time;
    private Aggregation _aggregation;
    private VisualType? _seriesType;
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
        SelectCommand = new RelayCommand<object?>(_ => Selecting?.Invoke(this));
        // The badges sum the settings up: they follow them, and the language
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(Badges) or nameof(IsSelected))) OnPropertyChanged(nameof(Badges));
        };
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, (_, _) => OnPropertyChanged(nameof(Badges)), string.Empty);
    }

    /// <summary>Selects the chip in its designer (a click on it), whose properties panel then shows its settings.</summary>
    public ICommand SelectCommand { get; }

    internal Action<object>? Selecting { get; set; }

    /// <summary>
    /// Short marks of the chip's settings, shown on it (the settings themselves are in the properties panel):
    /// aggregation, granularity, time calculation, chart type, secondary axis, sort.
    /// </summary>
    public IReadOnlyList<string> Badges
    {
        get
        {
            var badges = new List<string>();
            if (HasAggregation) badges.Add(Aggregation switch
            {
                Aggregation.Average => Strings.BadgeAverage,
                Aggregation.Min => Strings.BadgeMin,
                Aggregation.Max => Strings.BadgeMax,
                Aggregation.Count => Strings.BadgeCount,
                _ => "Σ",
            });
            if (HasGrain) badges.Add(DateGrains.Caption(Grain));
            if (IsValue && Time != TimeCalculation.None) badges.Add(Time switch
            {
                TimeCalculation.PreviousYear => Strings.BadgePreviousYear,
                TimeCalculation.ChangeFromPreviousYear => Strings.BadgeChange,
                TimeCalculation.PercentChangeFromPreviousYear => Strings.BadgePercentChange,
                _ => Strings.BadgeYearToDate,
            });
            if (ShowSeriesOptions && SeriesType is not null) badges.Add(SeriesTypeOption.Label);
            if (ShowSeriesOptions && SecondaryAxis) badges.Add(Strings.SecondaryAxis);
            if (CanSort && SortDirection is not null) badges.Add(SortOption.Symbol);
            return badges;
        }
    }

    public FieldDescriptor Field { get; }

    public Shelf Shelf { get; }

    public string Caption => Field.Caption;

    public bool HasGrain => Field.Kind == FieldKind.Dimension && Field.DataType == DataType.Date;

    /// <summary>True while the chip is the designer's <see cref="DesignerViewModel.SelectedItem"/>.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

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
    public VisualType? SeriesType
    {
        get => _seriesType;
        set
        {
            if (!SetProperty(ref _seriesType, value)) return;
            OnPropertyChanged(nameof(SeriesTypeOption));
            _changed();
        }
    }

    /// <summary><see cref="SeriesType"/> as a list option (a combo box cannot select a null value).</summary>
    public Option<VisualType?> SeriesTypeOption
    {
        get => SeriesTypeOptions.First(o => o.Value == _seriesType);
        set => SeriesType = value?.Value;
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

    public FieldRef ToFieldRef() => new(Field.Name, Grain) { Labels = _labels };

    /// <summary>
    /// The texts the visual shows instead of some values of this dimension (keyed by the value's invariant text).
    /// Not for dates: their periods are shown by their grain.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Labels
    {
        get => _labels;
        set
        {
            _labels = value is { Count: > 0 } ? value : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the visual can show other texts for the values of this field: a dimension other than a date.</summary>
    public bool HasValueLabels => Field.Kind == FieldKind.Dimension && Field.DataType != DataType.Date;

    /// <summary>The editor of <see cref="Labels"/> (the field's values, loaded once from the model).</summary>
    internal ValueLabelsViewModel ValueLabels(ISemanticModel model) => _valueLabels ??= new ValueLabelsViewModel(this, model);

    /// <summary>Shows <paramref name="text"/> instead of <paramref name="value"/>; a blank text shows the value again.</summary>
    public void SetLabel(object? value, string? text)
    {
        Labels = ToFieldRef().WithLabel(value, text).Labels;
        _changed();
    }

    /// <summary>The format of this measure's values (Values shelf), edited in the properties panel.</summary>
    public ValueFormatViewModel Format => _format ??= new ValueFormatViewModel(null, _changed);

    /// <summary>Starts from a saved format (when a visual is loaded).</summary>
    internal void LoadFormat(ValueFormat? format) => _format = new ValueFormatViewModel(format, _changed);

    public MeasureRef ToMeasureRef() => new(Field.Name, HasAggregation && Aggregation != Field.DefaultAggregation ? Aggregation : null)
    {
        SeriesType = SeriesType,
        SecondaryAxis = SecondaryAxis,
        Time = Time,
        Format = _format?.ToFormat(),
    };
}

/// <summary>A sort direction: its <see cref="Symbol"/> stands for it on a chip.</summary>
public sealed class SortOption(ListSortDirection? direction, string symbol, Func<string> label) : Option<ListSortDirection?>(direction, label)
{
    public string Symbol { get; } = symbol;
}

/// <summary>
/// A filter on a dimension. Most dimensions list their values to check, as in Excel: every value checked does not
/// filter, unchecking one leaves it out, and "(Select all)" checks or unchecks every value shown. The filter is the
/// shorter list: "one of" the checked values (In) or "all except" the others (NotIn). A date dimension filters on a
/// period instead: between two dates, or relative to today (the current, last or previous months...).
/// </summary>
public sealed class FilterItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly ISemanticModel _model;
    private bool _isSelected;
    private ICommand? _selectCommand;
    private HashSet<string?>? _pendingSelection;
    private bool _isLoading = true;
    private Option<FilterOperator?> _periodKind;
    private DateTime? _from;
    private DateTime? _to;
    private int _count = 12;
    private Option<DateGrain> _unit;
    private string _search = "";
    private IReadOnlyList<FilterValueViewModel>? _visibleValues;
    private bool _addToFilter;
    // Checking several values at once changes the filter once
    private bool _toggling;
    private bool _isTruncated;
    // Whether the values not listed (beyond a truncated list) are kept by the filter
    private bool _othersChecked = true;
    // Values found by a search are announced once
    private bool _merging;
    private CancellationTokenSource? _searching;
    private Option<FilterOperator?> _condition;
    private string _number = "";
    private string _secondNumber = "";

    /// <summary>A list of values shows a search box from this number of values on.</summary>
    public const int SearchThreshold = 8;

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
        _model = model;
        // While loading, the values are announced once at the end
        Values.CollectionChanged += (_, _) => { if (!IsLoading && !_merging) OnValuesChanged(); };
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
        // A number dimension is filtered on a list of values or compared; a measure is only compared
        Conditions = field.DataType != DataType.Number ? [] :
        [
            IsMeasure ? new(null, () => Strings.ConditionAny) : new(@operator.IsComparison() ? FilterOperator.In : @operator, () => Strings.ConditionValues),
            new(FilterOperator.Range, () => Strings.ConditionRange),
            new(FilterOperator.GreaterThan, () => Strings.ConditionGreaterThan),
            new(FilterOperator.GreaterThanOrEqual, () => Strings.ConditionGreaterThanOrEqual),
            new(FilterOperator.LessThan, () => Strings.ConditionLessThan),
            new(FilterOperator.LessThanOrEqual, () => Strings.ConditionLessThanOrEqual),
        ];
        _condition = Conditions.FirstOrDefault(c => c.Value == @operator) ?? Conditions.FirstOrDefault()!;
        if (IsPeriod)
        {
            LoadPeriod(new FilterSpec(field.Name, @operator, selection?.ToArray() ?? [], grain));
            _isLoading = false;
            return;
        }
        if (@operator.IsComparison())
            LoadComparison(new FilterSpec(field.Name, @operator, selection?.ToArray() ?? []));
        if (IsMeasure)
        {
            // A measure has no list of values
            _isLoading = false;
            return;
        }
        _pendingSelection = selection?.ToHashSet();
        _ = LoadAsync(model);
    }

    public FieldDescriptor Field { get; }

    /// <summary>True while the filter is the designer's <see cref="DesignerViewModel.SelectedItem"/>.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Selects the filter in its designer (a click on it), whose properties panel then shows its editor.</summary>
    public ICommand SelectCommand => _selectCommand ??= new RelayCommand<object?>(_ => Selecting?.Invoke(this));

    internal Action<object>? Selecting { get; set; }

    /// <summary>The operator the filter was created with; <see cref="Condition"/> tells how it filters now.</summary>
    public FilterOperator Operator { get; }

    public string Caption => Field.Caption;

    /// <summary>A date dimension: filtered on a period rather than on a list of values.</summary>
    public bool IsPeriod => Field.DataType == DataType.Date;

    /// <summary>A measure of the visual: its groups are kept by comparing the measure with numbers.</summary>
    public bool IsMeasure => Field.Kind == FieldKind.Measure;

    public bool IsValueList => !IsPeriod && !IsMeasure && !IsComparison;

    /// <summary>Compared with numbers (see <see cref="Condition"/>).</summary>
    public bool IsComparison => Condition?.Value?.IsComparison() == true;

    /// <summary>A number dimension or a measure: <see cref="Conditions"/> are offered.</summary>
    public bool HasConditions => Conditions.Count > 0;

    /// <summary>How a number is filtered: on a list of values (a dimension), on any value (a measure), or compared.</summary>
    public IReadOnlyList<Option<FilterOperator?>> Conditions { get; }

    public Option<FilterOperator?> Condition
    {
        get => _condition;
        set
        {
            if (value is null || !SetProperty(ref _condition, value)) return;
            OnPropertyChanged(nameof(IsComparison));
            OnPropertyChanged(nameof(IsValueList));
            OnPropertyChanged(nameof(ShowSecondNumber));
            OnConditionChanged();
        }
    }

    /// <summary>The number compared with, or the minimum of a range, as typed (in <see cref="EasyBILocalization.FormatCulture"/>).</summary>
    public string Number
    {
        get => _number;
        set { if (SetProperty(ref _number, value ?? "")) OnConditionChanged(); }
    }

    /// <summary>The maximum of a range, as typed.</summary>
    public string SecondNumber
    {
        get => _secondNumber;
        set { if (SetProperty(ref _secondNumber, value ?? "")) OnConditionChanged(); }
    }

    public bool ShowSecondNumber => Condition?.Value == FilterOperator.Range;

    public ObservableCollection<FilterValueViewModel> Values { get; } = [];

    /// <summary>
    /// The <see cref="Values"/> matching <see cref="Search"/>, computed once per search: the values that do not match
    /// keep their state until a value found is checked or unchecked (see <see cref="AddToFilter"/>).
    /// </summary>
    public IReadOnlyList<FilterValueViewModel> VisibleValues => _visibleValues ??= Values.Where(v => TextMatch.Matches(v.Label, _search)).ToArray();

    /// <summary>Text searched in the values (see <see cref="TextMatch"/>).</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value ?? "")) return;
            OnPropertyChanged(nameof(IsSearching));
            OnVisibleValuesChanged();
            // The values beyond a truncated list are searched in the model
            if (IsTruncated) SearchTask = SearchModelAsync(_search);
        }
    }

    /// <summary>The values being searched in the model (tests wait for it).</summary>
    public Task SearchTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Whether the model has more values than listed (<see cref="DistinctValuesRequest.DefaultLimit"/>): a search
    /// asks the model for those containing it, and the values not listed keep the state of the filter.
    /// </summary>
    public bool IsTruncated
    {
        get => _isTruncated;
        private set => SetProperty(ref _isTruncated, value);
    }

    public bool IsSearching => _search.Trim().Length > 0;

    /// <summary>
    /// Whether every value shown is checked (true), none (false), or some (null): the "(Select all)" box, or "(Select
    /// all search results)" while searching. Setting it checks or unchecks every value shown, with one change of the
    /// filter.
    /// </summary>
    public bool? AllChecked
    {
        get
        {
            var visible = VisibleValues;
            if (visible.Count == 0) return false;
            var checkedCount = visible.Count(v => v.IsChecked);
            return checkedCount == visible.Count ? true : checkedCount == 0 ? false : null;
        }
        set
        {
            // A click on a box partly checked checks everything, as in Excel (WPF would uncheck)
            var isChecked = value == true || AllChecked is null;
            Toggle(() =>
            {
                foreach (var item in VisibleValues) item.SetChecked(isChecked);
                if (!IsSearching) _othersChecked = isChecked;
            });
        }
    }

    /// <summary>
    /// While searching, as Excel's "Add current selection to filter": when false, checking or unchecking a value found
    /// filters on the checked values found only, the others are unchecked; when true, the others keep their state.
    /// </summary>
    public bool AddToFilter
    {
        get => _addToFilter;
        set => SetProperty(ref _addToFilter, value);
    }

    /// <summary>Whether the values are many enough to need a search box (<see cref="SearchThreshold"/>).</summary>
    public bool ShowSearch => Values.Count >= SearchThreshold;

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
            if (IsComparison || IsMeasure) return ToFilterSpec() is { } comparison ? NumericFilters.Describe(comparison, Field.Format) : Strings.FilterAll;
            if (ToFilterSpec() is not { } filter) return Strings.FilterAll;
            if (filter.Values.Count == 0) return Strings.FilterNone;
            // Captions of the loaded values, the stored texts while loading
            var labels = Values.ToDictionary(v => InvariantValue.Format(v.Value) ?? "", v => v.Label);
            var listed = filter.Values.Select(v => labels.TryGetValue(v ?? "", out var label) ? label : v ?? "").ToList();
            var summary = listed.Count <= 2 ? string.Join(", ", listed) : $"{listed[0]}, {listed[1]} +{listed.Count - 2}";
            return filter.Operator == FilterOperator.NotIn ? Strings.Format(Strings.FilterExcept, summary) : summary;
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
        if (IsComparison)
        {
            var (first, second) = (ParseNumber(Number), ParseNumber(SecondNumber));
            return Condition.Value switch
            {
                FilterOperator.Range when first is null && second is null => null,
                FilterOperator.Range => NumericFilters.Range(Field.Name, first, second),
                var comparison => first is { } value ? NumericFilters.Compare(Field.Name, comparison!.Value, value) : null,
            };
        }
        if (IsMeasure) return null;
        // While values are loading, keep the original filter
        if (IsLoading) return _pendingSelection is { } pending ? new FilterSpec(Field.Name, Operator, [.. pending]) : null;
        // As in Excel, every value checked does not filter; otherwise the shorter list: the values kept, or those left out
        var (kept, left) = (Values.Where(v => v.IsChecked).ToArray(), Values.Where(v => !v.IsChecked).ToArray());
        // Beyond a truncated list, the values not listed keep their state: "all except" when they are kept
        var allExcept = IsTruncated ? _othersChecked : left.Length < kept.Length;
        if (left.Length == 0 && (allExcept || !IsTruncated)) return null;
        return allExcept
            ? new FilterSpec(Field.Name, FilterOperator.NotIn, left.Select(v => InvariantValue.Format(v.Value)).ToArray())
            : new FilterSpec(Field.Name, FilterOperator.In, kept.Select(v => InvariantValue.Format(v.Value)).ToArray());
    }

    private static DateOnly? Day(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

    private static double? ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, EasyBILocalization.FormatCulture, out var number)
            ? number
            : null;

    private static string ShowNumber(double? number) => number?.ToString("G", EasyBILocalization.FormatCulture) ?? "";

    private void LoadComparison(FilterSpec filter)
    {
        var (first, second) = NumericFilters.Bounds(filter);
        (_number, _secondNumber) = (ShowNumber(first), ShowNumber(second));
    }

    private void OnConditionChanged()
    {
        OnPropertyChanged(nameof(Summary));
        _changed();
    }

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
            var found = await model.GetDistinctValuesAsync(new DistinctValuesRequest(Field.Name));
            IsTruncated = found.IsTruncated;
            // Without a filter, every value is checked; "one of" leaves out the others, "all except" keeps them
            _othersChecked = _pendingSelection is null || Operator == FilterOperator.NotIn;
            foreach (var value in found.Values)
                Values.Add(new FilterValueViewModel(value, ValueFormatter.Display(value), IsCheckedByFilter(value), OnValueToggled));
            // A truncated list may miss values of the filter: listed too, so that a change keeps them
            if (found.IsTruncated && _pendingSelection is { } pending)
            {
                var listed = Values.Select(v => InvariantValue.Format(v.Value)).ToHashSet();
                var type = Field.DataType switch { DataType.Number => typeof(double), DataType.Boolean => typeof(bool), _ => typeof(string) };
                foreach (var value in pending.Where(t => !listed.Contains(t)).Select(t => InvariantValue.Parse(t, type)))
                    Insert(new FilterValueViewModel(value, ValueFormatter.Display(value), IsCheckedByFilter(value), OnValueToggled));
            }
        }
        finally
        {
            IsLoading = false;
            _pendingSelection = null;
            OnPropertyChanged(nameof(Summary));
            OnValuesChanged();
        }
    }

    private bool IsCheckedByFilter(object? value) =>
        _pendingSelection is not { } pending || pending.Contains(InvariantValue.Format(value)) != (Operator == FilterOperator.NotIn);

    // In the order of the values, as the model gives them
    private void Insert(FilterValueViewModel item)
    {
        var index = 0;
        while (index < Values.Count && Compare(Values[index].Value, item.Value) <= 0) index++;
        Values.Insert(index, item);
    }

    private static int Compare(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string x, string y) => string.CompareOrdinal(x, y),
        _ when a.GetType() == b.GetType() && a is IComparable comparable => comparable.CompareTo(b),
        _ => string.CompareOrdinal(a.ToString(), b.ToString()),
    };

    // The model's values containing the longest word searched: the list keeps those matching every word
    private async Task SearchModelAsync(string search)
    {
        _searching?.Cancel();
        if (search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).OrderByDescending(w => w.Length).FirstOrDefault() is not { } word)
            return;
        var cancellation = _searching = new CancellationTokenSource();
        try
        {
            var found = await _model.GetDistinctValuesAsync(new DistinctValuesRequest(Field.Name) { Search = word }, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            var listed = Values.Select(v => InvariantValue.Format(v.Value)).ToHashSet();
            _merging = true;
            try
            {
                foreach (var value in found.Values.Where(v => !listed.Contains(InvariantValue.Format(v))))
                    Insert(new FilterValueViewModel(value, ValueFormatter.Display(value), _othersChecked, OnValueToggled));
            }
            finally
            {
                _merging = false;
            }
            OnValuesChanged();
        }
        catch (Exception) when (cancellation.IsCancellationRequested)
        {
            // A newer search replaces it
        }
    }

    private void OnValuesChanged()
    {
        OnVisibleValuesChanged();
        OnPropertyChanged(nameof(ShowSearch));
    }

    private void OnVisibleValuesChanged()
    {
        _visibleValues = null;
        OnPropertyChanged(nameof(VisibleValues));
        OnPropertyChanged(nameof(AllChecked));
    }

    private void OnValueToggled()
    {
        if (_toggling) return;
        Toggle(() => { });
    }

    // Checks or unchecks values, then changes the filter once: while searching, on the values found only (AddToFilter)
    private void Toggle(Action change)
    {
        _toggling = true;
        try
        {
            change();
            if (IsSearching && !AddToFilter)
            {
                var found = VisibleValues.ToHashSet();
                foreach (var item in Values.Where(v => !found.Contains(v))) item.SetChecked(false);
                _othersChecked = false;
            }
        }
        finally
        {
            _toggling = false;
        }
        OnPropertyChanged(nameof(AllChecked));
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

    // Checked by its filter: the filter announces the change
    internal void SetChecked(bool isChecked) => SetProperty(ref _isChecked, isChecked, nameof(IsChecked));
}
