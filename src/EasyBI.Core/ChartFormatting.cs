using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// A zone of the value axis to shade, from a rule shown on the chart: <see cref="From"/> to <see cref="To"/> (null for
/// an open end), in <see cref="Color"/> (#RRGGBB, to draw translucent), against the right axis when
/// <see cref="SecondaryAxis"/>, with the rule's condition as <see cref="Label"/>.
/// </summary>
public sealed record ChartBand(double? From, double? To, string Color, bool SecondaryAxis, string Label);

/// <summary>
/// A frame around a whole stack of bars that a rule or a scale picks out (its segments keep their legend colors): the
/// stack of <see cref="Category"/>, from 0 to <see cref="Top"/> (the sum of its positive segments), in
/// <see cref="Color"/> (#RRGGBB), against the right axis when <see cref="SecondaryAxis"/>.
/// </summary>
public sealed record StackOutline(int Category, double Top, string Color, bool SecondaryAxis);

/// <summary>
/// The formats of a chart's measures (<see cref="MeasureRef.Format"/>) as a renderer draws them: the color of each
/// point from the rules and the color scale (a rule's text color, else its background, full strength), and the zones
/// of the rules shown on the chart. A scale compares a point with the points of the same measure. Stacked bars are
/// judged as a whole and keep their legend colors: a rule or a scale compares the total of the stack, and frames the
/// stack in its color (<see cref="StackOutlines"/>).
/// </summary>
public sealed class ChartFormatting
{
    private readonly RenderRequest _request;
    private readonly ValueFormat?[] _formats;
    private readonly (double Min, double Max)[] _ranges;
    // The stack of each stacked series (bars stack separately on each value axis), null for the others
    private readonly int?[] _stacks;
    private readonly Dictionary<int, double?[]> _totals = [];

    private ChartFormatting(RenderRequest request)
    {
        _request = request;
        var series = request.Chart.Series;
        _formats = [.. series.Select(s => request.Visual.Values.FirstOrDefault(v => v.Key == s.MeasureKey)?.Format)];
        _stacks = [.. series.Select(s => s.Mark == MarkType.StackedBar ? (s.SecondaryAxis ? 1 : 0) : (int?)null)];
        foreach (var stack in _stacks.OfType<int>().Distinct())
        {
            _totals[stack] = [.. Enumerable.Range(0, request.Chart.Categories.Count).Select(c =>
            {
                var values = series.Where((_, i) => _stacks[i] == stack).Select(s => s.Values.ElementAtOrDefault(c)).OfType<double>().ToList();
                return values.Count == 0 ? (double?)null : values.Sum();
            })];
        }
        _ranges = [.. series.Select((s, i) =>
        {
            var values = _stacks[i] is { } stack
                ? _totals[stack].OfType<double>().ToList()
                : series.Where(o => o.MeasureKey == s.MeasureKey).SelectMany(o => o.Values).OfType<double>().ToList();
            return values.Count == 0 ? (0d, 0d) : (values.Min(), values.Max());
        })];
        StackOutlines = [.. _totals.SelectMany(stack => stack.Value.Select((total, category) => Outline(stack.Key, category, total))).OfType<StackOutline>()];
        Bands = [.. request.Visual.Values
            .Where(v => v.Format is not null)
            .SelectMany(v => v.Format!.Rules.Where(r => r.ShowOnChart).Select(r => Band(v, r)))
            .OfType<ChartBand>()];
    }

    public static ChartFormatting For(RenderRequest request) => new(request);

    /// <summary>True when some point (not stacked) gets its own color.</summary>
    public bool ColorsPoints => _formats.Where((_, i) => _stacks[i] is null).Any(f => f is not null && (f.Rules.Any(HasColor) || f.Scale is not null));

    /// <summary>The stacks a rule or a scale picks out, to frame in its color.</summary>
    public IReadOnlyList<StackOutline> StackOutlines { get; }

    /// <summary>The zones to shade, in the order of the measures and their rules.</summary>
    public IReadOnlyList<ChartBand> Bands { get; }

    /// <summary>
    /// The color of a point (#RRGGBB) from its measure's rules or scale; null keeps the series' color, and always for
    /// a segment of a stack (the stack is framed instead, see <see cref="StackOutlines"/>).
    /// </summary>
    public string? PointColor(int series, int category)
    {
        if (_stacks.ElementAtOrDefault(series) is not null) return null;
        if (_formats.ElementAtOrDefault(series) is not { } own) return null;
        if (_request.Chart.Series[series].Values.ElementAtOrDefault(category) is not { } value) return null;
        return ColorOf(own, series, value);
    }

    // The color a format gives a value: its first matching rule's, else its scale's
    private string? ColorOf(ValueFormat format, int series, double value)
    {
        var theme = _request.Theme;
        if (format.Rules.FirstOrDefault(r => HasColor(r) && ValueStyler.Matches(r, value)) is { } rule)
            return theme.Resolve(rule.Color ?? rule.Background);
        if (format.Scale is not { } scale) return null;
        var (min, max) = _ranges[series];
        var position = max == min ? 1 : (value - min) / (max - min);
        return scale.Middle is { } middle
            ? position < 0.5
                ? ValueStyler.Mix(theme.Resolve(scale.Minimum), theme.Resolve(middle), position * 2)
                : ValueStyler.Mix(theme.Resolve(middle), theme.Resolve(scale.Maximum), position * 2 - 1)
            : ValueStyler.Mix(theme.Resolve(scale.Minimum), theme.Resolve(scale.Maximum), position);
    }

    private static bool HasColor(FormatRule rule) => rule.Color is not null || rule.Background is not null;

    // The frame of a stack: the color of the first of its measures' formats that gives one for the stack's total
    private StackOutline? Outline(int stack, int category, double? total)
    {
        if (total is not { } value) return null;
        var color = Enumerable.Range(0, _stacks.Length)
            .Where(i => _stacks[i] == stack)
            .Select(i => _formats[i] is { } format ? ColorOf(format, i, value) : null)
            .FirstOrDefault(c => c is not null);
        if (color is null) return null;
        var top = _request.Chart.Series.Where((_, i) => _stacks[i] == stack)
            .Select(s => s.Values.ElementAtOrDefault(category)).OfType<double>().Where(v => v > 0).Sum();
        return new StackOutline(category, top, color, stack == 1);
    }

    private ChartBand? Band(MeasureRef measure, FormatRule rule)
    {
        var (from, to) = rule.Operator switch
        {
            FilterOperator.GreaterThan or FilterOperator.GreaterThanOrEqual => (rule.Value, null),
            FilterOperator.LessThan or FilterOperator.LessThanOrEqual => (null, rule.Value),
            FilterOperator.Range => (rule.Value, rule.Maximum),
            _ => ((double?)null, (double?)null),
        };
        if (from is null && to is null) return null;
        var color = _request.Theme.Resolve(rule.Color ?? rule.Background ?? ThemeColors.Accent)!;
        var format = _request.Chart.Series.FirstOrDefault(s => s.MeasureKey == measure.Key)?.Format;
        var label = NumericFilters.Describe(new FilterSpec("", rule.Operator,
            [Invariant(rule.Value), rule.Operator == FilterOperator.Range ? Invariant(rule.Maximum) : null]), format);
        return new ChartBand(from, to, color, _request.Visual.Mark.IsCartesian() && measure.SecondaryAxis, label);
    }

    private static string? Invariant(double? value) => value is { } v ? InvariantValue.Format(v) : null;
}
