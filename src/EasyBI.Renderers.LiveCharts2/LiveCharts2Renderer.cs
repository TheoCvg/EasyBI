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
            YAxes =
            [
                new Axis
                {
                    Labeler = value => ValueFormatter.Format(value, format),
                    TextSize = 12,
                    SeparatorsPaint = new SolidColorPaint(new SKColor(0xE5, 0xE7, 0xEB)),
                },
            ],
            LegendPosition = chart.Series.Count > 1 ? LegendPosition.Right : LegendPosition.Hidden,
        };

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
