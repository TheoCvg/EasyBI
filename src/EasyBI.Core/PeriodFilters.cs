using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Filters on a period of a date dimension (<see cref="FilterOperator.Between"/>, <see cref="FilterOperator.Current"/>,
/// <see cref="FilterOperator.Last"/>, <see cref="FilterOperator.Previous"/>): the days they keep, and how to word them.
/// Relative periods are resolved when the query runs, so a saved report keeps showing "the last 12 months".
/// </summary>
public static class PeriodFilters
{
    private const string DateFormat = "yyyy-MM-dd";

    public static bool IsPeriod(this FilterOperator @operator) =>
        @operator is FilterOperator.Between or FilterOperator.Current or FilterOperator.Last or FilterOperator.Previous;

    /// <summary>A filter between two days (included); null leaves that end open.</summary>
    public static FilterSpec Between(string field, DateOnly? from, DateOnly? to) =>
        new(field, FilterOperator.Between, [from?.ToString(DateFormat, CultureInfo.InvariantCulture), to?.ToString(DateFormat, CultureInfo.InvariantCulture)]);

    /// <summary>A period relative to today: the current one, the last <paramref name="count"/>, or the previous ones.</summary>
    public static FilterSpec Relative(string field, FilterOperator @operator, DateGrain unit, int count = 1) =>
        new(field, @operator, @operator == FilterOperator.Current ? [] : [count.ToString(CultureInfo.InvariantCulture)], unit);

    /// <summary>The bounds of a <see cref="FilterOperator.Between"/> filter.</summary>
    public static (DateOnly? From, DateOnly? To) Bounds(FilterSpec filter) =>
        (ParseDate(filter.Values.ElementAtOrDefault(0)), ParseDate(filter.Values.ElementAtOrDefault(1)));

    /// <summary>The number of periods of a relative filter (at least 1).</summary>
    public static int Count(FilterSpec filter) =>
        int.TryParse(filter.Values.ElementAtOrDefault(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0 ? count : 1;

    /// <summary>The days a period filter keeps on <paramref name="today"/>: from Start (included) to End (excluded); null is open.</summary>
    public static (DateTime? Start, DateTime? End) Range(FilterSpec filter, DateOnly today)
    {
        var unit = filter.Grain == DateGrain.None ? DateGrain.Day : filter.Grain;
        var current = Start(today, unit);
        var count = Count(filter);
        return filter.Operator switch
        {
            FilterOperator.Between => Bounds(filter) is var (from, to)
                ? (from?.ToDateTime(TimeOnly.MinValue), to?.ToDateTime(TimeOnly.MinValue).AddDays(1))
                : default,
            FilterOperator.Current => (current, Add(current, unit, 1)),
            FilterOperator.Last => (Add(current, unit, 1 - count), Add(current, unit, 1)),
            FilterOperator.Previous => (Add(current, unit, -count), current),
            _ => throw new ArgumentException($"'{filter.Operator}' is not a period filter.", nameof(filter)),
        };
    }

    /// <summary>The filter as the user reads it: "Last 12 months", "Since 01/03/2024"...</summary>
    public static string Describe(FilterSpec filter)
    {
        var unit = filter.Grain == DateGrain.None ? DateGrain.Day : filter.Grain;
        var count = Count(filter);
        switch (filter.Operator)
        {
            case FilterOperator.Between:
                var (from, to) = Bounds(filter);
                return (from, to) switch
                {
                    ({ } f, { } t) => Strings.Format(Strings.PeriodBetween, Day(f), Day(t)),
                    ({ } f, null) => Strings.Format(Strings.PeriodSince, Day(f)),
                    (null, { } t) => Strings.Format(Strings.PeriodUntil, Day(t)),
                    _ => Strings.PeriodAny,
                };
            case FilterOperator.Current:
            case FilterOperator.Last when count == 1:
                return Strings.Named($"PeriodCurrent{unit}");
            case FilterOperator.Last:
                return Strings.Format(Strings.Named($"PeriodLast{unit}"), count);
            case FilterOperator.Previous:
                return count == 1 ? Strings.Named($"PeriodPreviousOne{unit}") : Strings.Format(Strings.Named($"PeriodPrevious{unit}"), count);
            default:
                throw new ArgumentException($"'{filter.Operator}' is not a period filter.", nameof(filter));
        }
    }

    private static string Day(DateOnly day) => day.ToString("d", EasyBILocalization.FormatCulture);

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static DateTime Start(DateOnly day, DateGrain unit) => unit switch
    {
        DateGrain.Year => new DateTime(day.Year, 1, 1),
        DateGrain.Quarter => new DateTime(day.Year, (day.Month - 1) / 3 * 3 + 1, 1),
        DateGrain.Month => new DateTime(day.Year, day.Month, 1),
        _ => day.ToDateTime(TimeOnly.MinValue),
    };

    private static DateTime Add(DateTime start, DateGrain unit, int count) => unit switch
    {
        DateGrain.Year => start.AddYears(count),
        DateGrain.Quarter => start.AddMonths(3 * count),
        DateGrain.Month => start.AddMonths(count),
        _ => start.AddDays(count),
    };
}
