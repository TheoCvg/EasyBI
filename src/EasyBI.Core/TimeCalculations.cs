using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Measures seen through time (<see cref="MeasureRef.Time"/>), on top of any <see cref="ISemanticModel"/>: the query runs
/// as usual, and, for a comparison with the previous year, again with its dates shifted back one year (period filters,
/// filters on periods); rows are matched on their groups, dates moved forward one year. The year-to-date total adds
/// the values up along the date axis, year by year.
/// </summary>
public static class TimeCalculations
{
    /// <summary>Runs a query whose measures may be seen through time.</summary>
    public static async Task<ResultSet> ExecuteWithTimeAsync(this ISemanticModel model, AnalyticQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Measures.All(m => m.Time == TimeCalculation.None))
            return await model.ExecuteAsync(query, cancellationToken).ConfigureAwait(false);

        // The model computes each measure once; a sort on a time column is applied once the columns exist
        var timeKeys = query.Measures.Where(m => m.Time != TimeCalculation.None).Select(m => m.Key).ToHashSet();
        var sortsOnTime = query.Sort?.Any(s => timeKeys.Contains(s.Key)) == true;
        var baseQuery = query with
        {
            Measures = query.Measures.Select(m => m with { Time = TimeCalculation.None }).DistinctBy(m => m.Key).ToArray(),
            Sort = sortsOnTime ? null : query.Sort,
            Top = sortsOnTime ? null : query.Top,
        };
        var current = await model.ExecuteAsync(baseQuery, cancellationToken).ConfigureAwait(false);

        ResultSet? previous = null;
        var comparesYears = query.Measures.Any(m => m.Time is TimeCalculation.PreviousYear or TimeCalculation.ChangeFromPreviousYear or TimeCalculation.PercentChangeFromPreviousYear);
        if (comparesYears && HasDates(model.Metadata, query))
        {
            var shifted = baseQuery with { Filters = query.Filters?.Select(f => ShiftFilter(model.Metadata, f)).ToArray(), Sort = null, Top = null };
            previous = await model.ExecuteAsync(shifted, cancellationToken).ConfigureAwait(false);
        }

        var result = Combine(query, current, previous);
        return sortsOnTime ? SortAndLimit(result, query.Sort!, query.Top) : result;
    }

    // Without a date in the groups or the filters, "a year earlier" selects the same rows: it is left empty instead
    private static bool HasDates(ModelMetadata metadata, AnalyticQuery query) =>
        query.GroupBy.Any(g => IsDate(metadata, g.Field))
        || query.Filters?.Any(f => f.Operator.IsPeriod() || f.Grain != DateGrain.None || IsDate(metadata, f.Field)) == true;

    private static bool IsDate(ModelMetadata metadata, string field) =>
        metadata.Fields.FirstOrDefault(f => f.Name == field)?.DataType == DataType.Date;

    private static FilterSpec ShiftFilter(ModelMetadata metadata, FilterSpec filter)
    {
        if (filter.Operator.IsPeriod())
        {
            var (start, end) = PeriodFilters.Range(filter, DateOnly.FromDateTime(DateTime.Today));
            return PeriodFilters.Between(filter.Field,
                start is { } s ? DateOnly.FromDateTime(s.AddYears(-1)) : null,
                end is { } e ? DateOnly.FromDateTime(e.AddYears(-1).AddDays(-1)) : null);
        }
        if (filter.Grain == DateGrain.None && !IsDate(metadata, filter.Field)) return filter;
        return filter with { Values = filter.Values.Select(v => ShiftValue(v, filter.Grain)).ToArray() };
    }

    // A filter value a year earlier: a period key (2024, 20243, 202405) or a date
    private static string? ShiftValue(string? value, DateGrain grain)
    {
        if (value is null) return null;
        if (grain is DateGrain.Year or DateGrain.Quarter or DateGrain.Month)
        {
            var key = int.Parse(value, CultureInfo.InvariantCulture);
            return (key - YearStep(grain)).ToString(CultureInfo.InvariantCulture);
        }
        return value.Length == 10
            ? DateOnly.Parse(value, CultureInfo.InvariantCulture).AddYears(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : InvariantValue.Format(((DateTime)InvariantValue.Parse(value, typeof(DateTime))!).AddYears(-1));
    }

    private static int YearStep(DateGrain grain) => grain switch { DateGrain.Quarter => 10, DateGrain.Month => 100, _ => 1 };

    private static ResultSet Combine(AnalyticQuery query, ResultSet current, ResultSet? previous)
    {
        var groups = query.GroupBy.Count;
        var lastYear = previous?.Rows.GroupBy(r => KeyOf(r, groups, oneYearLater: true)).ToDictionary(g => g.Key, g => g.First());
        var dateColumn = Enumerable.Range(0, groups).Cast<int?>().FirstOrDefault(i => current.Rows.Any(r => IsDateValue(r[i!.Value])));

        var columns = current.Columns.Take(groups).ToList();
        var cells = new List<Func<object?[], int, object?>>();
        foreach (var measure in query.Measures)
        {
            var source = current.IndexOf((measure with { Time = TimeCalculation.None }).Key);
            var baseColumn = current.Columns[source];
            columns.Add(baseColumn with
            {
                Key = measure.Key,
                Caption = measure.Time == TimeCalculation.None ? baseColumn.Caption : Strings.Format(Strings.Named($"Time{measure.Time}"), baseColumn.Caption),
                Format = measure.Time == TimeCalculation.PercentChangeFromPreviousYear ? "P1" : baseColumn.Format,
            });
            var runningTotals = measure.Time == TimeCalculation.YearToDate ? YearToDate(current, source, groups, dateColumn) : null;
            cells.Add(measure.Time switch
            {
                TimeCalculation.None => (row, _) => row[source],
                TimeCalculation.YearToDate => (_, index) => runningTotals![index],
                _ => (row, _) =>
                {
                    var before = lastYear?.GetValueOrDefault(KeyOf(row, groups, oneYearLater: false))?[source];
                    return measure.Time switch
                    {
                        TimeCalculation.PreviousYear => before,
                        TimeCalculation.ChangeFromPreviousYear => Number(row[source]) - Number(before),
                        _ => Number(before) is { } b && b != 0 && Number(row[source]) is { } c ? (c - b) / Math.Abs(b) : null,
                    };
                },
            });
        }
        var rows = current.Rows.Select((row, index) => row.Take(groups).Concat(cells.Select(cell => cell(row, index))).ToArray()).ToList();
        return new ResultSet(columns, rows);
    }

    /// <summary>The total of a measure since the start of each row's year, along the date column; the value itself without one.</summary>
    private static object?[] YearToDate(ResultSet result, int source, int groups, int? dateColumn)
    {
        var totals = new object?[result.Rows.Count];
        if (dateColumn is not { } date)
        {
            for (var i = 0; i < totals.Length; i++) totals[i] = Number(result.Rows[i][source]);
            return totals;
        }
        var partitions = Enumerable.Range(0, result.Rows.Count).GroupBy(i =>
            string.Join("\u001f", Enumerable.Range(0, groups).Where(g => g != date).Select(g => KeyPart(result.Rows[i][g])))
            + "\u001f" + YearOf(result.Rows[i][date]));
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

    private static bool IsDateValue(object? value) => value is DatePeriod or DateTime or DateOnly or DateTimeOffset;

    private static double? Number(object? value) => value is null or DatePeriod ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static int YearOf(object? date) => date switch
    {
        DatePeriod p => p.Grain switch { DateGrain.Quarter => p.Key / 10, DateGrain.Month => p.Key / 100, _ => p.Key },
        DateTime d => d.Year,
        DateOnly d => d.Year,
        DateTimeOffset d => d.Year,
        _ => 0,
    };

    private static long SortKey(object? date) => date switch
    {
        DatePeriod p => p.Key,
        DateTime d => d.Ticks,
        DateOnly d => d.DayNumber,
        DateTimeOffset d => d.UtcTicks,
        _ => 0,
    };

    private static string KeyOf(object?[] row, int groups, bool oneYearLater) =>
        string.Join("\u001f", row.Take(groups).Select(v => KeyPart(oneYearLater ? OneYearLater(v) : v)));

    private static object? OneYearLater(object? value) => value switch
    {
        DatePeriod p => p with { Key = p.Key + YearStep(p.Grain) },
        DateTime d => d.AddYears(1),
        DateOnly d => d.AddYears(1),
        DateTimeOffset d => d.AddYears(1),
        _ => value,
    };

    private static string KeyPart(object? value) => value switch
    {
        null => "\u0000",
        DatePeriod p => $"{p.Grain}:{p.Key}",
        _ => InvariantValue.Format(value) ?? "",
    };
}
