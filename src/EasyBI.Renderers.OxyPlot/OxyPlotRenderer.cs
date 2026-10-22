using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf;
using OxyPlot;
using OxyPlot.Annotations;
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
        ApplyTheme(model, request.Theme);
        var view = new PlotView { Model = model, Background = System.Windows.Media.Brushes.Transparent };
        // Legend, labels and axis title adapt to the room the chart actually gets
        view.SizeChanged += (_, _) => Fit(view, model, request);
        if (request.Select is { } select)
            view.Controller = SelectionController(model, select);
        return view;
    }

    private static void Fit(PlotView view, PlotModel model, RenderRequest request)
    {
        if (view.ActualWidth <= 0) return;
        var changed = request.Visual.Mark == MarkType.Pie
            ? FitPie(model, ChartFitting.Pie(request.Chart, view.ActualWidth, view.ActualHeight))
            : FitCartesian(model, request.Chart, ChartFitting.Cartesian(request.Chart, view.ActualWidth, view.ActualHeight));
        // Only redraw when something changed: SizeChanged fires continuously while a dashboard cell is resized
        if (changed) model.InvalidatePlot(false);
    }

    private static bool FitCartesian(PlotModel model, ChartData chart, ChartFit fit)
    {
        var changed = false;
        var axis = (CategoryAxis)model.Axes.First(a => a.Key == CategoryAxisKey);
        if (axis.Angle != fit.LabelRotation) (axis.Angle, changed) = (fit.LabelRotation, true);
        if (axis.MajorStep != fit.LabelStep) (axis.MajorStep, changed) = (fit.LabelStep, true);
        var title = fit.ShowAxisTitles ? chart.CategoryCaption : null;
        if (axis.Title != title) (axis.Title, changed) = (title, true);

        var visible = fit.Legend != ChartLegend.Hidden;
        if (model.IsLegendVisible != visible) (model.IsLegendVisible, changed) = (visible, true);
        if (visible && model.Legends.FirstOrDefault() is { } legend)
        {
            var position = fit.Legend == ChartLegend.Bottom ? LegendPosition.BottomCenter : LegendPosition.RightTop;
            if (legend.LegendPosition != position)
            {
                legend.LegendPosition = position;
                legend.LegendOrientation = fit.Legend == ChartLegend.Bottom ? LegendOrientation.Horizontal : LegendOrientation.Vertical;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// OxyPlot draws the category names around the pie instead of in a legend: they are dropped when the fit has no
    /// room for a legend. The donut hole is relative to the pie, so it already keeps its proportions.
    /// </summary>
    private static bool FitPie(PlotModel model, ChartFit fit)
    {
        var pie = (PieSeries)model.Series[0];
        var format = fit.Legend == ChartLegend.Hidden ? null : PieOutsideLabelFormat;
        if (pie.OutsideLabelFormat == format) return false;
        pie.OutsideLabelFormat = format;
        (pie.TickHorizontalLength, pie.TickRadialLength) = format is null ? (0, 0) : (8, 6);
        return true;
    }

    /// <summary>
    /// Left click: shows the tracker as usual, and selects the element that was hit. A binding only applies to its own
    /// modifier keys: Ctrl+click (adding to the selection) is bound too.
    /// </summary>
    private static PlotController SelectionController(PlotModel model, Action<DataSelection> select)
    {
        var controller = new PlotController();
        var click = new DelegatePlotCommand<OxyMouseDownEventArgs>((view, ctl, args) =>
        {
            global::OxyPlot.PlotCommands.SnapTrack.Execute(view, ctl, args);
            if (HitTest(model, args.Position) is { } selection) select(selection);
        });
        controller.BindMouseDown(OxyMouseButton.Left, click);
        controller.BindMouseDown(OxyMouseButton.Left, OxyModifierKeys.Control, click);
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
            AxisTitleDistance = 10,
            Key = CategoryAxisKey,
        };
        categoryAxis.Labels.AddRange(chart.Categories);
        model.Axes.Add(categoryAxis);
        model.Axes.Add(ValueAxis(chart, secondary: false));
        if (chart.Series.Any(s => s.SecondaryAxis))
            model.Axes.Add(ValueAxis(chart, secondary: true));

        var formatting = ChartFormatting.For(request);
        for (var i = 0; i < chart.Series.Count; i++)
            model.Series.Add(CreateSeries(request, formatting, i));
        foreach (var band in formatting.Bands)
            model.Annotations.Add(Zone(band, request.Theme));

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

    /// <summary>
    /// The series in its color. With a highlight (cross-filtering), the bars left out of it are faded; OxyPlot has
    /// no color per point for lines and points, so a series is faded when none of its points is highlighted.
    /// </summary>
    /// <summary>
    /// The zone of a rule shown on the chart (<see cref="ChartBand"/>): a translucent band across the chart between
    /// its values, an open end reaching the edge of the value axis, with the rule's condition as text.
    /// </summary>
    private static RectangleAnnotation Zone(ChartBand band, BiTheme theme)
    {
        var color = OxyColor.Parse(band.Color);
        var zone = new RectangleAnnotation
        {
            XAxisKey = CategoryAxisKey,
            YAxisKey = band.SecondaryAxis ? SecondaryAxisKey : PrimaryAxisKey,
            Fill = OxyColor.FromAColor(0x26, color),
            Stroke = OxyColor.FromAColor(0x99, color),
            StrokeThickness = 1,
            Text = band.Label,
            TextColor = Parse(theme.MutedForeground) ?? color,
            TextHorizontalAlignment = global::OxyPlot.HorizontalAlignment.Left,
            Layer = AnnotationLayer.BelowSeries,
        };
        if (band.From is { } from) zone.MinimumY = from;
        if (band.To is { } to) zone.MaximumY = to;
        return zone;
    }

    private static Series CreateSeries(RenderRequest request, ChartFormatting formatting, int index)
    {
        var source = request.Chart.Series[index];
        var color = request.IsEmphasized(index) ? Color(request.Theme, index) : Dim(Color(request.Theme, index));
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
                    // Segments of a stack colored alike stay apart: a thin line of the background between them
                    StrokeColor = source.Mark == MarkType.StackedBar && formatting.ColorsPoints ? Parse(request.Theme.Background) ?? OxyColors.White : OxyColors.Automatic,
                    StrokeThickness = source.Mark == MarkType.StackedBar && formatting.ColorsPoints ? 1 : 0,
                    // Series on the right axis stack separately from those on the left
                    StackGroup = source.SecondaryAxis ? SecondaryAxisKey : PrimaryAxisKey,
                    TrackerFormatString = "{0}\n{1}: {2:" + (source.Format ?? "#,##0.##") + "}",
                };
                bars.Items.AddRange(source.Values.Select((v, category) => new BarItem(v ?? 0)
                {
                    Color = BarColor(request, formatting, index, category),
                }));
                return bars;
        }
    }

    private const string PieOutsideLabelFormat = "{1}";

    private static PlotModel BuildPie(RenderRequest request)
    {
        var chart = request.Chart;
        var source = chart.Series[0];
        var formatting = ChartFormatting.For(request);
        var pie = new PieSeries
        {
            InnerDiameter = 0.45,
            StrokeThickness = 2,
            Stroke = OxyColors.White,
            InsideLabelPosition = 0.75,
            InsideLabelColor = OxyColors.White,
            InsideLabelFormat = "{2:0}%",
            OutsideLabelFormat = PieOutsideLabelFormat,
            AngleSpan = 360,
            StartAngle = -90,
            TrackerFormatString = "{1}: {2:" + (source.Format ?? "#,##0.##") + "}",
        };
        var categories = new List<int>();
        for (var i = 0; i < chart.Categories.Count; i++)
        {
            if (source.Values[i] is not { } value) continue;
            var fill = formatting.PointColor(0, i) is { } formatted ? OxyColor.Parse(formatted) : Color(request.Theme, i);
            pie.Slices.Add(new PieSlice(chart.Categories[i], value) { Fill = request.IsEmphasized(i, 0) ? fill : Dim(fill) });
            categories.Add(i);
        }
        // Categories without a value have no slice: slice index → category index
        pie.Tag = categories.ToArray();
        var model = new PlotModel();
        model.Series.Add(pie);
        return model;
    }

    private static OxyColor Color(BiTheme theme, int index) => OxyColor.Parse(theme.ColorAt(index));

    // A bar: the color of its rule or scale (as LiveCharts2's conditional draw), else the series'; faded when left out of the highlight
    private static OxyColor BarColor(RenderRequest request, ChartFormatting formatting, int series, int category)
    {
        var formatted = formatting.PointColor(series, category) is { } hex ? OxyColor.Parse(hex) : (OxyColor?)null;
        if (request.IsEmphasized(category, series)) return formatted ?? OxyColors.Automatic;
        return Dim(formatted ?? Color(request.Theme, series));
    }

    private static OxyColor Dim(OxyColor color) => OxyColor.FromAColor((byte)(color.A * SelectionFilters.DimmedOpacity), color);

    /// <summary>Text, axes, grid lines and slice borders in the theme's colors (EasyBI's light colors when it has none).</summary>
    private static void ApplyTheme(PlotModel model, BiTheme theme)
    {
        model.TextColor = Parse(theme.Foreground) ?? OxyColor.FromRgb(0x37, 0x41, 0x51);
        var grid = Parse(theme.GridLine) ?? OxyColor.FromRgb(0xE5, 0xE7, 0xEB);
        foreach (var axis in model.Axes)
        {
            axis.MajorGridlineColor = grid;
            axis.TicklineColor = grid;
            axis.AxislineColor = grid;
            if (Parse(theme.MutedForeground) is { } labels) axis.TextColor = labels;
        }
        // Slices are separated by the color they are drawn on
        foreach (var pie in model.Series.OfType<PieSeries>())
            pie.Stroke = Parse(theme.Background) ?? OxyColors.White;
    }

    private static OxyColor? Parse(string? color) => color is null ? null : OxyColor.Parse(color);
}
