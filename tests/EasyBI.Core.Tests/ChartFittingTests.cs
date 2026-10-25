using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ChartFittingTests
{
    private static ChartData Chart(int categories, int series, string label = "Region", bool secondary = false) => new(
        "Region",
        Enumerable.Range(0, categories).Select(i => $"{label} {i}").ToArray(),
        Enumerable.Range(0, series).Select(i => new ChartSeries($"Series {i}", new double?[categories], null) { SecondaryAxis = secondary && i > 0 }).ToArray());

    [Fact]
    public void A_large_chart_keeps_its_legend_on_the_right_and_horizontal_labels()
    {
        var fit = ChartFitting.Cartesian(Chart(5, 3), 900, 400);

        Assert.Equal((ChartLegend.Right, 0d, 1, true), (fit.Legend, fit.LabelRotation, fit.LabelStep, fit.ShowAxisTitles));
    }

    [Fact]
    public void A_narrow_chart_moves_its_legend_below_instead_of_squashing_the_plot()
    {
        Assert.Equal(ChartLegend.Bottom, ChartFitting.Cartesian(Chart(5, 3), 380, 400).Legend);
    }

    [Fact]
    public void A_small_chart_hides_its_legend_and_axis_titles()
    {
        var fit = ChartFitting.Cartesian(Chart(5, 3), 380, 180);

        Assert.Equal((ChartLegend.Hidden, false), (fit.Legend, fit.ShowAxisTitles));
    }

    [Fact]
    public void The_plot_keeps_its_height_before_the_legend_and_the_axis_title()
    {
        // Long rotated labels take most of the spare height: no room left for a legend or a title below the plot
        var fit = ChartFitting.Cartesian(Chart(5, 3, "Region long name"), 340, 240);

        Assert.Equal((-45d, ChartLegend.Hidden, false), (fit.LabelRotation, fit.Legend, fit.ShowAxisTitles));
    }

    [Fact]
    public void A_single_series_needs_no_legend()
    {
        Assert.Equal(ChartLegend.Hidden, ChartFitting.Cartesian(Chart(5, 1), 900, 400).Legend);
    }

    [Fact]
    public void Labels_rotate_then_skip_so_that_they_never_overlap()
    {
        var crowded = ChartFitting.Cartesian(Chart(12, 1, "Long category"), 600, 400);
        Assert.Equal((-45d, 1), (crowded.LabelRotation, crowded.LabelStep));

        var packed = ChartFitting.Cartesian(Chart(60, 1, "Long category"), 400, 400);
        Assert.Equal(-45d, packed.LabelRotation);
        Assert.True(packed.LabelStep > 1);

        // Too short to rotate: horizontal labels, every n-th one
        var flat = ChartFitting.Cartesian(Chart(12, 1, "Long category"), 600, 150);
        Assert.Equal(0d, flat.LabelRotation);
        Assert.True(flat.LabelStep > 1);
    }

    [Fact]
    public void A_second_value_axis_leaves_less_room_for_labels()
    {
        var one = ChartFitting.Cartesian(Chart(8, 2), 600, 150);
        var two = ChartFitting.Cartesian(Chart(8, 2, secondary: true), 600, 150);

        Assert.True(two.LabelStep >= one.LabelStep);
    }

    [Fact]
    public void A_donut_keeps_its_proportions_and_its_legend_follows_the_room()
    {
        var chart = Chart(4, 1);

        var large = ChartFitting.Pie(chart, 600, 300, donut: true);
        Assert.Equal(ChartLegend.Right, large.Legend);
        Assert.Equal((300 - 0) / 2 * 0.5, large.InnerRadius);

        var tall = ChartFitting.Pie(chart, 200, 400, donut: true);
        Assert.Equal(ChartLegend.Bottom, tall.Legend);
        Assert.Equal(200 / 2 * 0.5, tall.InnerRadius);

        var tiny = ChartFitting.Pie(chart, 150, 120, donut: true);
        Assert.Equal((ChartLegend.Hidden, 30d), (tiny.Legend, tiny.InnerRadius));
    }

    [Fact]
    public void A_pie_is_a_full_disc()
    {
        Assert.Equal(0, ChartFitting.Pie(Chart(4, 1), 600, 300).InnerRadius);
    }

    // --- Where zero holds a value axis

    private static ChartData Values(params (VisualType Type, double?[] Values, bool Secondary)[] series) => new(
        "Month", ["Jan", "Feb"], series.Select((s, i) => new ChartSeries($"S{i}", s.Values, null) { SeriesType = s.Type, SecondaryAxis = s.Secondary }).ToArray());

    [Fact]
    public void An_axis_drawing_bars_starts_at_zero_even_with_a_line()
    {
        var chart = Values((VisualType.Bar, [12_000, 14_000], false), (VisualType.Line, [11_000, null], false));

        Assert.Equal(((double?)0, (double?)null), ChartFitting.ZeroBounds(chart, secondary: false));
    }

    [Fact]
    public void An_axis_of_negative_bars_ends_at_zero_and_one_with_both_signs_is_left_to_the_library()
    {
        Assert.Equal(((double?)null, (double?)0), ChartFitting.ZeroBounds(Values((VisualType.StackedBar, [-5, -2], false)), secondary: false));
        Assert.Equal(((double?)null, (double?)null), ChartFitting.ZeroBounds(Values((VisualType.Bar, [-5, 8], false)), secondary: false));
    }

    [Fact]
    public void An_axis_without_bars_is_left_to_the_library()
    {
        var chart = Values((VisualType.Bar, [12_000, 14_000], false), (VisualType.Line, [0.3, 0.4], true));

        Assert.Equal(((double?)null, (double?)null), ChartFitting.ZeroBounds(chart, secondary: true));
        Assert.Equal(((double?)null, (double?)null), ChartFitting.ZeroBounds(Values((VisualType.Area, [5, 8], false)), secondary: false));
    }
}
