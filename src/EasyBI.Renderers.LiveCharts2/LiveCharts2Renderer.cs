using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace EasyBI.Renderers.LiveCharts2;

/// <summary>LiveCharts2 connector: translates a <see cref="RenderRequest"/> into a CartesianChart / PieChart.</summary>
public sealed class LiveCharts2Renderer : IVisualRenderer
{
    public string Name => "LiveCharts2";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType>
    {
        MarkType.Bar, MarkType.StackedBar, MarkType.Line, MarkType.Area, MarkType.Scatter, MarkType.Pie,
    });

    public FrameworkElement Render(RenderRequest request) => request.Visual.Mark switch
    {
        MarkType.Pie => RenderPie(request),
        var mark when mark.IsCartesian() => RenderCartesian(request),
        _ => throw new NotSupportedException($"LiveCharts2Renderer does not support '{request.Visual.Mark}'."),
    };

    /// <summary>
    /// Bars, stacked bars, lines, areas and points in one chart: each series is drawn with its own
    /// <see cref="ChartSeries.Mark"/>, against the left axis or, for <see cref="ChartSeries.SecondaryAxis"/>, the right one.
    /// </summary>
    private static CartesianChart RenderCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var series = chart.Series.Select((s, i) => CreateSeries(s, Color(request.Theme, i))).ToArray();
        var hasSecondary = chart.Series.Any(s => s.SecondaryAxis);

        var theme = request.Theme;
        var categoryAxis = Themed(new Axis
        {
            Labels = chart.Categories.ToArray(),
            Name = chart.CategoryCaption,
            NameTextSize = 13,
            TextSize = 12,
        }, theme);
        var view = new CartesianChart
        {
            Series = series,
            XAxes = [categoryAxis],
            YAxes = hasSecondary
                ? [ValueAxis(chart, theme, secondary: false), ValueAxis(chart, theme, secondary: true)]
                : [ValueAxis(chart, theme, secondary: false)],
            LegendPosition = chart.Series.Count > 1 ? LegendPosition.Right : LegendPosition.Hidden,
        };
        ApplyTheme(view, theme);
        // Legend, labels and axis title adapt to the room the chart actually gets
        view.SizeChanged += (_, _) => FitCartesian(view, categoryAxis, chart);

        if (request.Select is { } select)
        {
            // Only bars: the clicked bar is picked (category + legend series).
            // With lines, areas or points: the point closest to the clicked X position.
            view.FindingStrategy = chart.Series.All(s => s.Mark is MarkType.Bar or MarkType.StackedBar)
                ? FindingStrategy.ExactMatch
                : FindingStrategy.CompareOnlyXTakeClosest;
            view.Cursor = System.Windows.Input.Cursors.Hand;
            view.DataPointerDown += (_, points) =>
            {
                if (points?.FirstOrDefault() is not { } point) return;
                var index = Array.IndexOf(series, point.Context.Series);
                select(new CategorySelection(point.Index, index < 0 ? null : index));
            };
        }
        return view;
    }

    private static void FitCartesian(CartesianChart view, Axis categoryAxis, ChartData chart)
    {
        if (view.ActualWidth <= 0) return;
        var fit = ChartFitting.Cartesian(chart, view.ActualWidth, view.ActualHeight);

        // Only assign what changed: every assignment redraws the chart
        var legend = Position(fit.Legend);
        if (view.LegendPosition != legend) view.LegendPosition = legend;
        if (categoryAxis.LabelsRotation != fit.LabelRotation) categoryAxis.LabelsRotation = fit.LabelRotation;
        // Skipped labels are blanked: LiveCharts2 does not apply a step to an axis made of labels
        var labels = chart.Categories.Select((c, i) => i % fit.LabelStep == 0 ? c : "").ToArray();
        if (!labels.SequenceEqual(categoryAxis.Labels ?? [])) categoryAxis.Labels = labels;
        var title = fit.ShowAxisTitles ? chart.CategoryCaption : null;
        if (categoryAxis.Name != title) categoryAxis.Name = title;
    }

    private static LegendPosition Position(ChartLegend placement) => placement switch
    {
        ChartLegend.Right => LegendPosition.Right,
        ChartLegend.Bottom => LegendPosition.Bottom,
        _ => LegendPosition.Hidden,
    };

    /// <summary>A value axis formatted like the first series drawn against it.</summary>
    private static Axis ValueAxis(ChartData chart, BiTheme theme, bool secondary)
    {
        var series = chart.Series.Where(s => s.SecondaryAxis == secondary).ToList();
        var format = series.FirstOrDefault()?.Format;
        // Bars are compared by their height: an axis showing only bars of positive values starts at zero
        var barsFromZero = series.Count > 0
            && series.All(s => s.Mark is MarkType.Bar or MarkType.StackedBar)
            && series.SelectMany(s => s.Values).All(v => v is null or >= 0);
        return Themed(new Axis
        {
            MinLimit = barsFromZero ? 0 : null,
            Labeler = value => ValueFormatter.Format(value, format),
            TextSize = 12,
            Position = secondary ? AxisPosition.End : AxisPosition.Start,
            // Grid lines come from the left axis only, so that the two scales do not draw two grids
            SeparatorsPaint = secondary ? null : new SolidColorPaint(Parse(theme.GridLine) ?? new SKColor(0xE5, 0xE7, 0xEB)),
        }, theme);
    }

    /// <summary>Axis labels and title in the theme's colors (the library's defaults when the theme has none).</summary>
    private static Axis Themed(Axis axis, BiTheme theme)
    {
        if (Parse(theme.MutedForeground) is { } labels) axis.LabelsPaint = new SolidColorPaint(labels);
        if (Parse(theme.Foreground ?? theme.MutedForeground) is { } name) axis.NamePaint = new SolidColorPaint(name);
        return axis;
    }

    /// <summary>Legend and tooltips in the theme's colors.</summary>
    private static void ApplyTheme(System.Windows.Controls.Control view, BiTheme theme)
    {
        // The chart is drawn on the visual's own background
        view.Background = System.Windows.Media.Brushes.Transparent;
        if (view is not LiveChartsCore.Kernel.Sketches.IChartView chart) return;
        if (Parse(theme.Foreground) is { } text)
        {
            chart.LegendTextPaint = new SolidColorPaint(text);
            chart.TooltipTextPaint = new SolidColorPaint(text);
        }
        if (Parse(theme.Background) is { } background) chart.TooltipBackgroundPaint = new SolidColorPaint(background);
    }

    private static SKColor? Parse(string? color) => color is null ? null : SKColor.Parse(color);

    private static ISeries CreateSeries(ChartSeries source, SKColor color)
    {
        // ObservableValue(null) = missing point: LiveCharts2 draws it as a gap (or no bar, no point).
        var values = source.Values.Select(v => new ObservableValue(v)).ToArray();
        var axis = source.SecondaryAxis ? 1 : 0;
        string Tooltip(double value) => ValueFormatter.Format(value, source.Format);

        return source.Mark switch
        {
            MarkType.Line or MarkType.Area => new LineSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Stroke = new SolidColorPaint(color, 3),
                Fill = source.Mark == MarkType.Area ? new SolidColorPaint(color.WithAlpha(0x55)) : null,
                GeometrySize = source.Mark == MarkType.Area ? 0 : 8,
                GeometryStroke = new SolidColorPaint(color, 3),
                LineSmoothness = 0.3,
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            },
            // Points sit on the categories, without a line between them
            MarkType.Scatter => new LineSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Stroke = null,
                Fill = null,
                GeometrySize = 12,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = new SolidColorPaint(color, 2),
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            },
            MarkType.StackedBar => new StackedColumnSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Fill = new SolidColorPaint(color),
                MaxBarWidth = 48,
                // Series on the right axis stack separately from those on the left
                StackGroup = axis,
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            },
            _ => new ColumnSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Fill = new SolidColorPaint(color),
                MaxBarWidth = 48,
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            },
        };
    }

    private static PieChart RenderPie(RenderRequest request)
    {
        var chart = request.Chart;
        var source = chart.Series[0];
        // One series per slice: keep each slice's category index for selection
        var slices = new Dictionary<ISeries, int>();
        foreach (var (category, i) in chart.Categories.Select((category, i) => (category, i)))
        {
            if (source.Values[i] is not { } value) continue;
            slices[new PieSeries<double>
            {
                Name = category,
                Values = [value],
                Fill = new SolidColorPaint(Color(request.Theme, i)),
                ToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
            }] = i;
        }

        var view = new PieChart { Series = slices.Keys.ToArray(), LegendPosition = LegendPosition.Right };
        ApplyTheme(view, request.Theme);
        // Legend and donut hole adapt to the room the chart actually gets
        view.SizeChanged += (_, _) => FitPie(view, slices.Keys, chart);
        if (request.Select is { } select)
        {
            view.Cursor = System.Windows.Input.Cursors.Hand;
            view.DataPointerDown += (_, points) =>
            {
                if (points?.FirstOrDefault() is { } point && slices.TryGetValue(point.Context.Series, out var category))
                    select(new CategorySelection(category));
            };
        }
        return view;
    }

    private static void FitPie(PieChart view, IEnumerable<ISeries> slices, ChartData chart)
    {
        if (view.ActualWidth <= 0) return;
        var fit = ChartFitting.Pie(chart, view.ActualWidth, view.ActualHeight);
        var legend = Position(fit.Legend);
        if (view.LegendPosition != legend) view.LegendPosition = legend;
        foreach (var slice in slices.OfType<PieSeries<double>>())
            if (Math.Abs(slice.InnerRadius - fit.InnerRadius) > 1) slice.InnerRadius = fit.InnerRadius;
    }

    private static SKColor Color(BiTheme theme, int index) => SKColor.Parse(theme.ColorAt(index));
}
