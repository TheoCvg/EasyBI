using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>How a chart arranges itself in the room it gets (see <see cref="ChartFitting"/>).</summary>
public sealed record ChartFit
{
    /// <summary>Where the legend goes: a side or <see cref="ChartLegend.Hidden"/>, never <see cref="ChartLegend.Auto"/>.</summary>
    public ChartLegend Legend { get; init; } = ChartLegend.Hidden;

    /// <summary>
    /// The entries of the legend each page shows, when they do not all fit in its room (a column on a side, a row
    /// above or below); a single page otherwise. See <see cref="ChartFitting.LegendEntries"/>.
    /// </summary>
    public IReadOnlyList<LegendPage> LegendPages { get; init; } = [];

    /// <summary>Angle of the category labels, in degrees (0 = horizontal).</summary>
    public double LabelRotation { get; init; }

    /// <summary>Only every n-th category label is shown, so that labels never overlap.</summary>
    public int LabelStep { get; init; } = 1;

    /// <summary>False when there is no room for the axis titles.</summary>
    public bool ShowAxisTitles { get; init; } = true;

    /// <summary>Inner radius of a donut, in device-independent pixels (0 for a pie).</summary>
    public double InnerRadius { get; init; }
}

/// <summary>
/// The size of a library's legend entries, from which the pages of a legend are measured: the height of an entry in a
/// column (on a side), and the width of an entry in a row (<see cref="EntryWidth"/> plus <see cref="CharWidth"/> per
/// character). The defaults are LiveCharts2's at text size 13.
/// </summary>
public sealed record LegendMetrics(double EntryHeight = 34, double CharWidth = 7.5, double EntryWidth = 56)
{
    public static LegendMetrics Default { get; } = new();

    /// <summary>The width of an entry in a row (marker, text, spacing).</summary>
    public double WidthOf(string entry) => entry.Length * CharWidth + EntryWidth;
}

/// <summary>The entries of a legend page: <see cref="Count"/> entries from <see cref="First"/>.</summary>
public sealed record LegendPage(int First, int Count)
{
    public bool Contains(int entry) => entry >= First && entry < First + Count;
}

/// <summary>
/// Renderer-independent rules that keep a chart readable whatever its size: the legend goes from the right to the
/// bottom, then disappears (a visual can prefer a side, kept while there is room for it); a legend with more entries
/// than its room shows them by pages; category labels rotate, then skip; axis titles are dropped; a donut keeps its
/// proportions. A connector calls them with the actual size of its chart (and again when it changes).
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

    /// <summary>A plot next to a legend the visual asked for keeps at least this width.</summary>
    private const double MinPreferredPlotWidth = 160;

    /// <summary>Room of a legend the library takes for its margins.</summary>
    private const double LegendPadding = 16;

    /// <summary>
    /// Room left for the host's pager when the legend has pages: twice its size, as libraries center their legend
    /// (the pager sits in a corner, past the entries).
    /// </summary>
    private const double PagerHeight = 26, PagerWidth = 80;

    /// <summary>Width of a legend on the right, from its longest entry.</summary>
    public static double LegendWidth(IEnumerable<string> entries) =>
        Compat.Clamp(entries.Select(e => e.Length).DefaultIfEmpty(0).Max() * CharWidth + 36, 90, 220);

    /// <summary>
    /// The pages of a legend on <paramref name="side"/> in a chart of the given size: one page when every entry fits
    /// (a column on the right or left, a row above or below), otherwise as many entries per page as fit next to a pager.
    /// </summary>
    public static IReadOnlyList<LegendPage> LegendPages(IReadOnlyList<string> entries, ChartLegend side, double width, double height,
        LegendMetrics? metrics = null)
    {
        metrics ??= LegendMetrics.Default;
        var entryHeight = metrics.EntryHeight;
        if (side is ChartLegend.Right or ChartLegend.Left)
        {
            var room = height - LegendPadding;
            if (entries.Count * entryHeight <= room) return [new LegendPage(0, entries.Count)];
            var perPage = Math.Max(1, (int)((room - 2 * PagerHeight) / entryHeight));
            return [.. Enumerable.Range(0, (entries.Count + perPage - 1) / perPage)
                .Select(page => new LegendPage(page * perPage, Math.Min(perPage, entries.Count - page * perPage)))];
        }
        if (side is ChartLegend.Top or ChartLegend.Bottom && entries.Sum(metrics.WidthOf) > width - LegendPadding)
        {
            // Entries flow on one row per page, at least one each
            var pages = new List<LegendPage>();
            var limit = width - LegendPadding - 2 * PagerWidth;
            for (int first = 0, i = 0; first < entries.Count; first = i)
            {
                var used = metrics.WidthOf(entries[i++]);
                while (i < entries.Count && used + metrics.WidthOf(entries[i]) <= limit) used += metrics.WidthOf(entries[i++]);
                pages.Add(new LegendPage(first, i - first));
            }
            return pages;
        }
        return [new LegendPage(0, entries.Count)];
    }

    /// <summary>
    /// Which entries of the legend show: those of the pager's page, or all of them without a pager (the host shows
    /// none) or pages. The pager learns the legend's position and pages. A connector calls it with each fit.
    /// </summary>
    public static Func<int, bool> LegendEntries(ChartFit fit, LegendPager? pager)
    {
        IReadOnlyList<LegendPage> pages = fit.Legend == ChartLegend.Hidden ? [] : fit.LegendPages;
        pager?.Update(fit.Legend, pages.Count);
        if (pager is null || pages.Count <= 1) return _ => true;
        return pages[pager.Page].Contains;
    }

    /// <summary>
    /// Where zero holds a value axis (the right one when <paramref name="secondary"/>). Bars are compared by their length
    /// from zero: an axis drawing bars, alone or with lines, starts at zero when its values are positive and ends at
    /// zero when they are negative. Null bounds are left to the library: an axis without bars, or with both signs (zero
    /// is inside then).
    /// </summary>
    public static (double? Minimum, double? Maximum) ZeroBounds(ChartData chart, bool secondary)
    {
        var series = chart.Series.Where(s => s.SecondaryAxis == secondary).ToList();
        if (!series.Any(s => s.SeriesType == VisualType.Bar || s.SeriesType == VisualType.StackedBar)) return (null, null);
        var values = series.SelectMany(s => s.Values).OfType<double>().ToList();
        return values.All(v => v >= 0) ? (0, null) : values.All(v => v <= 0) ? (null, 0) : (null, null);
    }

    public static ChartFit Cartesian(ChartData chart, double width, double height, ChartLegend preferred = ChartLegend.Auto,
        LegendMetrics? metrics = null)
    {
        var entries = chart.Series.Select(s => s.Name).ToArray();
        // Without a choice, a single series needs no legend; a legend on a side only when the plot keeps a comfortable
        // width (a chosen side: a smaller one)
        var shown = preferred != ChartLegend.Hidden && entries.Length > 0 && (preferred != ChartLegend.Auto || entries.Length > 1);
        var legendWidth = LegendWidth(entries);
        var side = !shown ? ChartLegend.Hidden
            : preferred is ChartLegend.Right or ChartLegend.Left && width >= legendWidth + MinPreferredPlotWidth ? preferred
            : preferred == ChartLegend.Auto && width >= legendWidth + MinPlotWidth ? ChartLegend.Right
            : ChartLegend.Hidden;

        var axes = chart.Series.Any(s => s.SecondaryAxis) ? 2 : 1;
        var plotWidth = Math.Max(40, width - axes * ValueAxisWidth - (side != ChartLegend.Hidden ? legendWidth : 0));
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

        // Above or below the plot, the legend (one row, by pages) and the axis title only take height the plot can spare
        var spare = height - labelsHeight - MinPlotHeight;
        var legend = side != ChartLegend.Hidden || !shown ? side
            : spare >= LegendRowHeight ? preferred == ChartLegend.Top ? ChartLegend.Top : ChartLegend.Bottom
            : ChartLegend.Hidden;
        if (legend is ChartLegend.Top or ChartLegend.Bottom) spare -= LegendRowHeight;

        return new ChartFit
        {
            Legend = legend,
            LegendPages = LegendPages(entries, legend, width, height, metrics),
            LabelRotation = rotation,
            LabelStep = Math.Max(1, step),
            ShowAxisTitles = spare >= AxisTitleHeight && width >= 260,
        };
    }

    /// <summary>The fit of a pie, or of a donut (<paramref name="donut"/>), whose hole keeps half of the radius.</summary>
    public static ChartFit Pie(ChartData chart, double width, double height, bool donut = false, ChartLegend preferred = ChartLegend.Auto,
        LegendMetrics? metrics = null)
    {
        var legendWidth = LegendWidth(chart.Categories);
        var legend = preferred switch
        {
            ChartLegend.Hidden => ChartLegend.Hidden,
            ChartLegend.Right or ChartLegend.Left when width >= legendWidth + MinPreferredPlotWidth => preferred,
            ChartLegend.Top or ChartLegend.Bottom when height >= MinPlotHeight + 80 => preferred,
            _ when width >= legendWidth + 160 && width >= height => ChartLegend.Right,
            _ when height >= 280 => ChartLegend.Bottom,
            _ => ChartLegend.Hidden,
        };

        // The donut hole keeps half of the radius at any size
        var pieWidth = width - (legend is ChartLegend.Right or ChartLegend.Left ? legendWidth : 0);
        var pieHeight = height - (legend is ChartLegend.Top or ChartLegend.Bottom ? 60 : 0);
        return new ChartFit
        {
            Legend = legend,
            LegendPages = LegendPages(chart.Categories, legend, width, height, metrics),
            InnerRadius = donut ? Math.Max(0, Math.Min(pieWidth, pieHeight) / 2 * 0.5) : 0,
        };
    }
}
