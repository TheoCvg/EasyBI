using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ComboChartTests
{
    private static readonly ResultSet Result = new(
        [
            new ResultColumn("Region", "Region", FieldKind.Dimension, DataType.Text),
            new ResultColumn("Channel", "Channel", FieldKind.Dimension, DataType.Text),
            new ResultColumn("Amount", "Revenue", FieldKind.Measure, DataType.Number, "#,##0 €"),
            new ResultColumn("Count", "Orders", FieldKind.Measure, DataType.Number),
        ],
        [
            ["North", "Web", 10m, 1],
            ["North", "Store", 20m, 2],
            ["South", "Web", 30m, 3],
        ]);

    private static VisualDefinition Chart(MarkType mark, FieldRef? legend, params MeasureRef[] values) => new()
    {
        Model = "sales",
        Mark = mark,
        Axis = [new("Region")],
        Legend = legend,
        Values = values,
    };

    [Fact]
    public void Each_measure_draws_its_series_with_its_own_chart_type_and_axis()
    {
        var visual = Chart(MarkType.Bar, null, new MeasureRef("Amount"), new MeasureRef("Count") { Mark = MarkType.Line, SecondaryAxis = true });

        var chart = ChartDataBuilder.Build(visual, Result);

        Assert.Equal([(MarkType.Bar, false), (MarkType.Line, true)], chart.Series.Select(s => (s.Mark, s.SecondaryAxis)));
    }

    [Fact]
    public void Legend_series_keep_the_chart_type_of_their_measure()
    {
        var visual = Chart(MarkType.Line, new("Channel"), new MeasureRef("Amount") { Mark = MarkType.StackedBar }, new MeasureRef("Count"));

        var chart = ChartDataBuilder.Build(visual, Result);

        Assert.Equal(
            [("Web · Revenue", MarkType.StackedBar), ("Web · Orders", MarkType.Line), ("Store · Revenue", MarkType.StackedBar), ("Store · Orders", MarkType.Line)],
            chart.Series.Select(s => (s.Name, s.Mark)));
    }

    [Fact]
    public void Chart_types_of_measures_only_apply_to_cartesian_charts()
    {
        var visual = Chart(MarkType.Kpi, null, new MeasureRef("Amount") { Mark = MarkType.Line, SecondaryAxis = true }) with { Axis = [] };

        var series = Assert.Single(ChartDataBuilder.Build(visual, Result).Series);

        Assert.Equal((MarkType.Kpi, false), (series.Mark, series.SecondaryAxis));
        Assert.Equal(new HashSet<MarkType> { MarkType.Kpi }, VisualPipeline.MarksOf(visual));
    }

    [Fact]
    public void A_renderer_must_support_every_mark_of_a_combined_chart()
    {
        var visual = Chart(MarkType.Bar, null, new MeasureRef("Amount"), new MeasureRef("Count") { Mark = MarkType.Area });

        Assert.Equal(new HashSet<MarkType> { MarkType.Bar, MarkType.Area }, VisualPipeline.MarksOf(visual));
    }

    [Fact]
    public void Changing_chart_types_or_axes_does_not_change_the_query()
    {
        var plain = Chart(MarkType.Bar, null, new MeasureRef("Amount"), new MeasureRef("Count"));
        var combined = Chart(MarkType.Bar, null, new MeasureRef("Amount") { Mark = MarkType.Scatter }, new MeasureRef("Count") { SecondaryAxis = true });

        var (a, b) = (plain.ToQuery(), combined.ToQuery());

        Assert.Equal(a.Measures, b.Measures);
        Assert.Equal(a.GroupBy, b.GroupBy);
    }

    [Fact]
    public void A_measure_of_a_chart_cannot_be_drawn_as_a_pie()
    {
        Assert.NotNull(VisualValidator.Validate(Chart(MarkType.Bar, null, new MeasureRef("Amount") { Mark = MarkType.Pie })));
        Assert.Null(VisualValidator.Validate(Chart(MarkType.Bar, null, new MeasureRef("Amount") { Mark = MarkType.Line })));
        Assert.Null(VisualValidator.Validate(Chart(MarkType.Scatter, null, new MeasureRef("Amount"))));
    }

    [Fact]
    public void Chart_types_and_axes_of_measures_round_trip_through_json()
    {
        var report = new ReportDefinition
        {
            Visuals = [Chart(MarkType.Bar, null, new MeasureRef("Amount"), new MeasureRef("Count") { Mark = MarkType.Line, SecondaryAxis = true })],
        };

        var json = ReportSerializer.Serialize(report);
        var values = ReportSerializer.Deserialize(json).Visuals[0].Values;

        Assert.Equal((null, false), (values[0].Mark, values[0].SecondaryAxis));
        Assert.Equal((MarkType.Line, true), (values[1].Mark, values[1].SecondaryAxis));
        Assert.Contains("\"mark\": \"Line\"", json);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "secondaryAxis")); // false is not written
    }

    [Fact]
    public void Redrawing_after_a_chart_type_change_rebuilds_the_series_without_querying()
    {
        var visual = Chart(MarkType.Bar, null, new MeasureRef("Amount"), new MeasureRef("Count"));
        var previous = new RenderRequest(visual, Result, ChartDataBuilder.Build(visual, Result), BiTheme.Default);
        var changed = visual with { Values = [new MeasureRef("Amount"), new MeasureRef("Count") { Mark = MarkType.Line, SecondaryAxis = true }] };

        var request = VisualPipeline.Redraw(previous, changed);

        Assert.Same(Result, request.Result);
        Assert.Equal((MarkType.Line, true), (request.Chart.Series[1].Mark, request.Chart.Series[1].SecondaryAxis));
    }
}
