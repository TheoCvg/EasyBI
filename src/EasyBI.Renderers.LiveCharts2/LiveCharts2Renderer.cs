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

    public RendererCapabilities Capabilities { get; } =
        new(new HashSet<MarkType> { MarkType.Bar, MarkType.Line, MarkType.Pie });

    public FrameworkElement Render(RenderRequest request) => request.Visual.Mark switch
    {
        MarkType.Pie => RenderPie(request),
        MarkType.Bar or MarkType.Line => RenderCartesian(request),
        _ => throw new NotSupportedException($"LiveCharts2Renderer does not support '{request.Visual.Mark}'."),
    };

    private static CartesianChart RenderCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var series = chart.Series.Select((s, i) => CreateSeries(request.Visual.Mark, s, Color(request.Theme, i))).ToArray();
        var format = chart.Series.FirstOrDefault()?.Format;

        var categoryAxis = new Axis
        {
            Labels = chart.Categories.ToArray(),
            Name = chart.CategoryCaption,
            NameTextSize = 13,
            TextSize = 12,
        };
        var view = new CartesianChart
        {
            Series = series,
            XAxes = [categoryAxis],
            YAxes =
            [
                new Axis
                {
                    Labeler = value => ValueFormatter.Format(value, format),
                    TextSize = 12,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(0xE5, 0xE7, 0xEB)),
                },
            ],
        };
        // The legend and the axis labels adapt to the room the chart actually gets
        view.SizeChanged += (_, _) => FitCartesian(view, categoryAxis, chart);
        FitCartesian(view, categoryAxis, chart);

        if (request.Select is { } select)
        {
            // Bars: only the clicked bar is picked (category + legend series).
            // Lines: the point closest to the clicked X position.
            view.FindingStrategy = request.Visual.Mark == MarkType.Bar ? FindingStrategy.ExactMatch : FindingStrategy.CompareOnlyXTakeClosest;
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

    private static ISeries CreateSeries(MarkType mark, ChartSeries source, SKColor color)
    {
        // ObservableValue(null) = missing point: LiveCharts2 draws it as a gap.
        var values = source.Values.Select(v => new ObservableValue(v)).ToArray();
        if (mark == MarkType.Line)
        {
            return new LineSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Stroke = new SolidColorPaint(color, 3),
                Fill = null,
                GeometrySize = 8,
                GeometryStroke = new SolidColorPaint(color, 3),
                LineSmoothness = 0.3,
                YToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
            };
        }
        return new ColumnSeries<ObservableValue>
        {
            Name = source.Name,
            Values = values,
            Fill = new SolidColorPaint(color),
            MaxBarWidth = 48,
            YToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
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

        var view = new PieChart { Series = slices.Keys.ToArray() };
        view.SizeChanged += (_, _) => FitPie(view, slices.Keys);
        FitPie(view, slices.Keys);
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

    private const double LegendWidth = 170;
    private const double LabelLineHeight = 16;

    /// <summary>
    /// Legend on the right when there is room, below when the chart is narrow, hidden when it is small.
    /// Category labels stay horizontal when they fit, rotate otherwise, and only every n-th label is
    /// shown when even rotated labels would overlap.
    /// </summary>
    private static void FitCartesian(CartesianChart view, Axis categoryAxis, ChartData chart)
    {
        double width = view.ActualWidth, height = view.ActualHeight;
        if (width <= 0) return;

        var legend = chart.Series.Count <= 1 ? LegendPosition.Hidden
            : width >= 3 * LegendWidth ? LegendPosition.Right
            : height >= 260 ? LegendPosition.Bottom
            : LegendPosition.Hidden;
        if (view.LegendPosition != legend) view.LegendPosition = legend;

        var plotWidth = Math.Max(40, width - 80 - (legend == LegendPosition.Right ? LegendWidth : 0));
        var perLabel = plotWidth / Math.Max(1, chart.Categories.Count);
        var longest = chart.Categories.Select(c => c.Length).DefaultIfEmpty(1).Max() * 7.0;
        double rotation = 0, step = 1;
        if (perLabel < longest + 8)
        {
            rotation = -45;
            step = Math.Max(1, Math.Ceiling(LabelLineHeight / perLabel));
        }
        if (categoryAxis.LabelsRotation != rotation) categoryAxis.LabelsRotation = rotation;
        if (categoryAxis.MinStep != step)
        {
            categoryAxis.MinStep = step;
            categoryAxis.ForceStepToMin = step > 1;
        }
    }

    /// <summary>Legend placed like for cartesian charts; the donut hole keeps the same proportion at any size.</summary>
    private static void FitPie(PieChart view, IEnumerable<ISeries> slices)
    {
        double width = view.ActualWidth, height = view.ActualHeight;
        if (width <= 0) return;

        var legend = width >= 2.4 * LegendWidth && width >= height ? LegendPosition.Right
            : height >= 280 ? LegendPosition.Bottom
            : LegendPosition.Hidden;
        if (view.LegendPosition != legend) view.LegendPosition = legend;

        var pieWidth = width - (legend == LegendPosition.Right ? LegendWidth : 0);
        var pieHeight = height - (legend == LegendPosition.Bottom ? 60 : 0);
        var innerRadius = Math.Max(0, Math.Min(pieWidth, pieHeight) / 2 * 0.5);
        foreach (var slice in slices.OfType<PieSeries<double>>())
            if (Math.Abs(slice.InnerRadius - innerRadius) > 1) slice.InnerRadius = innerRadius;
    }

    private static SKColor Color(BiTheme theme, int index) => SKColor.Parse(theme.ColorAt(index));
}
