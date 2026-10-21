using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// A zone of the value axis to shade, from a rule shown on the chart: <see cref="From"/> to <see cref="To"/> (null for
/// an open end), in <see cref="Color"/> (#RRGGBB, to draw translucent), against the right axis when
/// <see cref="SecondaryAxis"/>, with the rule's condition as <see cref="Label"/>.
/// </summary>
public sealed record ChartBand(double? From, double? To, string Color, bool SecondaryAxis, string Label);

/// <summary>
/// The formats of a chart's measures (<see cref="MeasureRef.Format"/>) as a renderer draws them: the color of each
/// point from the rules and the color scale (a rule's text color, else its background, full strength), and the zones
/// of the rules shown on the chart. A scale compares a point with the points of the same measure.
/// </summary>
public sealed class ChartFormatting
{
    private readonly RenderRequest _request;
    private readonly ValueFormat?[] _formats;
    private readonly (double Min, double Max)[] _ranges;

    private ChartFormatting(RenderRequest request)
    {
        _request = request;
        var series = request.Chart.Series;
        _formats = [.. series.Select(s => request.Visual.Values.FirstOrDefault(v => v.Key == s.MeasureKey)?.Format)];
        _ranges = [.. series.Select(s =>
        {
            var values = series.Where(o => o.MeasureKey == s.MeasureKey).SelectMany(o => o.Values).OfType<double>().ToList();
            return values.Count == 0 ? (0d, 0d) : (values.Min(), values.Max());
        })];
        Bands = [.. request.Visual.Values
            .Where(v => v.Format is not null)
            .SelectMany(v => v.Format!.Rules.Where(r => r.ShowOnChart).Select(r => Band(v, r)))
            .OfType<ChartBand>()];
    }

    public static ChartFormatting For(RenderRequest request) => new(request);

    /// <summary>True when some point gets its own color.</summary>
    public bool ColorsPoints => _formats.Any(f => f is not null && (f.Rules.Any(HasColor) || f.Scale is not null));

    /// <summary>The zones to shade, in the order of the measures and their rules.</summary>
    public IReadOnlyList<ChartBand> Bands { get; }

    /// <summary>The color of a point (#RRGGBB) from its measure's rules or scale; null keeps the series' color.</summary>
    public string? PointColor(int series, int category)
    {
        if (_formats.ElementAtOrDefault(series) is not { } format) return null;
        if (_request.Chart.Series[series].Values.ElementAtOrDefault(category) is not { } value) return null;
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
