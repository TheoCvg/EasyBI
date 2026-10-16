using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.Wpf;

namespace EasyBI.Renderers.OxyPlot;

/// <summary>Connecteur OxyPlot : traduit un <see cref="RenderRequest"/> en PlotModel.</summary>
public sealed class OxyPlotRenderer : IVisualRenderer
{
    public string Name => "OxyPlot";

    public RendererCapabilities Capabilities { get; } =
        new(new HashSet<MarkType> { MarkType.Bar, MarkType.Line, MarkType.Pie });

    public FrameworkElement Render(RenderRequest request)
    {
        var model = request.Visual.Mark switch
        {
            MarkType.Pie => BuildPie(request),
            MarkType.Bar or MarkType.Line => BuildCartesian(request),
            _ => throw new NotSupportedException($"OxyPlotRenderer ne gère pas '{request.Visual.Mark}'."),
        };
        model.PlotAreaBorderThickness = new OxyThickness(0);
        model.TextColor = OxyColor.FromRgb(0x37, 0x41, 0x51);
        return new PlotView { Model = model, Background = System.Windows.Media.Brushes.Transparent };
    }

    private static PlotModel BuildCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var format = chart.Series.FirstOrDefault()?.Format;
        var model = new PlotModel();

        var categoryAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.CategoryCaption,
            Angle = chart.Categories.Count > 8 ? -45 : 0,
            AxisTitleDistance = 10,
            Key = "categories",
        };
        categoryAxis.Labels.AddRange(chart.Categories);
        model.Axes.Add(categoryAxis);
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "values",
            LabelFormatter = value => ValueFormatter.Format(value, format),
            MajorGridlineStyle = LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xE5, 0xE7, 0xEB),
            MinimumPadding = request.Visual.Mark == MarkType.Bar ? 0 : 0.05,
            MaximumPadding = 0.08,
            AbsoluteMinimum = request.Visual.Mark == MarkType.Bar ? 0 : double.MinValue,
        });

        for (var i = 0; i < chart.Series.Count; i++)
        {
            var source = chart.Series[i];
            var color = Color(request.Theme, i);
            if (request.Visual.Mark == MarkType.Line)
            {
                var line = new LineSeries
                {
                    Title = source.Name,
                    Color = color,
                    StrokeThickness = 3,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 4,
                    MarkerFill = color,
                    TrackerFormatString = "{0}\n{2}: {4:" + (format ?? "#,##0.##") + "}",
                };
                // DataPoint.Undefined = point absent : OxyPlot coupe la ligne.
                line.Points.AddRange(source.Values.Select((v, x) => v is null ? DataPoint.Undefined : new DataPoint(x, v.Value)));
                model.Series.Add(line);
            }
            else
            {
                // Depuis OxyPlot 2.1, BarSeries s'oriente selon ses axes : catégories en bas = colonnes.
                var bars = new BarSeries
                {
                    Title = source.Name,
                    FillColor = color,
                    XAxisKey = model.Axes[1].Key,
                    YAxisKey = categoryAxis.Key,
                    TrackerFormatString = "{0}\n{1}: {2:" + (format ?? "#,##0.##") + "}",
                };
                bars.Items.AddRange(source.Values.Select(v => new BarItem(v ?? 0)));
                model.Series.Add(bars);
            }
        }

        if (chart.Series.Count > 1)
            model.Legends.Add(new Legend { LegendPosition = LegendPosition.RightTop, LegendPlacement = LegendPlacement.Outside });
        return model;
    }

    private static PlotModel BuildPie(RenderRequest request)
    {
        var chart = request.Chart;
        var source = chart.Series[0];
        var pie = new PieSeries
        {
            InnerDiameter = 0.45,
            StrokeThickness = 2,
            Stroke = OxyColors.White,
            InsideLabelPosition = 0.75,
            InsideLabelColor = OxyColors.White,
            InsideLabelFormat = "{2:0}%",
            OutsideLabelFormat = "{1}",
            AngleSpan = 360,
            StartAngle = -90,
            TrackerFormatString = "{1}: {2:" + (source.Format ?? "#,##0.##") + "}",
        };
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            if (source.Values[i] is { } value)
                pie.Slices.Add(new PieSlice(chart.Categories[i], value) { Fill = Color(request.Theme, i) });
        }
        var model = new PlotModel();
        model.Series.Add(pie);
        return model;
    }

    private static OxyColor Color(BiTheme theme, int index) => OxyColor.Parse(theme.ColorAt(index));
}
