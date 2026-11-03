using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Measures seen through time (<see cref="MeasureRef.Time"/>), on top of any <see cref="QuerySemanticModel"/>, with the
/// entries of its <see cref="QuerySemanticModel.Time"/> catalog. The query runs as usual, and, for each period the
/// measures are compared with, again with its dates moved back to that period (period filters, filters on periods);
/// each row is matched with the row of its dates moved back. A period under way (this year, the last 12 months, the
/// current month clicked in a chart) is compared with the same days of the other period: up to today's date, since the
/// data of the current period stops today. An accumulation adds the values up along the date axis, starting again with
/// each of its periods; compared, the totals of both periods are.
/// </summary>
public static class TimeCalculations
{
    /// <summary>Runs a query whose measures may be seen through time.</summary>
    public static Task<ResultSet> ExecuteWithTimeAsync(this QuerySemanticModel model, AnalyticQuery query, CancellationToken cancellationToken = default) =>
        model.ExecuteWithTimeAsync(query, DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    /// <summary>
    /// Runs a query whose measures may be seen through time, on <paramref name="today"/>: its periods relative to today
    /// are the days they keep on that date.
    /// </summary>
    public static async Task<ResultSet> ExecuteWithTimeAsync(this QuerySemanticModel model, AnalyticQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        query = query with { Filters = PeriodFilters.OnDay(query.Filters, today) };
        if (query.Measures.All(m => m.Time is null))
            return await model.ExecuteAsync(query, cancellationToken).ConfigureAwait(false);

        // The model computes each measure once; a sort on a time column is applied once the columns exist
        var timeKeys = query.Measures.Where(m => m.Time is not null).Select(m => m.Key).ToHashSet();
        var sortsOnTime = query.Sort?.Any(s => timeKeys.Contains(s.Key)) == true;
        var baseQuery = query with
        {
            Measures = query.Measures.Select(m => m with { Time = null }).DistinctBy(m => m.Key).ToArray(),
            Sort = sortsOnTime ? null : query.Sort,
            Top = sortsOnTime ? null : query.Top,
        };
        var current = await model.ExecuteAsync(baseQuery, cancellationToken).ConfigureAwait(false);

        // One more query for each period the measures are compared with
        var metadata = model.Metadata;
        var catalog = model.Time;
        var context = Context(metadata, query, today);
        var shifts = new Dictionary<MeasureRef, TimeShift?>();
        var compared = new Dictionary<TimeShift, ResultSet>();
        foreach (var measure in query.Measures.Where(m => m.Time?.Period is not null))
        {
            var shift = HasDates(metadata, query) ? catalog.Period(measure.Time!.Period!)?.Shift(context with { Time = measure.Time }) : null;
            shifts[measure] = shift;
            if (shift is not { } s || compared.ContainsKey(s)) continue;
            // Groups kept by a filter on a measure keep their value in the other period, whatever it is
            var shifted = baseQuery with
            {
                Filters = query.Filters?
                    .Where(f => !NumericFilters.IsOnMeasure(f, metadata, query.Calculations))
                    .Select(f => ShiftFilter(metadata, f, s, today))
                    .ToArray(),
                Sort = null,
                Top = null,
            };
            compared[s] = await model.ExecuteAsync(shifted, cancellationToken).ConfigureAwait(false);
        }

        var result = Combine(metadata, catalog, query, context, current, shifts, compared);
        return sortsOnTime ? SortAndLimit(result, query.Sort!, query.Top) : result;
    }

    // Without a date in the groups or the filters, another period selects the same rows: it is left empty instead
    private static bool HasDates(ModelMetadata metadata, AnalyticQuery query) =>
        query.GroupBy.Any(g => IsDate(metadata, g.Field))
        || query.Filters?.Any(f => FiltersDates(metadata, f)) == true;

    private static bool FiltersDates(ModelMetadata metadata, FilterSpec filter) =>
        filter.Operator == FilterOperator.AnyOf
            ? filter.Alternatives?.Any(group => group.Any(f => FiltersDates(metadata, f))) == true
            : filter.Operator.IsPeriod() || filter.Grain != DateGrain.None || IsDate(metadata, filter.Field);

    private static bool IsDate(ModelMetadata metadata, string field) =>
        metadata.Fields.FirstOrDefault(f => f.Name == field)?.DataType == DataType.Date;

    /// <summary>
    /// The granularity of the date on the axis, and the days the first date filter keeps (a range left open ends today,
    /// as its data does).
    /// </summary>
    private static TimeComparisonContext Context(ModelMetadata metadata, AnalyticQuery query, DateOnly today)
    {
        var axis = query.GroupBy.FirstOrDefault(g => IsDate(metadata, g.Field));
        var grain = axis is null ? DateGrain.None : axis.Grain == DateGrain.None ? DateGrain.Day : axis.Grain;
        var range = (query.Filters ?? []).Where(f => f.Operator != FilterOperator.AnyOf && FiltersDates(metadata, f))
            .Select(f => Days(f, today)).FirstOrDefault(r => r.Start is not null || r.End is not null);
        return new(grain, range.Start, range.Start is not null ? range.End ?? Tomorrow(today) : range.End, MeasureTime.PreviousYear);
    }

    /// <summary>The days a date filter keeps: a period, periods clicked in a chart (2024, 202405), or days.</summary>
    private static (DateTime? Start, DateTime? End) Days(FilterSpec filter, DateOnly today)
    {
        if (filter.Operator.IsPeriod()) return PeriodFilters.Range(filter, today);
        if (filter.Operator != FilterOperator.In || filter.Values.Count == 0 || filter.Values.Any(v => v is null)) return default;
        if (filter.Grain is DateGrain.Year or DateGrain.Quarter or DateGrain.Month)
        {
            var periods = filter.Values.Select(v => new DatePeriod(filter.Grain, int.Parse(v!, CultureInfo.InvariantCulture))).ToList();
            return (periods.Min(StartOf), periods.Max(p => EndOf(p)));
        }
        var days = filter.Values.Select(v => DayOf(v!)).ToList();
        return (days.Min(), days.Max().AddDays(1));
    }

    private static DateTime Tomorrow(DateOnly today) => today.AddDays(1).ToDateTime(TimeOnly.MinValue);

    private static FilterSpec ShiftFilter(ModelMetadata metadata, FilterSpec filter, TimeShift shift, DateOnly today)
    {
        if (filter.Operator == FilterOperator.AnyOf)
            return filter with { Alternatives = filter.Alternatives?.Select(group => (IReadOnlyList<FilterSpec>)[.. group.Select(f => ShiftFilter(metadata, f, shift, today))]).ToArray() };
        if (filter.Operator.IsPeriod())
        {
            var (start, end) = PeriodFilters.Range(filter, today);
            return ShiftedRange(filter.Field, start, end, shift, today);
        }
        if (filter.Grain is DateGrain.Year or DateGrain.Quarter or DateGrain.Month && filter.Values.All(v => v is not null))
        {
            var periods = filter.Values.Select(v => new DatePeriod(filter.Grain, int.Parse(v!, CultureInfo.InvariantCulture))).ToList();
            // Periods clicked in a chart become their days moved back: a month before each month of a year is not a year
            if (filter.Operator == FilterOperator.In && periods.Count > 0)
            {
                var ranges = periods.Select(p => ShiftedRange(filter.Field, StartOf(p), EndOf(p), shift, today)).ToList();
                return ranges.Count == 1 ? ranges[0] : FilterSpec.AnyOf([.. ranges.Select(r => (IReadOnlyList<FilterSpec>)[r])]);
            }
            return filter with { Values = [.. periods.Select(p => KeyOf(Shift(p, shift)))] };
        }
        if (filter.Grain != DateGrain.None || !IsDate(metadata, filter.Field) || filter.Operator is not (FilterOperator.In or FilterOperator.NotIn))
            return filter;
        return filter with { Values = filter.Values.Select(v => v is null ? null : ShiftDay(v, shift)).ToArray() };
    }

    /// <summary>
    /// The days from <paramref name="start"/> (included) to <paramref name="end"/> (excluded, null for open) moved by
    /// <paramref name="shift"/>. A range under way stops at today's date moved back: its data stops today, comparing it
    /// with the whole other period would make it look like a drop.
    /// </summary>
    private static FilterSpec ShiftedRange(string field, DateTime? start, DateTime? end, TimeShift shift, DateOnly today)
    {
        var tomorrow = Tomorrow(today);
        if ((end is null || end > tomorrow) && (start is null || start < tomorrow)) end = tomorrow;
        return PeriodFilters.Between(field,
            start is { } s ? DateOnly.FromDateTime(shift.Apply(s)) : null,
            end is { } e ? DateOnly.FromDateTime(shift.Apply(e).AddDays(-1)) : null);
    }

    // A day of a filter (yyyy-MM-dd, or an invariant date and time) moved by the shift
    private static string? ShiftDay(string value, TimeShift shift) =>
        value.Length == 10
            ? DateOnly.FromDateTime(shift.Apply(DayOf(value))).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : InvariantValue.Format(shift.Apply((DateTime)InvariantValue.Parse(value, typeof(DateTime))!));

    private static DateTime DayOf(string value) =>
        value.Length == 10 ? DateOnly.Parse(value, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue) : (DateTime)InvariantValue.Parse(value, typeof(DateTime))!;

    private static ResultSet Combine(ModelMetadata metadata, TimeCatalog catalog, AnalyticQuery query, TimeComparisonContext context,
        ResultSet current, IReadOnlyDictionary<MeasureRef, TimeShift?> shifts, IReadOnlyDictionary<TimeShift, ResultSet> compared)
    {
        var groups = query.GroupBy.Count;
        var dateColumn = Enumerable.Range(0, groups).Cast<int?>().FirstOrDefault(i => IsDate(metadata, query.GroupBy[i!.Value].Field));
        // The rows of each other period by their groups, and their totals by accumulation
        var lookups = compared.ToDictionary(c => c.Key, c => c.Value.Rows.Select((row, index) => (row, index))
            .GroupBy(r => KeyOf(r.row, groups, null)).ToDictionary(g => g.Key, g => g.First().index));

        var columns = current.Columns.Take(groups).ToList();
        var cells = new List<Func<object?[], int, object?>>();
        foreach (var measure in query.Measures)
        {
            var source = current.IndexOf((measure with { Time = null }).Key);
            var baseColumn = current.Columns[source];
            var time = measure.Time;
            var accumulation = time?.Accumulation is { } accumulationId ? catalog.Accumulation(accumulationId) : null;
            var caption = accumulation is null ? baseColumn.Caption
                : Strings.Format(accumulation.ColumnCaption?.Invoke() ?? "{0}, " + accumulation.Caption(), baseColumn.Caption);
            object?[] Values(ResultSet result) => accumulation is null
                ? [.. result.Rows.Select(r => r[source])]
                : Accumulate(result, source, groups, dateColumn, accumulation.Start);
            var values = Values(current);

            // An entry the catalog does not have: empty rather than the plain value under the calculation's name
            var known = time?.Accumulation is null || accumulation is not null;
            if (time is null || time.Period is null)
            {
                columns.Add(baseColumn with { Key = measure.Key, Caption = caption });
                cells.Add((_, index) => known ? values[index] : null);
                continue;
            }

            var period = catalog.Period(time.Period);
            var output = catalog.Output(time.Show);
            var shift = shifts.GetValueOrDefault(measure);
            var shortCaption = period is null ? time.Period : period.ShortCaption?.Invoke(context with { Time = time }) ?? period.Caption();
            columns.Add(baseColumn with
            {
                Key = measure.Key,
                Caption = Strings.Format(output?.ColumnCaption?.Invoke() ?? "{0} {1}", caption, shortCaption),
                Format = output?.Format ?? baseColumn.Format,
                Change = output?.IsChange == true ? measure.Favorable ?? metadata.Fields.FirstOrDefault(f => f.Name == measure.Measure)?.Favorable ?? Favorable.Higher : null,
            });
            if (!known || output is null || shift is not { } s || !compared.TryGetValue(s, out var other))
            {
                cells.Add((_, _) => null);
                continue;
            }
            var otherValues = Values(other);
            var lookup = lookups[s];
            cells.Add((row, index) =>
            {
                // A missing date has no other period: its group is not the missing dates of another period
                var before = dateColumn is { } date && row[date] is null
                    ? null
                    : lookup.TryGetValue(KeyOf(row, groups, s), out var match) ? otherValues[match] : null;
                return output.Compute(Number(values[index]), Number(before));
            });
        }
        var rows = current.Rows.Select((row, index) => row.Take(groups).Concat(cells.Select(cell => cell(row, index))).ToArray()).ToList();
        return new ResultSet(columns, rows);
    }

    /// <summary>
    /// The total of a measure since the start of each row's period (<paramref name="start"/>), along the date column; the
    /// value itself without one.
    /// </summary>
    private static object?[] Accumulate(ResultSet result, int source, int groups, int? dateColumn, Func<DateTime, DateTime> start)
    {
        var totals = new object?[result.Rows.Count];
        if (dateColumn is not { } date)
        {
            for (var i = 0; i < totals.Length; i++) totals[i] = Number(result.Rows[i][source]);
            return totals;
        }
        var partitions = Enumerable.Range(0, result.Rows.Count).GroupBy(i =>
            string.Join("\u001f", Enumerable.Range(0, groups).Where(g => g != date).Select(g => KeyPart(result.Rows[i][g])))
            + "\u001f" + (DateOf(result.Rows[i][date]) is { } d ? start(d).Ticks.ToString(CultureInfo.InvariantCulture) : ""));
        foreach (var partition in partitions)
        {
            double? total = 0;
            foreach (var i in partition.OrderBy(i => SortKey(result.Rows[i][date])))
            {
                total += Number(result.Rows[i][source]) ?? 0;
                totals[i] = total;
            }
        }
        return totals;
    }

    private static ResultSet SortAndLimit(ResultSet result, IReadOnlyList<SortSpec> sort, int? top)
    {
        IEnumerable<object?[]> rows = result.Rows;
        IOrderedEnumerable<object?[]>? ordered = null;
        foreach (var spec in sort)
        {
            var index = result.IndexOf(spec.Key);
            var comparer = new EmptyLast(spec.Descending);
            ordered = ordered is null ? rows.OrderBy(r => r[index], comparer) : ordered.ThenBy(r => r[index], comparer);
        }
        var sorted = (ordered ?? rows).ToList();
        return new ResultSet(result.Columns, top is { } n ? sorted.Take(n).ToList() : sorted);
    }

    /// <summary>Orders values one way or the other, empty values last either way.</summary>
    private sealed class EmptyLast(bool descending) : IComparer<object?>
    {
        public int Compare(object? x, object? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => (descending ? -1 : 1) * Comparer<object>.Default.Compare(Number(x) ?? x, Number(y) ?? y),
        };
    }

    // --- Values

    private static double? Number(object? value) => value is null or DatePeriod ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static int Months(DateGrain grain) => grain switch { DateGrain.Quarter => 3, DateGrain.Month => 1, _ => 12 };

    private static DateTime StartOf(DatePeriod period) => period.Grain switch
    {
        DateGrain.Quarter => new DateTime(period.Key / 10, (period.Key % 10 - 1) * 3 + 1, 1),
        DateGrain.Month => new DateTime(period.Key / 100, period.Key % 100, 1),
        _ => new DateTime(period.Key, 1, 1),
    };

    private static DateTime EndOf(DatePeriod period) => StartOf(period).AddMonths(Months(period.Grain));

    private static DatePeriod PeriodOf(DateTime date, DateGrain grain) => new(grain, grain switch
    {
        DateGrain.Quarter => date.Year * 10 + (date.Month + 2) / 3,
        DateGrain.Month => date.Year * 100 + date.Month,
        _ => date.Year,
    });

    // The period holding the first day of a period moved by the shift: a month before 2024-05 is 2024-04
    private static DatePeriod Shift(DatePeriod period, TimeShift shift) => PeriodOf(shift.Apply(StartOf(period)), period.Grain);

    private static string KeyOf(DatePeriod period) => period.Key.ToString(CultureInfo.InvariantCulture);

    private static DateTime? DateOf(object? value) => value switch
    {
        DatePeriod p => StartOf(p),
        DateTime d => d,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset d => d.DateTime,
        _ => null,
    };

    private static long SortKey(object? date) => date switch
    {
        DatePeriod p => p.Key,
        DateTime d => d.Ticks,
        DateOnly d => d.DayNumber,
        DateTimeOffset d => d.UtcTicks,
        _ => 0,
    };

    // The groups of a row, its dates moved by the shift (null: as they are)
    private static string KeyOf(object?[] row, int groups, TimeShift? shift) =>
        string.Join("\u001f", row.Take(groups).Select(v => KeyPart(shift is { } s ? Shifted(v, s) : v)));

    private static object? Shifted(object? value, TimeShift shift) => value switch
    {
        DatePeriod p => Shift(p, shift),
        DateTime d => shift.Apply(d),
        DateOnly d => DateOnly.FromDateTime(shift.Apply(d.ToDateTime(TimeOnly.MinValue))),
        DateTimeOffset d => d.AddMonths(shift.Months).AddDays(shift.Days),
        _ => value,
    };

    private static string KeyPart(object? value) => value switch
    {
        null => "\u0000",
        DatePeriod p => $"{p.Grain}:{p.Key}",
        _ => InvariantValue.Format(value) ?? "",
    };
}
