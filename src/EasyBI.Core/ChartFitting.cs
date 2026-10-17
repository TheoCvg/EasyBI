using EasyBI.Abstractions;

namespace EasyBI.Core;

public enum ChartLegend { Hidden, Right, Bottom }

/// <summary>How a chart arranges itself in the room it gets (see <see cref="ChartFitting"/>).</summary>
public sealed record ChartFit
{
    public ChartLegend Legend { get; init; }

    /// <summary>Angle of the category labels, in degrees (0 = horizontal).</summary>
    public double LabelRotation { get; init; }

    /// <summary>Only every n-th category label is shown, so that labels never overlap.</summary>
    public int LabelStep { get; init; } = 1;

    /// <summary>False when there is no room for the axis titles.</summary>
    public bool ShowAxisTitles { get; init; } = true;

    /// <summary>Inner radius of a donut, in device-independent pixels.</summary>
    public double InnerRadius { get; init; }
}

/// <summary>
/// Renderer-independent rules that keep a chart readable whatever its size: the legend goes from the right to the
/// bottom, then disappears; category labels rotate, then skip; axis titles are dropped; a donut keeps its proportions.
/// A connector calls them with the actual size of its chart (and again when it changes).
/// </summary>
public static class ChartFitting
{
    /// <summary>Approximate width of a character of the chart texts (12-13 px fonts).</summary>
    private const double CharWidth = 7;

    /// <summary>Room a rotated label needs along the axis (one line of text, plus spacing).</summary>
    private const double RotatedLabelPitch = 22;

    private const double ValueAxisWidth = 70;
    private const double MinPlotWidth = 300;

    /// <summary>The plot keeps at least this height; legend, rotated labels and axis titles only get what is left.</summary>
    private const double MinPlotHeight = 120;

    private const double HorizontalLabelsHeight = 20;
    private const double LegendRowHeight = 34;
    private const double AxisTitleHeight = 28;

    /// <summary>Width of a legend on the right, from its longest entry.</summary>
    public static double LegendWidth(IEnumerable<string> entries) =>
        Math.Clamp(entries.Select(e => e.Length).DefaultIfEmpty(0).Max() * CharWidth + 36, 90, 220);

    /// <summary>Height of a legend below the chart: its entries flow on as many rows as the width requires.</summary>
    public static double BottomLegendHeight(IEnumerable<string> entries, double width)
    {
        // Legend texts are a bit larger than axis labels, and each entry has a marker and spacing
        var total = entries.Sum(e => e.Length * 8.5 + 40);
        return Math.Max(1, Math.Ceiling(total / Math.Max(1, width))) * LegendRowHeight;
    }

    public static ChartFit Cartesian(ChartData chart, double width, double height)
    {
        // A legend on the right only when the plot keeps a comfortable width
        var legendWidth = LegendWidth(chart.Series.Select(s => s.Name));
        var right = chart.Series.Count > 1 && width >= legendWidth + MinPlotWidth;

        var axes = chart.Series.Any(s => s.SecondaryAxis) ? 2 : 1;
        var plotWidth = Math.Max(40, width - axes * ValueAxisWidth - (right ? legendWidth : 0));
        var perLabel = plotWidth / Math.Max(1, chart.Categories.Count);
        var longest = chart.Categories.Select(c => c.Length).DefaultIfEmpty(1).Max() * CharWidth + 8;
        // At 45 degrees, a label takes about 0.71 of its length in height
        var rotatedHeight = longest * 0.71 + 10;

        double rotation = 0, labelsHeight = HorizontalLabelsHeight;
        int step;
        if (perLabel >= longest)
            step = 1;
        else if (height - rotatedHeight >= MinPlotHeight)
        {
            // Rotated labels only need one text line of room each
            rotation = -45;
            labelsHeight = rotatedHeight;
            step = (int)Math.Ceiling(RotatedLabelPitch / perLabel);
        }
        else
            step = (int)Math.Ceiling(longest / perLabel); // no room to rotate: skip horizontal labels

        // Below the plot, the legend and the axis title only take height the plot can spare
        var spare = height - labelsHeight - MinPlotHeight;
        var legendHeight = BottomLegendHeight(chart.Series.Select(s => s.Name), width);
        var legend = right ? ChartLegend.Right
            : chart.Series.Count > 1 && spare >= legendHeight ? ChartLegend.Bottom
            : ChartLegend.Hidden;
        if (legend == ChartLegend.Bottom) spare -= legendHeight;

        return new ChartFit
        {
            Legend = legend,
            LabelRotation = rotation,
            LabelStep = Math.Max(1, step),
            ShowAxisTitles = spare >= AxisTitleHeight && width >= 260,
        };
    }

    public static ChartFit Pie(ChartData chart, double width, double height)
    {
        var legendWidth = LegendWidth(chart.Categories);
        var legend = width >= legendWidth + 160 && width >= height ? ChartLegend.Right
            : height >= 280 ? ChartLegend.Bottom
            : ChartLegend.Hidden;

        // The donut hole keeps half of the radius at any size
        var pieWidth = width - (legend == ChartLegend.Right ? legendWidth : 0);
        var pieHeight = height - (legend == ChartLegend.Bottom ? 60 : 0);
        return new ChartFit
        {
            Legend = legend,
            InnerRadius = Math.Max(0, Math.Min(pieWidth, pieHeight) / 2 * 0.5),
        };
    }
}
