using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// The ways measures can be seen through time (<see cref="MeasureTime"/>): the periods they are compared with, what a
/// comparison shows, and the accumulations along the dates. EasyBI's own come first; an application adds its own
/// (with a prefixed id, such as <c>retail.week364</c>), changes or removes EasyBI's. The designer offers the entries of
/// the context's catalog, and a <see cref="QuerySemanticModel"/> computes with its own: both are
/// <see cref="Default"/> unless the application gives them another one.
/// </summary>
public sealed class TimeCatalog
{
    private readonly Entries<ComparisonPeriod> _periods = new(p => p.Id, "period");
    private readonly Entries<ComparisonOutput> _outputs = new(o => o.Id, "presentation");
    private readonly Entries<Accumulation> _accumulations = new(a => a.Id, "accumulation");

    public TimeCatalog()
        : this(null)
    {
    }

    // A copy of source, or EasyBI's own entries
    private TimeCatalog(TimeCatalog? source)
    {
        if (source is not null)
        {
            foreach (var period in source.Periods) _periods.AddBuiltIn(period);
            foreach (var output in source.Outputs) _outputs.AddBuiltIn(output);
            foreach (var accumulation in source.Accumulations) _accumulations.AddBuiltIn(accumulation);
            return;
        }

        _periods.AddBuiltIn(new(TimeIds.PreviousPeriod, () => Strings.TimePeriodPrevious, PreviousPeriod) { ShortCaption = PreviousPeriodCaption });
        _periods.AddBuiltIn(new(TimeIds.LastYear, () => Strings.TimePeriodLastYear, _ => TimeShift.Of(-1, TimeUnit.Year))
        {
            ShortCaption = _ => Short(TimeUnit.Year, 1),
        });
        _periods.AddBuiltIn(new(TimeIds.Offset, () => Strings.TimePeriodOffset, c => TimeShift.Of(-c.Time.Count, c.Time.Unit))
        {
            ShortCaption = c => Short(c.Time.Unit, c.Time.Count),
            HasOffset = true,
        });

        _outputs.AddBuiltIn(new(TimeIds.Value, () => Strings.TimeOutputValue, (_, compared) => compared)
        {
            ColumnCaption = () => Strings.TimeOutputValueColumn,
        });
        _outputs.AddBuiltIn(new(TimeIds.Change, () => Strings.TimeOutputChange, (value, compared) => value - compared)
        {
            ColumnCaption = () => Strings.TimeOutputChangeColumn,
            IsChange = true,
            Symbol = "Δ",
        });
        _outputs.AddBuiltIn(new(TimeIds.PercentChange, () => Strings.TimeOutputPercentChange,
            (value, compared) => compared is { } c && c != 0 && value is { } v ? (v - c) / Math.Abs(c) : null)
        {
            ColumnCaption = () => Strings.TimeOutputPercentChangeColumn,
            Format = "P1",
            IsChange = true,
            Symbol = "Δ%",
        });

        _accumulations.AddBuiltIn(new(TimeIds.YearToDate, () => Strings.TimeAccumulationYearToDate, d => new DateTime(d.Year, 1, 1))
        {
            ColumnCaption = () => Strings.TimeAccumulationYearToDateColumn,
        });
        _accumulations.AddBuiltIn(new(TimeIds.QuarterToDate, () => Strings.TimeAccumulationQuarterToDate, d => new DateTime(d.Year, (d.Month - 1) / 3 * 3 + 1, 1))
        {
            ColumnCaption = () => Strings.TimeAccumulationQuarterToDateColumn,
        });
        _accumulations.AddBuiltIn(new(TimeIds.MonthToDate, () => Strings.TimeAccumulationMonthToDate, d => new DateTime(d.Year, d.Month, 1))
        {
            ColumnCaption = () => Strings.TimeAccumulationMonthToDateColumn,
        });
        _accumulations.AddBuiltIn(new(TimeIds.Running, () => Strings.TimeAccumulationRunning, _ => DateTime.MinValue)
        {
            ColumnCaption = () => Strings.TimeAccumulationRunningColumn,
        });
    }

    /// <summary>
    /// The catalog of the application, used unless a context or a model is given another one: an application adds its
    /// entries to it once, at startup.
    /// </summary>
    public static TimeCatalog Default { get; } = new();

    public IReadOnlyList<ComparisonPeriod> Periods => _periods.Items;

    public IReadOnlyList<ComparisonOutput> Outputs => _outputs.Items;

    public IReadOnlyList<Accumulation> Accumulations => _accumulations.Items;

    public ComparisonPeriod? Period(string id) => _periods.Find(id);

    public ComparisonOutput? Output(string id) => _outputs.Find(id);

    public Accumulation? Accumulation(string id) => _accumulations.Find(id);

    /// <summary>
    /// Adds a period of the application. Its id has a prefix, such as <c>retail.week364</c>, so that it never takes the
    /// name of one EasyBI adds later.
    /// </summary>
    /// <exception cref="ArgumentException">The id has no prefix.</exception>
    /// <exception cref="InvalidOperationException">The catalog already has the id (<c>Replace</c> changes it).</exception>
    public TimeCatalog Add(ComparisonPeriod period) => Do(() => _periods.Add(period));

    /// <inheritdoc cref="Add(ComparisonPeriod)"/>
    public TimeCatalog Add(ComparisonOutput output) => Do(() => _outputs.Add(output));

    /// <inheritdoc cref="Add(ComparisonPeriod)"/>
    public TimeCatalog Add(Accumulation accumulation) => Do(() => _accumulations.Add(accumulation));

    /// <summary>Changes an entry, EasyBI's or the application's, found by its id.</summary>
    /// <exception cref="InvalidOperationException">The catalog does not have the id.</exception>
    public TimeCatalog Replace(ComparisonPeriod period) => Do(() => _periods.Replace(period));

    /// <inheritdoc cref="Replace(ComparisonPeriod)"/>
    public TimeCatalog Replace(ComparisonOutput output) => Do(() => _outputs.Replace(output));

    /// <inheritdoc cref="Replace(ComparisonPeriod)"/>
    public TimeCatalog Replace(Accumulation accumulation) => Do(() => _accumulations.Replace(accumulation));

    /// <summary>
    /// Removes a period: the designer no longer offers it, and the visuals using it show empty values. Returns false when
    /// the catalog does not have it.
    /// </summary>
    public bool RemovePeriod(string id) => _periods.Remove(id);

    /// <inheritdoc cref="RemovePeriod"/>
    public bool RemoveOutput(string id) => _outputs.Remove(id);

    /// <inheritdoc cref="RemovePeriod"/>
    public bool RemoveAccumulation(string id) => _accumulations.Remove(id);

    /// <summary>
    /// The calculation in a few characters, for a chip of the designer: "Cumul", "Δ% Y-1", "M-3". The period's short
    /// caption is the one of a visual without a date on its axis.
    /// </summary>
    public string Badge(MeasureTime time)
    {
        var parts = new List<string>();
        if (time.Accumulation is not null) parts.Add(Strings.TimeBadgeAccumulation);
        if (time.Period is { } id)
        {
            if (Output(time.Show)?.Symbol is { } symbol) parts.Add(symbol);
            parts.Add(Period(id) is { } period
                ? period.ShortCaption?.Invoke(new(DateGrain.None, null, null, time)) ?? period.Caption()
                : id);
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// This catalog with the periods and presentations the end user added to a report, after its own: a copy, this one
    /// is left as it is (itself when there are none). An entry of the report with the id of one of the catalog
    /// replaces it.
    /// </summary>
    public TimeCatalog With(IEnumerable<ReportComparisonPeriod>? periods, IEnumerable<ReportComparisonOutput>? outputs)
    {
        var (addedPeriods, addedOutputs) = (periods?.ToArray() ?? [], outputs?.ToArray() ?? []);
        if (addedPeriods.Length == 0 && addedOutputs.Length == 0) return this;
        var catalog = new TimeCatalog(this);
        foreach (var period in addedPeriods) catalog._periods.Set(ToPeriod(period));
        foreach (var output in addedOutputs) catalog._outputs.Set(ToOutput(output));
        return catalog;
    }

    /// <summary>A period of a report as an entry of a catalog: its count of units back, shown by its name.</summary>
    public static ComparisonPeriod ToPeriod(ReportComparisonPeriod period) =>
        new(period.Id, () => period.Caption, _ => TimeShift.Of(-Math.Max(1, period.Count), period.Unit))
        {
            ShortCaption = _ => period.Caption,
        };

    /// <summary>A presentation of a report as an entry of a catalog: its formula, its name in the captions of columns.</summary>
    public static ComparisonOutput ToOutput(ReportComparisonOutput output)
    {
        // The name goes in a template of captions: its braces are kept as they are
        var name = output.Caption.Replace("{", "{{").Replace("}", "}}");
        return new(output.Id, () => output.Caption, ComparisonFormula.Compile(output.Formula))
        {
            ColumnCaption = () => Strings.Format(Strings.TimeOutputCustomColumn, "{0}", "{1}", name),
            Format = output.Format,
            IsChange = output.IsChange,
        };
    }

    private TimeCatalog Do(Action change)
    {
        change();
        return this;
    }

    // The period just before: a month before each month of the axis; without a date on the axis, the period of the
    // filters (a month before a month, the 30 days before the last 30).
    private static TimeShift? PreviousPeriod(TimeComparisonContext context) => context.AxisGrain switch
    {
        DateGrain.Year => TimeShift.Of(-1, TimeUnit.Year),
        DateGrain.Quarter => TimeShift.Of(-1, TimeUnit.Quarter),
        DateGrain.Month => TimeShift.Of(-1, TimeUnit.Month),
        DateGrain.Day => TimeShift.Of(-1, TimeUnit.Day),
        _ => FilteredLength(context) is var (months, days) ? new TimeShift(-months, -days) : null,
    };

    private static string PreviousPeriodCaption(TimeComparisonContext context) => context.AxisGrain switch
    {
        DateGrain.Year => Short(TimeUnit.Year, 1),
        DateGrain.Quarter => Short(TimeUnit.Quarter, 1),
        DateGrain.Month => Short(TimeUnit.Month, 1),
        DateGrain.Day => Short(TimeUnit.Day, 1),
        _ => FilteredLength(context) switch
        {
            (12, 0) => Short(TimeUnit.Year, 1),
            (3, 0) => Short(TimeUnit.Quarter, 1),
            (1, 0) => Short(TimeUnit.Month, 1),
            _ => Strings.Format(Strings.TimeShortCaption, Strings.TimeLetterPeriod, 1),
        },
    };

    /// <summary>The length of the filtered days: in months when they are whole months, in days otherwise.</summary>
    private static (int Months, int Days)? FilteredLength(TimeComparisonContext context)
    {
        if (context.From is not { } from || context.To is not { } to || to <= from) return null;
        if (from.Day == 1 && to.Day == 1)
            return ((to.Year - from.Year) * 12 + to.Month - from.Month, 0);
        return (0, (int)(to - from).TotalDays);
    }

    /// <summary>"Y-1", "M-3": the unit's letter and how many back.</summary>
    internal static string Short(TimeUnit unit, int count) => Strings.Format(Strings.TimeShortCaption, unit switch
    {
        TimeUnit.Day => Strings.TimeLetterDay,
        TimeUnit.Week => Strings.TimeLetterWeek,
        TimeUnit.Month => Strings.TimeLetterMonth,
        TimeUnit.Quarter => Strings.TimeLetterQuarter,
        _ => Strings.TimeLetterYear,
    }, count);

    /// <summary>Entries kept in order, EasyBI's first; the application's have a prefixed id.</summary>
    private sealed class Entries<T>(Func<T, string> id, string kind) where T : class
    {
        private readonly List<T> _items = [];

        public IReadOnlyList<T> Items => _items;

        public T? Find(string key) => _items.Find(e => id(e) == key);

        public void AddBuiltIn(T entry) => _items.Add(entry);

        public void Add(T entry)
        {
            var key = id(entry);
            if (key.IndexOf('.') is <= 0 || key.EndsWith(".", StringComparison.Ordinal))
                throw new ArgumentException($"The id of a {kind} declared by an application has a prefix, such as 'company.{key}': '{key}' has none.", nameof(entry));
            if (Find(key) is not null) throw new InvalidOperationException($"The {kind} '{key}' is already declared: replace it instead.");
            _items.Add(entry);
        }

        public void Replace(T entry)
        {
            var index = _items.FindIndex(e => id(e) == id(entry));
            if (index < 0) throw new InvalidOperationException($"The {kind} '{id(entry)}' is not declared: add it instead.");
            _items[index] = entry;
        }

        public bool Remove(string key) => _items.RemoveAll(e => id(e) == key) > 0;

        // An entry of a report: replaces the one with its id, if any
        public void Set(T entry)
        {
            var index = _items.FindIndex(e => id(e) == id(entry));
            if (index >= 0) _items[index] = entry;
            else _items.Add(entry);
        }
    }
}
