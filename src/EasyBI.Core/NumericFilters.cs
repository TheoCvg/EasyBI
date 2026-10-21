using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Comparisons with numbers (<see cref="FilterOperator.GreaterThan"/>... <see cref="FilterOperator.Range"/>), on a number
/// dimension or on a measure of a visual: how to build them, read their bounds and word them.
/// </summary>
public static class NumericFilters
{
    public static bool IsComparison(this FilterOperator @operator) =>
        @operator is FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual
            or FilterOperator.LessThan or FilterOperator.LessThanOrEqual or FilterOperator.Range;

    /// <summary>A comparison with one number (greater or less than).</summary>
    public static FilterSpec Compare(string field, FilterOperator @operator, double value) =>
        @operator is FilterOperator.Range || !@operator.IsComparison()
            ? throw new ArgumentException($"'{@operator}' does not compare with one number.", nameof(@operator))
            : new(field, @operator, [Format(value)]);

    /// <summary>Between two numbers (included); null leaves that end open.</summary>
    public static FilterSpec Range(string field, double? minimum, double? maximum) =>
        new(field, FilterOperator.Range, [minimum is { } min ? Format(min) : null, maximum is { } max ? Format(max) : null]);

    /// <summary>The numbers of a comparison: its value, or the minimum and maximum of a range (null when open or unreadable).</summary>
    public static (double? First, double? Second) Bounds(FilterSpec filter) =>
        (Parse(filter.Values.ElementAtOrDefault(0)), Parse(filter.Values.ElementAtOrDefault(1)));

    /// <summary>
    /// True when a filter applies to a measure (a model's or a calculated one) rather than
    /// to a dimension: it keeps groups, not rows.
    /// </summary>
    public static bool IsOnMeasure(FilterSpec filter, ModelMetadata metadata, IEnumerable<CalculatedMeasure>? calculations = null) =>
        filter.Operator.IsComparison()
        && (metadata.Fields.FirstOrDefault(f => f.Name == filter.Field)?.Kind == FieldKind.Measure
            || calculations?.Any(c => c.Name == filter.Field) == true);

    /// <summary>The condition as the user reads it, "≥ 1,000" or "between 10 and 20", numbers in <paramref name="format"/>.</summary>
    public static string Describe(FilterSpec filter, string? format = null)
    {
        var (first, second) = Bounds(filter);
        string Number(double? value) => value is { } v ? ValueFormatter.Format(v, format) : "";
        return filter.Operator switch
        {
            FilterOperator.GreaterThan => Strings.Format(Strings.CompareGreaterThan, Number(first)),
            FilterOperator.GreaterThanOrEqual => Strings.Format(Strings.CompareGreaterThanOrEqual, Number(first)),
            FilterOperator.LessThan => Strings.Format(Strings.CompareLessThan, Number(first)),
            FilterOperator.LessThanOrEqual => Strings.Format(Strings.CompareLessThanOrEqual, Number(first)),
            FilterOperator.Range => (first, second) switch
            {
                ({ }, { }) => Strings.Format(Strings.CompareRange, Number(first), Number(second)),
                ({ }, null) => Strings.Format(Strings.CompareGreaterThanOrEqual, Number(first)),
                (null, { }) => Strings.Format(Strings.CompareLessThanOrEqual, Number(second)),
                _ => "",
            },
            _ => throw new ArgumentException($"'{filter.Operator}' is not a comparison.", nameof(filter)),
        };
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double? Parse(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
