using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>The legend of a chart: the position a visual prefers, and pages of entries when they do not all fit.</summary>
public class LegendTests
{
    private static ChartData Chart(int categories, int series) => new(
        "Region",
        Enumerable.Range(0, categories).Select(i => $"Region {i}").ToArray(),
        Enumerable.Range(0, series).Select(i => new ChartSeries($"Series {i}", new double?[categories], null)).ToArray());

    private static readonly LegendMetrics Metrics = new(EntryHeight: 20, CharWidth: 10, EntryWidth: 20);

    [Fact]
    public void A_chosen_side_is_kept_while_the_chart_has_room_for_it()
    {
        Assert.Equal(ChartLegend.Left, ChartFitting.Cartesian(Chart(5, 3), 900, 400, ChartLegend.Left).Legend);
        Assert.Equal(ChartLegend.Top, ChartFitting.Cartesian(Chart(5, 3), 900, 400, ChartLegend.Top).Legend);
        Assert.Equal(ChartLegend.Bottom, ChartFitting.Cartesian(Chart(5, 3), 900, 400, ChartLegend.Bottom).Legend);
        // Too narrow for a side: below, as the automatic position would
        Assert.Equal(ChartLegend.Bottom, ChartFitting.Cartesian(Chart(5, 3), 250, 400, ChartLegend.Right).Legend);
        Assert.Equal(ChartLegend.Hidden, ChartFitting.Cartesian(Chart(5, 3), 900, 400, ChartLegend.Hidden).Legend);
    }

    [Fact]
    public void A_chosen_side_shows_the_legend_of_a_single_series()
    {
        Assert.Equal(ChartLegend.Hidden, ChartFitting.Cartesian(Chart(5, 1), 900, 400).Legend);
        Assert.Equal(ChartLegend.Right, ChartFitting.Cartesian(Chart(5, 1), 900, 400, ChartLegend.Right).Legend);
    }

    [Fact]
    public void A_pie_follows_the_chosen_side_and_its_donut_hole_the_room_left()
    {
        var left = ChartFitting.Pie(Chart(4, 1), 600, 300, donut: true, ChartLegend.Left);
        var top = ChartFitting.Pie(Chart(4, 1), 600, 300, donut: true, ChartLegend.Top);

        Assert.Equal(ChartLegend.Left, left.Legend);
        Assert.Equal((ChartLegend.Top, (300 - 60) / 2 * 0.5), (top.Legend, top.InnerRadius));
        Assert.Equal(ChartLegend.Hidden, ChartFitting.Pie(Chart(4, 1), 600, 300, preferred: ChartLegend.Hidden).Legend);
    }

    [Fact]
    public void A_column_of_entries_taller_than_the_chart_is_shown_by_pages()
    {
        var entries = Enumerable.Range(0, 10).Select(i => $"E{i}").ToArray();

        // 116 px of room: 10 entries of 20 px do not fit; with the pager's room, 3 per page
        var pages = ChartFitting.LegendPages(entries, ChartLegend.Right, 500, 132, Metrics);

        Assert.Equal([new(0, 3), new(3, 3), new(6, 3), new(9, 1)], pages);
        Assert.Single(ChartFitting.LegendPages(entries, ChartLegend.Left, 500, 300, Metrics));
    }

    [Fact]
    public void A_row_of_entries_wider_than_the_chart_is_shown_by_pages()
    {
        // Entries of 40 px (2 characters): 400 px do not fit in 300; 300 - 16 - 160 = 124 px per page, 3 entries
        var entries = Enumerable.Range(0, 10).Select(i => $"E{i}").ToArray();

        var pages = ChartFitting.LegendPages(entries, ChartLegend.Bottom, 300, 400, Metrics);

        Assert.Equal([new(0, 3), new(3, 3), new(6, 3), new(9, 1)], pages);
        Assert.Single(ChartFitting.LegendPages(entries, ChartLegend.Top, 500, 400, Metrics));
    }

    [Fact]
    public void A_very_long_entry_gets_a_page_of_its_own()
    {
        var pages = ChartFitting.LegendPages(["A", new string('x', 100), "B"], ChartLegend.Bottom, 300, 400, Metrics);

        Assert.Equal([new(0, 1), new(1, 1), new(2, 1)], pages);
    }

    [Fact]
    public void The_entries_shown_are_those_of_the_pagers_page()
    {
        var fit = new ChartFit { Legend = ChartLegend.Right, LegendPages = [new(0, 3), new(3, 2)] };
        var pager = new LegendPager();

        var shown = ChartFitting.LegendEntries(fit, pager);

        Assert.Equal((ChartLegend.Right, 2, 0), (pager.Position, pager.PageCount, pager.Page));
        Assert.Equal([true, true, true, false, false], Enumerable.Range(0, 5).Select(shown));
        pager.GoTo(1);
        Assert.Equal([false, false, false, true, true], Enumerable.Range(0, 5).Select(ChartFitting.LegendEntries(fit, pager)));
        // Without a pager to turn them, every entry shows
        Assert.True(ChartFitting.LegendEntries(fit, null)(4));
    }

    [Fact]
    public void A_pager_keeps_its_page_within_the_pages_and_reports_changes_only()
    {
        var pager = new LegendPager();
        var changes = 0;
        pager.Changed += (_, _) => changes++;

        pager.Update(ChartLegend.Bottom, 4);
        pager.GoTo(3);
        pager.Update(ChartLegend.Bottom, 4); // same layout
        pager.GoTo(9);
        Assert.Equal((3, 2), (pager.Page, changes));

        pager.Update(ChartLegend.Bottom, 2); // the chart grew: fewer pages
        Assert.Equal((1, 3), (pager.Page, changes));
        pager.GoTo(-1);
        Assert.Equal(0, pager.Page);
    }

    [Fact]
    public void The_legend_position_is_saved_with_the_visual_and_omitted_when_automatic()
    {
        var visual = new VisualDefinition { Type = VisualType.Bar, LegendPosition = ChartLegend.Bottom };

        var json = JsonSerializer.Serialize(visual, ReportSerializer.Options);

        Assert.Contains("\"legendPosition\": \"Bottom\"", json);
        Assert.Equal(ChartLegend.Bottom, JsonSerializer.Deserialize<VisualDefinition>(json, ReportSerializer.Options)!.LegendPosition);
        Assert.DoesNotContain("legendPosition", JsonSerializer.Serialize(visual with { LegendPosition = ChartLegend.Auto }, ReportSerializer.Options));
    }
}
