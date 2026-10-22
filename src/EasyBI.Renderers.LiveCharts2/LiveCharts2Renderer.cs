using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Renderers.LiveCharts2.Resources;
using EasyBI.Wpf;
using EasyBI.Wpf.Renderers;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace EasyBI.Renderers.LiveCharts2;

/// <summary>LiveCharts2 connector: translates a <see cref="RenderRequest"/> into a CartesianChart / PieChart.</summary>
public sealed class LiveCharts2Renderer : IVisualRenderer
{
    /// <summary>Option showing a scroll bar under a cartesian chart (<see cref="RendererCapabilities.Options"/>).</summary>
    public const string ScrollableOption = "scrollable";

    /// <summary>Categories shown at first by a scrollable chart; the scroll bar and the mouse wheel show the others.</summary>
    public const int ScrollWindow = 20;

    public string Name => "LiveCharts2";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType>
    {
        MarkType.Bar, MarkType.StackedBar, MarkType.Line, MarkType.Area, MarkType.Scatter, MarkType.Pie, MarkType.Donut,
    })
    {
        Options =
        [
            RendererOption.Toggle(ScrollableOption, () => Strings.Scrollable, appliesTo: v => v.Mark.IsCartesian())
                with { Description = () => Strings.ScrollableHint },
        ],
    };

    public FrameworkElement Render(RenderRequest request) => request.Visual.Mark switch
    {
        var mark when mark.IsCircular() => RenderPie(request),
        var mark when mark.IsCartesian() => request.Visual.GetRendererOption<bool>(Name, ScrollableOption)
            ? RenderScrollable(request)
            : RenderCartesian(request),
        _ => throw new NotSupportedException($"LiveCharts2Renderer does not support '{request.Visual.Mark}'."),
    };

    /// <summary>
    /// A cartesian chart showing <see cref="ScrollWindow"/> categories, above a small chart of all of them where a band
    /// marks the visible ones: dragging on it scrolls, the mouse wheel on the chart zooms.
    /// </summary>
    private static Grid RenderScrollable(RenderRequest request)
    {
        var chart = request.Chart;
        var view = RenderCartesian(request);
        var axis = (Axis)view.XAxes.First();
        double first = -0.5, last = chart.Categories.Count - 0.5;
        view.ZoomMode = ZoomAndPanMode.X;
        if (chart.Categories.Count > ScrollWindow)
        {
            axis.MinLimit = first;
            axis.MaxLimit = first + ScrollWindow;
        }

        var thumb = new RectangularSection
        {
            Xi = axis.MinLimit ?? first,
            Xj = axis.MaxLimit ?? last,
            Fill = new SolidColorPaint(Color(request.Theme, 0).WithAlpha(0x40)),
        };
        var scrollbar = new CartesianChart
        {
            Height = 56,
            Margin = new Thickness(0, 6, 0, 0),
            Series = chart.Series.Select((s, i) => (ISeries)new LineSeries<ObservableValue>
            {
                Values = s.Values.Select(v => new ObservableValue(v)).ToArray(),
                Stroke = new SolidColorPaint(Color(request.Theme, i), 1.5f),
                Fill = null,
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0,
                ScalesYAt = s.SecondaryAxis ? 1 : 0,
                IsHoverable = false,
            }).ToArray(),
            XAxes = [new Axis { IsVisible = false, MinLimit = first, MaxLimit = last }],
            YAxes = [new Axis { IsVisible = false }, new Axis { IsVisible = false }],
            Sections = [thumb],
            TooltipPosition = TooltipPosition.Hidden,
            LegendPosition = LegendPosition.Hidden,
            Cursor = System.Windows.Input.Cursors.SizeWE,
        };
        ApplyTheme(scrollbar, request.Theme);

        // Zooming or panning the chart moves the band; its labels adapt to the categories now visible
        view.UpdateStarted += _ =>
        {
            thumb.Xi = axis.MinLimit ?? first;
            thumb.Xj = axis.MaxLimit ?? last;
            view.Dispatcher.InvokeAsync(() => FitCartesian(view, axis, chart));
        };

        // Dragging on the scroll bar centers the visible categories on the pointer
        var dragging = false;
        void ScrollTo(Point position)
        {
            var center = scrollbar.ScalePixelsToData(new LvcPointD(position.X, position.Y)).X;
            var range = (axis.MaxLimit ?? last) - (axis.MinLimit ?? first);
            var start = Math.Clamp(center - range / 2, first, Math.Max(first, last - range));
            axis.MinLimit = start;
            axis.MaxLimit = start + range;
        }
        scrollbar.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragging = scrollbar.CaptureMouse();
            ScrollTo(e.GetPosition(scrollbar));
        };
        scrollbar.PreviewMouseMove += (_, e) =>
        {
            if (dragging) ScrollTo(e.GetPosition(scrollbar));
        };
        scrollbar.PreviewMouseLeftButtonUp += (_, _) =>
        {
            dragging = false;
            scrollbar.ReleaseMouseCapture();
        };

        var grid = new Grid { RowDefinitions = { new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } } };
        Grid.SetRow(scrollbar, 1);
        grid.Children.Add(view);
        grid.Children.Add(scrollbar);
        return grid;
    }

    /// <summary>
    /// Bars, stacked bars, lines, areas and points in one chart: each series is drawn with its own
    /// <see cref="ChartSeries.Mark"/>, against the left axis or, for <see cref="ChartSeries.SecondaryAxis"/>, the right one.
    /// </summary>
    private static CartesianChart RenderCartesian(RenderRequest request)
    {
        var chart = request.Chart;
        var formatting = ChartFormatting.For(request);
        var series = chart.Series.Select((_, i) => CreateSeries(request, formatting, i)).ToArray();
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
            Series = [.. series, .. Outlines(formatting, chart.Categories.Count)],
            XAxes = [categoryAxis],
            YAxes = hasSecondary
                ? [ValueAxis(chart, theme, secondary: false), ValueAxis(chart, theme, secondary: true)]
                : [ValueAxis(chart, theme, secondary: false)],
            LegendPosition = chart.Series.Count > 1 ? LegendPosition.Right : LegendPosition.Hidden,
        };
        ApplyTheme(view, theme);
        // LiveCharts2 has no open section end: far beyond the data, the chart clips it to the axis
        var far = chart.Series.SelectMany(s => s.Values).OfType<double>()
            .Concat(formatting.Bands.SelectMany(b => new[] { b.From, b.To }).OfType<double>())
            .Select(Math.Abs).DefaultIfEmpty(1).Max() * 1000 + 1;
        view.Sections = [.. formatting.Bands.Select(band => Zone(band, theme, far))];
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
            OnPointClicked(view, view.FindingStrategy, point =>
            {
                var index = Array.IndexOf(series, point.Context.Series);
                select(new CategorySelection(point.Index, index < 0 ? null : index));
            });
        }
        return view;
    }

    private static void FitCartesian(CartesianChart view, Axis categoryAxis, ChartData chart)
    {
        if (view.ActualWidth <= 0) return;
        // A zoomed or scrollable chart shows part of the categories: labels are fitted to those
        var shown = categoryAxis.MaxLimit - categoryAxis.MinLimit is double range && range < chart.Categories.Count
            ? chart with { Categories = chart.Categories.Take(Math.Max(1, (int)Math.Ceiling(range))).ToArray() }
            : chart;
        var fit = ChartFitting.Cartesian(shown, view.ActualWidth, view.ActualHeight);

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
    private static void ApplyTheme(Control view, BiTheme theme)
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

    /// <summary>
    /// The frames of the stacks a rule or a scale picks out (<see cref="StackOutline"/>): one column series per color,
    /// empty inside, centered on the category like the stack (same width) and drawn over it.
    /// </summary>
    private static IEnumerable<ISeries> Outlines(ChartFormatting formatting, int categories) =>
        formatting.StackOutlines
            .GroupBy(o => (o.Color, o.SecondaryAxis))
            .Select(group => new ColumnSeries<ObservableValue>
            {
                Values = [.. Enumerable.Range(0, categories).Select(c => new ObservableValue(group.FirstOrDefault(o => o.Category == c)?.Top))],
                Fill = null,
                Stroke = new SolidColorPaint(SKColor.Parse(group.Key.Color), 3),
                MaxBarWidth = 48,
                IgnoresBarPosition = true,
                ScalesYAt = group.Key.SecondaryAxis ? 1 : 0,
                IsHoverable = false,
                IsVisibleAtLegend = false,
                ZIndex = 1000,
            });

    /// <summary>
    /// The zone of a rule shown on the chart (<see cref="ChartBand"/>): a translucent band across the chart between
    /// its values, an open end reaching the edge of the axis, with the rule's condition as label.
    /// </summary>
    private static RectangularSection Zone(ChartBand band, BiTheme theme, double far)
    {
        var color = SKColor.Parse(band.Color);
        return new RectangularSection
        {
            Yi = band.From ?? -far,
            Yj = band.To ?? far,
            ScalesYAt = band.SecondaryAxis ? 1 : 0,
            Fill = new SolidColorPaint(color.WithAlpha(0x26)),
            Stroke = new SolidColorPaint(color.WithAlpha(0x99), 1),
            Label = band.Label,
            LabelSize = 11,
            LabelPaint = new SolidColorPaint(Parse(theme.MutedForeground) ?? color),
            ZIndex = -1,
        };
    }

    /// <summary>
    /// The series in its color; a point whose measure's format gives a color (a rule, a scale) is drawn in it, as in
    /// LiveCharts2's conditional draw. With a highlight (cross-filtering), the points left out of it are faded, and so
    /// is the line or area of a series that has none of them.
    /// </summary>
    private static ISeries CreateSeries(RenderRequest request, ChartFormatting formatting, int index)
    {
        var source = request.Chart.Series[index];
        var color = Color(request.Theme, index);
        var lineColor = request.IsEmphasized(index) ? color : Dim(color);
        // ObservableValue(null) = missing point: LiveCharts2 draws it as a gap (or no bar, no point).
        var values = source.Values.Select(v => new ObservableValue(v)).ToArray();
        var axis = source.SecondaryAxis ? 1 : 0;
        string Tooltip(double value) => ValueFormatter.Format(value, source.Format);
        var paints = new Dictionary<string, SolidColorPaint>();
        void Fade(IDrawnElement? point, int category)
        {
            if (point is null) return;
            if (request.Highlight is { Count: > 0 })
                point.Opacity = request.IsEmphasized(category, index) ? 1f : (float)SelectionFilters.DimmedOpacity;
            if (formatting.ColorsPoints && point is DrawnGeometry geometry)
            {
                // Null gives the point back the series' paint
                var paint = formatting.PointColor(index, category) is { } hex
                    ? paints.TryGetValue(hex, out var known) ? known : paints[hex] = new SolidColorPaint(SKColor.Parse(hex))
                    : null;
                geometry.Fill = paint;
                if (source.Mark is MarkType.Line or MarkType.Scatter) geometry.Stroke = paint;
            }
        }

        return source.Mark switch
        {
            MarkType.Line or MarkType.Area => new LineSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Stroke = new SolidColorPaint(lineColor, 3),
                Fill = source.Mark == MarkType.Area ? new SolidColorPaint(color.WithAlpha((byte)(lineColor.Alpha * 0x55 / 0xFF))) : null,
                GeometrySize = source.Mark == MarkType.Area ? 0 : 8,
                GeometryStroke = new SolidColorPaint(color, 3),
                LineSmoothness = 0.3,
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            }.OnPointMeasured(p => Fade(p.Visual, p.Index)),
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
            }.OnPointMeasured(p => Fade(p.Visual, p.Index)),
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
            }.OnPointMeasured(p => Fade(p.Visual, p.Index)),
            _ => new ColumnSeries<ObservableValue>
            {
                Name = source.Name,
                Values = values,
                Fill = new SolidColorPaint(color),
                MaxBarWidth = 48,
                ScalesYAt = axis,
                YToolTipLabelFormatter = p => Tooltip(p.Coordinate.PrimaryValue),
            }.OnPointMeasured(p => Fade(p.Visual, p.Index)),
        };
    }

    // A slice: the color of its rule or scale, else the palette's; faded when left out of the highlight
    private static SKColor SliceColor(RenderRequest request, ChartFormatting formatting, int category)
    {
        var color = formatting.PointColor(0, category) is { } hex ? SKColor.Parse(hex) : Color(request.Theme, category);
        return request.IsEmphasized(category, 0) ? color : Dim(color);
    }

    private static SKColor Dim(SKColor color) => color.WithAlpha((byte)(color.Alpha * SelectionFilters.DimmedOpacity));

    /// <summary>A pie (a full disc), or a donut with the total in its hole.</summary>
    private static FrameworkElement RenderPie(RenderRequest request)
    {
        var donut = request.Visual.Mark == MarkType.Donut;
        var chart = request.Chart;
        var source = chart.Series[0];
        var formatting = ChartFormatting.For(request);
        // One series per slice: keep each slice's category index for selection
        var slices = new Dictionary<ISeries, int>();
        foreach (var (category, i) in chart.Categories.Select((category, i) => (category, i)))
        {
            if (source.Values[i] is not { } value) continue;
            slices[new PieSeries<double>
            {
                Name = category,
                Values = [value],
                Fill = new SolidColorPaint(SliceColor(request, formatting, i)),
                ToolTipLabelFormatter = p => ValueFormatter.Format(p.Coordinate.PrimaryValue, source.Format),
            }] = i;
        }

        var view = new PieChart { Series = slices.Keys.ToArray(), LegendPosition = LegendPosition.Right };
        ApplyTheme(view, request.Theme);
        // Legend and donut hole adapt to the room the chart actually gets
        view.SizeChanged += (_, _) => FitPie(view, slices.Keys, chart, donut);
        if (request.Select is { } select)
        {
            view.Cursor = System.Windows.Input.Cursors.Hand;
            OnPointClicked(view, FindingStrategy.Automatic, point =>
            {
                if (slices.TryGetValue(point.Context.Series, out var category))
                    select(new CategorySelection(category));
            });
        }
        if (!donut) return view;

        var total = source.Values.OfType<double>().Sum();
        var label = new ChartCenterLabel(view, () =>
            view.CoreChart is { DrawMarginSize: { Width: > 0 } size, DrawMarginLocation: var location }
                ? new Rect(location.X, location.Y, size.Width, size.Height)
                : null,
            ValueFormatter.Format(total, source.Format), source.Name);
        // LiveCharts2 measures the pie after WPF's layout
        view.UpdateFinished += _ => view.Dispatcher.InvokeAsync(label.Place);
        return label;
    }

    /// <summary>
    /// Reports the clicked point. LiveCharts2 ignores a click made with a modifier key held down, so a Ctrl+click
    /// (adding to the selection) looks for the point itself.
    /// </summary>
    private static void OnPointClicked<TView>(TView view, FindingStrategy strategy, Action<ChartPoint> clicked)
        where TView : Control, LiveChartsCore.Kernel.Sketches.IChartView
    {
        view.DataPointerDown += (_, points) =>
        {
            if (points?.FirstOrDefault() is { } point) clicked(point);
        };
        view.MouseLeftButtonDown += (_, e) =>
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None) return;
            var position = e.GetPosition(view);
            if (view.GetPointsAt(new LvcPointD(position.X, position.Y), strategy).FirstOrDefault() is { } point) clicked(point);
        };
    }

    private static void FitPie(PieChart view, IEnumerable<ISeries> slices, ChartData chart, bool donut)
    {
        if (view.ActualWidth <= 0) return;
        var fit = ChartFitting.Pie(chart, view.ActualWidth, view.ActualHeight, donut);
        var legend = Position(fit.Legend);
        if (view.LegendPosition != legend) view.LegendPosition = legend;
        foreach (var slice in slices.OfType<PieSeries<double>>())
            if (Math.Abs(slice.InnerRadius - fit.InnerRadius) > 1) slice.InnerRadius = fit.InnerRadius;
    }

    private static SKColor Color(BiTheme theme, int index) => SKColor.Parse(theme.ColorAt(index));
}
