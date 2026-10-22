using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

public enum WaterfallStepKind { Increase, Decrease, Total }

/// <summary>A bar of a waterfall: from <see cref="Start"/> to <see cref="End"/> of the running total.</summary>
public sealed record WaterfallStep(double Start, double End, WaterfallStepKind Kind)
{
    /// <summary>The value the bar stands for: a category's change, or the total.</summary>
    public double Value => End - Start;
}

/// <summary>
/// The bars of a <see cref="MarkType.Waterfall"/>: each category's value (its first series) starts where the running
/// total stands, up or down, and a last bar shows the total from 0. A missing value is a flat step.
/// </summary>
public static class Waterfall
{
    /// <returns>
    /// The chart with a last "Total" category (its series holding the total there), and one step per category of it.
    /// The categories keep their index, so a click or a highlight on them works as on any chart; the total has no
    /// <see cref="ChartData.CategoryValues"/> and is not selectable.
    /// </returns>
    public static (ChartData Chart, IReadOnlyList<WaterfallStep> Steps) Of(ChartData chart)
    {
        var values = chart.Series.FirstOrDefault()?.Values ?? [];
        var steps = new List<WaterfallStep>();
        var total = 0d;
        foreach (var value in values)
        {
            var end = total + (value ?? 0);
            steps.Add(new WaterfallStep(total, end, end < total ? WaterfallStepKind.Decrease : WaterfallStepKind.Increase));
            total = end;
        }
        steps.Add(new WaterfallStep(0, total, WaterfallStepKind.Total));

        var series = chart.Series.Take(1).Select(s => s with { Values = [.. s.Values, total] }).ToArray();
        return (chart with { Categories = [.. chart.Categories, Strings.Total], Series = series }, steps);
    }
}
