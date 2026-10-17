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

/// <summary>OxyPlot connector: translates a <see cref="RenderRequest"/> into a PlotModel.</summary>
public sealed class OxyPlotRenderer : IVisualRenderer
{
    public string Name => "OxyPlot";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType>
    {
        MarkType.Bar, MarkType.StackedBar, MarkType.Line, MarkType.Area, MarkType.Scatter, MarkType.Pie,
    });

    public FrameworkElement Render(RenderRequest request)
    {
        var model = request.Visual.Mark switch
        {
            MarkType.Pie => BuildPie(request),
            var mark when mark.IsCartesian() => BuildCartesian(request),
            _ => throw new NotSupportedException($"OxyPlotRenderer does not support '{request.Visual.Mark}'."),
        };
        model.PlotAreaBorderThickness = new OxyThickness(0);
        model.TextColor = OxyColor.FromRgb(0x37, 0x41, 0x51);
        var view = new PlotView { Model = model, Background = System.Windows.Media.Brushes.Transparent };
        if (request.Select is { } select)
            view.Controller = SelectionController(model, select);
        return view;
    }

    /// <summary>Left click: shows the tracker as usual, and selects the element that was hit.</summary>
    private static PlotController SelectionController(PlotModel model, Action<DataSelection> select)
    {
        var controller = new PlotController();
        controller.BindMouseDown(OxyMouseButton.Left, new DelegatePlotCommand<OxyMouseDownEventArgs>((view, ctl, args) =>
        {
            global::OxyPlot.PlotCommands.SnapTrack.Execute(view, ctl, args);
            if (HitTest(model, args.Position) is { } selection) select(selection);
        }));
        return controller;
    }

    private static DataSelection? HitTest(PlotModel model, ScreenPoint position)
    {
        const double tolerance = 12;
        for (var s = 0; s < model.Series.Count; s++)
        {
            var hit = model.Series[s].GetNearestPoint(position, false);
            if (hit is null) continue;
            switch (model.Series[s])
            {
                case PieSeries pie when hit.Item is PieSlice slice && pie.Tag is int[] categories:
                    return new CategorySelection(categories[pie.Slices.IndexOf(slice)]);
                case BarSeries:
                    return new CategorySelection((int)hit.Index, s);
                case LineSeries or ScatterSeries when hit.Position.DistanceTo(position) <= tolerance:
                    return new CategorySelection((int)Math.Round(hit.DataPoint.X), s);
            }
        }
        return null;
    }

    private const string CategoryAxisKey = "categories";
    private const string PrimaryAxisKey = "values";
    private const string SecondaryAxisKey = "values2";

    /// <summary>
    /// Bars, stacked bars, lines, areas and points in one chart: each series is drawn with its own
    /// <see cref="ChartSeries.Mark"/>, against the left axis or, for <see cref="ChartSeries.SecondaryAxis"/>, the right one.
    /// </summary>
    private static PlotModel BuildCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var model = new PlotModel();

        var categoryAxis = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Title = chart.CategoryCaption,
            Angle = chart.Categories.Count > 8 ? -45 : 0,
            AxisTitleDistance = 10,
            Key = CategoryAxisKey,
        };
        categoryAxis.Labels.AddRange(chart.Categories);
        model.Axes.Add(categoryAxis);
        model.Axes.Add(ValueAxis(chart, secondary: false));
        if (chart.Series.Any(s => s.SecondaryAxis))
            model.Axes.Add(ValueAxis(chart, secondary: true));

        for (var i = 0; i < chart.Series.Count; i++)
            model.Series.Add(CreateSeries(chart.Series[i], Color(request.Theme, i)));

        if (chart.Series.Count > 1)
            model.Legends.Add(new Legend { LegendPosition = LegendPosition.RightTop, LegendPlacement = LegendPlacement.Outside });
        return model;
    }

    /// <summary>A value axis formatted like the first series drawn against it; bars start at zero.</summary>
    private static LinearAxis ValueAxis(ChartData chart, bool secondary)
    {
        var series = chart.Series.Where(s => s.SecondaryAxis == secondary).ToList();
        var format = series.FirstOrDefault()?.Format;
        var onlyBars = series.Count > 0 && series.All(s => s.Mark is MarkType.Bar or MarkType.StackedBar);
        return new LinearAxis
        {
            Position = secondary ? AxisPosition.Right : AxisPosition.Left,
            Key = secondary ? SecondaryAxisKey : PrimaryAxisKey,
            LabelFormatter = value => ValueFormatter.Format(value, format),
            // Grid lines come from the left axis only, so that the two scales do not draw two grids
            MajorGridlineStyle = secondary ? LineStyle.None : LineStyle.Solid,
            MajorGridlineColor = OxyColor.FromRgb(0xE5, 0xE7, 0xEB),
            MinimumPadding = onlyBars ? 0 : 0.05,
            MaximumPadding = 0.08,
            AbsoluteMinimum = onlyBars ? 0 : double.MinValue,
        };
    }

    private static Series CreateSeries(ChartSeries source, OxyColor color)
    {
        var valueAxis = source.SecondaryAxis ? SecondaryAxisKey : PrimaryAxisKey;
        var valueFormat = "{4:" + (source.Format ?? "#,##0.##") + "}";
        switch (source.Mark)
        {
            case MarkType.Line or MarkType.Area:
                var line = source.Mark == MarkType.Area
                    ? new AreaSeries { Fill = OxyColor.FromAColor(0x55, color), Color2 = OxyColors.Transparent, ConstantY2 = 0 }
                    : new LineSeries { MarkerType = MarkerType.Circle, MarkerSize = 4, MarkerFill = color };
                line.Title = source.Name;
                line.Color = color;
                line.StrokeThickness = 3;
                line.XAxisKey = CategoryAxisKey;
                line.YAxisKey = valueAxis;
                line.TrackerFormatString = "{0}\n{2}: " + valueFormat;
                // DataPoint.Undefined = missing point: OxyPlot breaks the line.
                line.Points.AddRange(source.Values.Select((v, x) => v is null ? DataPoint.Undefined : new DataPoint(x, v.Value)));
                return line;

            case MarkType.Scatter:
                var points = new ScatterSeries
                {
                    Title = source.Name,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 6,
                    MarkerFill = color,
                    XAxisKey = CategoryAxisKey,
                    YAxisKey = valueAxis,
                    TrackerFormatString = "{0}\n{2}: " + valueFormat,
                };
                points.Points.AddRange(source.Values
                    .Select((v, x) => (Value: v, X: x))
                    .Where(p => p.Value is not null)
                    .Select(p => new ScatterPoint(p.X, p.Value!.Value)));
                return points;

            default:
                // Since OxyPlot 2.1, BarSeries follows its axes: categories at the bottom = columns.
                var bars = new BarSeries
                {
                    Title = source.Name,
                    FillColor = color,
                    XAxisKey = valueAxis,
                    YAxisKey = CategoryAxisKey,
                    IsStacked = source.Mark == MarkType.StackedBar,
                    // Series on the right axis stack separately from those on the left
                    StackGroup = source.SecondaryAxis ? SecondaryAxisKey : PrimaryAxisKey,
                    TrackerFormatString = "{0}\n{1}: {2:" + (source.Format ?? "#,##0.##") + "}",
                };
                bars.Items.AddRange(source.Values.Select(v => new BarItem(v ?? 0)));
                return bars;
        }
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
        var categories = new List<int>();
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            if (source.Values[i] is not { } value) continue;
            pie.Slices.Add(new PieSlice(chart.Categories[i], value) { Fill = Color(request.Theme, i) });
            categories.Add(i);
        }
        // Categories without a value have no slice: slice index → category index
        pie.Tag = categories.ToArray();
        var model = new PlotModel();
        model.Series.Add(pie);
        return model;
    }

    private static OxyColor Color(BiTheme theme, int index) => OxyColor.Parse(theme.ColorAt(index));
}
