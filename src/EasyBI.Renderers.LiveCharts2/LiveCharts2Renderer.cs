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

/// <summary>Connecteur LiveCharts2 : traduit un <see cref="RenderRequest"/> en CartesianChart / PieChart.</summary>
public sealed class LiveCharts2Renderer : IVisualRenderer
{
    public string Name => "LiveCharts2";

    public RendererCapabilities Capabilities { get; } =
        new(new HashSet<MarkType> { MarkType.Bar, MarkType.Line, MarkType.Pie });

    public FrameworkElement Render(RenderRequest request) => request.Visual.Mark switch
    {
        MarkType.Pie => RenderPie(request),
        MarkType.Bar or MarkType.Line => RenderCartesian(request),
        _ => throw new NotSupportedException($"LiveCharts2Renderer ne gère pas '{request.Visual.Mark}'."),
    };

    private static CartesianChart RenderCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var series = chart.Series.Select((s, i) => CreateSeries(request.Visual.Mark, s, Color(request.Theme, i))).ToArray();
        var format = chart.Series.FirstOrDefault()?.Format;

        return new CartesianChart
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
    }

    private static ISeries CreateSeries(MarkType mark, ChartSeries source, SKColor color)
    {
        // ObservableValue(null) = point absent : LiveCharts2 le dessine comme un trou.
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
        var series = chart.Categories
            .Select((category, i) => (category, value: source.Values[i], i))
            .Where(x => x.value is not null)
            .Select(x => (ISeries)new PieSeries<double>
            {
                Name = x.category,
                Values = [x.value!.Value],
                Fill = new SolidColorPaint(Color(request.Theme, x.i)),
                InnerRadius = 60,
                ToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
            })
            .ToArray();

        return new PieChart { Series = series, LegendPosition = LegendPosition.Right };
    }

    private static SKColor Color(BiTheme theme, int index) => SKColor.Parse(theme.ColorAt(index));
}
