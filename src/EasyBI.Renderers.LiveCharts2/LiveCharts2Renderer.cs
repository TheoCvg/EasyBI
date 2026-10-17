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

        var view = new CartesianChart
        {
            Series = series,
            XAxes =
            [
                new Axis
                {
                    Labels = chart.Categories.ToArray(),
                    LabelsRotation = chart.Categories.Count > 8 ? -45 : 0,
                    Name = chart.CategoryCaption,
                    NameTextSize = 13,
                    TextSize = 12,
                },
            ],
            YAxes = hasSecondary
                ? [ValueAxis(chart, secondary: false), ValueAxis(chart, secondary: true)]
                : [ValueAxis(chart, secondary: false)],
            LegendPosition = chart.Series.Count > 1 ? LegendPosition.Right : LegendPosition.Hidden,
        };

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

    /// <summary>A value axis formatted like the first series drawn against it.</summary>
    private static Axis ValueAxis(ChartData chart, bool secondary)
    {
        var series = chart.Series.Where(s => s.SecondaryAxis == secondary).ToList();
        var format = series.FirstOrDefault()?.Format;
        // Bars are compared by their height: an axis showing only bars of positive values starts at zero
        var barsFromZero = series.Count > 0
            && series.All(s => s.Mark is MarkType.Bar or MarkType.StackedBar)
            && series.SelectMany(s => s.Values).All(v => v is null or >= 0);
        return new Axis
        {
            MinLimit = barsFromZero ? 0 : null,
            Labeler = value => ValueFormatter.Format(value, format),
            TextSize = 12,
            Position = secondary ? AxisPosition.End : AxisPosition.Start,
            // Grid lines come from the left axis only, so that the two scales do not draw two grids
            SeparatorsPaint = secondary ? null : new SolidColorPaint(new SKColor(0xE5, 0xE7, 0xEB)),
        };
    }

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
                InnerRadius = 60,
                ToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
            }] = i;
        }

        var view = new PieChart { Series = slices.Keys.ToArray(), LegendPosition = LegendPosition.Right };
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

    private static SKColor Color(BiTheme theme, int index) => SKColor.Parse(theme.ColorAt(index));
}
