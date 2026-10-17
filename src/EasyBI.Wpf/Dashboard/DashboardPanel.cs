using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Lays out a dashboard: a stack of <see cref="Bands"/>, each with its own <see cref="BandMode"/>.
/// Like a <c>Canvas</c> or a <c>Grid</c>, it only places its children according to attached properties
/// (<c>Band</c>, <c>Column</c>, <c>Row</c>, <c>ColumnSpan</c>, <c>RowSpan</c>, <c>ItemWidth</c>, <c>IsFullRow</c>);
/// <see cref="DashboardLayout"/> moves them while keeping the layout consistent.
/// <list type="bullet">
/// <item><see cref="BandMode.Fit"/>: a grid of <see cref="Columns"/> columns sharing the width and rows of <see cref="RowHeight"/>;</item>
/// <item><see cref="BandMode.Scroll"/>: items of <c>ItemWidth</c> side by side in the order of their <c>Column</c>,
/// shifted by <see cref="DashboardBand.HorizontalOffset"/> and clipped to the panel;</item>
/// <item><see cref="BandMode.Wrap"/>: the same items wrapping onto new lines.</item>
/// </list>
/// </summary>
public class DashboardPanel : Panel
{
    private const FrameworkPropertyMetadataOptions ParentLayout =
        FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange;

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(DashboardLayoutDefinition.DefaultColumns, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (int)v >= 1);

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight), typeof(double), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(DashboardLayoutDefinition.DefaultRowHeight, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (double)v > 0);

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(DashboardPanel), new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Vertical space between two bands.</summary>
    public static readonly DependencyProperty BandSpacingProperty = DependencyProperty.Register(
        nameof(BandSpacing), typeof(double), typeof(DashboardPanel), new FrameworkPropertyMetadata(24d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty BandProperty = DependencyProperty.RegisterAttached(
        "Band", typeof(int), typeof(DashboardPanel), new FrameworkPropertyMetadata(0, ParentLayout));

    /// <summary>First column in a grid band; position in the band in a scrolling or wrapping band.</summary>
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached(
        "Column", typeof(int), typeof(DashboardPanel), new FrameworkPropertyMetadata(0, ParentLayout));

    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached(
        "Row", typeof(int), typeof(DashboardPanel), new FrameworkPropertyMetadata(0, ParentLayout));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(DashboardPanel), new FrameworkPropertyMetadata(ItemLayout.DefaultColumnSpan, ParentLayout));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(DashboardPanel), new FrameworkPropertyMetadata(ItemLayout.DefaultRowSpan, ParentLayout));

    /// <summary>Width in scrolling and wrapping bands.</summary>
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.RegisterAttached(
        "ItemWidth", typeof(double), typeof(DashboardPanel), new FrameworkPropertyMetadata(ItemLayout.DefaultWidth, ParentLayout));

    public static readonly DependencyProperty IsFullRowProperty = DependencyProperty.RegisterAttached(
        "IsFullRow", typeof(bool), typeof(DashboardPanel), new FrameworkPropertyMetadata(false, ParentLayout));

    /// <summary>Raised (bubbling) after <see cref="DashboardLayout"/> changed the arrangement of the panel.</summary>
    public static readonly RoutedEvent ArrangementChangedEvent = EventManager.RegisterRoutedEvent(
        "ArrangementChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DashboardPanel));

    private readonly Dictionary<UIElement, Rect> _slots = [];
    private readonly List<Rect> _bandBounds = [];
    private double _lastWidth;
    private bool _arranging;

    public DashboardPanel()
    {
        Bands.CollectionChanged += OnBandsChanged;
        // Children of a scrolling band are clipped to the panel
        ClipToBounds = true;
    }

    public event RoutedEventHandler ArrangementChanged
    {
        add => AddHandler(ArrangementChangedEvent, value);
        remove => RemoveHandler(ArrangementChangedEvent, value);
    }

    /// <summary>Raised after each arrange pass, once item and band bounds are up to date (overlays follow the items with it).</summary>
    public event EventHandler? Arranged;

    /// <summary>Bands from top to bottom. Without any, the panel behaves as one <see cref="BandMode.Fit"/> band.</summary>
    public ObservableCollection<DashboardBand> Bands { get; } = [];

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>Space between items, in device-independent pixels.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double BandSpacing
    {
        get => (double)GetValue(BandSpacingProperty);
        set => SetValue(BandSpacingProperty, value);
    }

    public static int GetBand(UIElement e) => (int)e.GetValue(BandProperty);
    public static void SetBand(UIElement e, int value) => e.SetValue(BandProperty, value);
    public static int GetColumn(UIElement e) => (int)e.GetValue(ColumnProperty);
    public static void SetColumn(UIElement e, int value) => e.SetValue(ColumnProperty, value);
    public static int GetRow(UIElement e) => (int)e.GetValue(RowProperty);
    public static void SetRow(UIElement e, int value) => e.SetValue(RowProperty, value);
    public static int GetColumnSpan(UIElement e) => (int)e.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(UIElement e, int value) => e.SetValue(ColumnSpanProperty, value);
    public static int GetRowSpan(UIElement e) => (int)e.GetValue(RowSpanProperty);
    public static void SetRowSpan(UIElement e, int value) => e.SetValue(RowSpanProperty, value);
    public static double GetItemWidth(UIElement e) => (double)e.GetValue(ItemWidthProperty);
    public static void SetItemWidth(UIElement e, double value) => e.SetValue(ItemWidthProperty, value);
    public static bool GetIsFullRow(UIElement e) => (bool)e.GetValue(IsFullRowProperty);
    public static void SetIsFullRow(UIElement e, bool value) => e.SetValue(IsFullRowProperty, value);

    /// <summary>Number of bands the panel shows (at least one).</summary>
    public int BandCount => Math.Max(1, Bands.Count);

    /// <summary>Mode of a band; <see cref="BandMode.Fit"/> for the implicit band of a panel without <see cref="Bands"/>.</summary>
    public BandMode ModeOf(int band) => band >= 0 && band < Bands.Count ? Bands[band].Mode : BandMode.Fit;

    /// <summary>Band index used to place a child: its attached band, clamped to the existing bands.</summary>
    public int BandIndexOf(UIElement child) => Math.Clamp(GetBand(child), 0, BandCount - 1);

    /// <summary>Children of a band, in display order (by <c>Column</c> in scrolling and wrapping bands).</summary>
    public IReadOnlyList<UIElement> ChildrenOf(int band)
    {
        var children = InternalChildren.Cast<UIElement>().Where(c => BandIndexOf(c) == band);
        return ModeOf(band) == BandMode.Fit
            ? children.ToArray()
            : children.Select((c, i) => (Child: c, Index: i)).OrderBy(c => GetColumn(c.Child)).ThenBy(c => c.Index).Select(c => c.Child).ToArray();
    }

    /// <summary>Width of one grid column for the last arranged width.</summary>
    public double ColumnWidth => GridColumnWidth(_lastWidth);

    /// <summary>Distance between the starts of two neighbouring columns.</summary>
    public double ColumnPitch => ColumnWidth + Gap;

    /// <summary>Distance between the starts of two neighbouring rows.</summary>
    public double RowPitch => RowHeight + Gap;

    /// <summary>Height of an item covering <paramref name="rowSpan"/> rows.</summary>
    public double HeightOf(int rowSpan) => Math.Max(1, rowSpan) * RowHeight + (Math.Max(1, rowSpan) - 1) * Gap;

    /// <summary>Bounds of a band after the last arrange, in panel coordinates (excluding the spacing below it).</summary>
    public Rect GetBandBounds(int band) => band >= 0 && band < _bandBounds.Count ? _bandBounds[band] : Rect.Empty;

    /// <summary>Where a child was placed by the last arrange (scroll offset included), in panel coordinates.</summary>
    public Rect GetItemBounds(UIElement child) => _slots.TryGetValue(child, out var rect) ? rect : Rect.Empty;

    /// <summary>The band under a vertical position: the band whose bounds (with the spacing below) contain it, or the nearest.</summary>
    public int BandAt(double y)
    {
        for (var i = 0; i < _bandBounds.Count; i++)
            if (y < _bandBounds[i].Bottom + BandSpacing / 2) return i;
        return Math.Max(0, _bandBounds.Count - 1);
    }

    /// <summary>
    /// Grid cell whose top-left corner is nearest to <paramref name="topLeft"/> (panel coordinates) in a grid band,
    /// for an item covering <paramref name="columnSpan"/> columns.
    /// </summary>
    public (int Column, int Row) CellAt(int band, Point topLeft, int columnSpan)
    {
        var bounds = GetBandBounds(band);
        var top = bounds.IsEmpty ? 0 : bounds.Top;
        var span = Math.Clamp(columnSpan, 1, Columns);
        var column = Math.Clamp((int)Math.Round(topLeft.X / Math.Max(1, ColumnPitch)), 0, Columns - span);
        var row = Math.Max(0, (int)Math.Round((topLeft.Y - top) / RowPitch));
        return (column, row);
    }

    /// <summary>
    /// Position (in the band's current order) before which an item dropped at <paramref name="point"/> goes in a
    /// scrolling or wrapping band: before the first other item whose centre comes after the point, reading line by line.
    /// </summary>
    public int FlowIndexAt(int band, Point point, UIElement? excluding = null)
    {
        var bounds = ChildrenOf(band).Select(c => c == excluding ? Rect.Empty : GetItemBounds(c)).ToArray();
        return InsertionIndex.At(bounds, point, singleLine: ModeOf(band) == BandMode.Scroll);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? DashboardLayoutEngine.ReferenceWidth : availableSize.Width;
        var height = Layout(width, (child, rect) => child.Measure(rect.Size));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _lastWidth = finalSize.Width;
        _slots.Clear();
        _arranging = true;
        try
        {
            Layout(finalSize.Width, (child, rect) =>
            {
                _slots[child] = rect;
                child.Arrange(rect);
            }, _bandBounds);
        }
        finally
        {
            _arranging = false;
        }
        Arranged?.Invoke(this, EventArgs.Empty);
        return finalSize;
    }

    /// <summary>Places every child for a given width; returns the total height.</summary>
    private double Layout(double width, Action<UIElement, Rect> place, List<Rect>? bandBounds = null)
    {
        bandBounds?.Clear();
        var top = 0d;
        for (var band = 0; band < BandCount; band++)
        {
            if (band > 0) top += BandSpacing;
            var children = ChildrenOf(band);
            var height = ModeOf(band) switch
            {
                BandMode.Scroll => LayoutScroll(band, children, width, top, place),
                BandMode.Wrap => LayoutWrap(children, width, top, place),
                _ => LayoutGrid(children, width, top, place),
            };
            // An empty band keeps one row, so that it can still be seen and receive items
            height = children.Count == 0 ? RowHeight : height;
            bandBounds?.Add(new Rect(0, top, width, height));
            top += height;
        }
        return top;
    }

    private double GridColumnWidth(double width) => Math.Max(0, (width - Gap * (Columns - 1)) / Columns);

    private double LayoutGrid(IReadOnlyList<UIElement> children, double width, double top, Action<UIElement, Rect> place)
    {
        var columnWidth = GridColumnWidth(width);
        var bottom = 0d;
        foreach (var child in children)
        {
            var full = GetIsFullRow(child);
            var span = full ? Columns : Math.Clamp(GetColumnSpan(child), 1, Columns);
            var column = full ? 0 : Math.Clamp(GetColumn(child), 0, Columns - span);
            var rect = new Rect(
                column * (columnWidth + Gap),
                top + Math.Max(0, GetRow(child)) * RowPitch,
                span * columnWidth + (span - 1) * Gap,
                HeightOf(GetRowSpan(child)));
            SetClip(child, null);
            place(child, rect);
            bottom = Math.Max(bottom, rect.Bottom - top);
        }
        return bottom;
    }

    private double LayoutScroll(int band, IReadOnlyList<UIElement> children, double width, double top, Action<UIElement, Rect> place)
    {
        var definition = band < Bands.Count ? Bands[band] : null;
        var extent = children.Sum(c => ItemWidthOf(c, width)) + Gap * Math.Max(0, children.Count - 1);
        var offset = Math.Clamp(definition?.HorizontalOffset ?? 0, 0, Math.Max(0, extent - width));
        // Only the arrange pass publishes the scroll state (measure may run with another width)
        if (definition is not null && _arranging)
        {
            definition.ExtentWidth = extent;
            definition.ViewportWidth = width;
            definition.HorizontalOffset = offset;
        }

        double x = -offset, height = 0;
        foreach (var child in children)
        {
            var rect = new Rect(x, top, ItemWidthOf(child, width), HeightOf(GetRowSpan(child)));
            // Items partly outside the band are clipped at its edges
            SetClip(child, rect.Left < 0 || rect.Right > width
                ? new RectangleGeometry(new Rect(-rect.Left, 0, width, rect.Height))
                : null);
            place(child, rect);
            x += rect.Width + Gap;
            height = Math.Max(height, rect.Height);
        }
        return height;
    }

    private double LayoutWrap(IReadOnlyList<UIElement> children, double width, double top, Action<UIElement, Rect> place)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in children)
        {
            var itemWidth = ItemWidthOf(child, width);
            if (x > 0 && (x + itemWidth > width || GetIsFullRow(child)))
            {
                x = 0;
                y += lineHeight + Gap;
                lineHeight = 0;
            }
            var rect = new Rect(x, top + y, itemWidth, HeightOf(GetRowSpan(child)));
            SetClip(child, null);
            place(child, rect);
            lineHeight = Math.Max(lineHeight, rect.Height);
            x = GetIsFullRow(child) ? width : x + itemWidth + Gap;
        }
        return y + lineHeight;
    }

    private static double ItemWidthOf(UIElement child, double bandWidth) =>
        GetIsFullRow(child) ? bandWidth : Math.Min(Math.Max(1, GetItemWidth(child)), Math.Max(1, bandWidth));

    // The panel only touches the clip of children of scrolling bands, and only when it set it itself
    private static readonly DependencyProperty OwnsClipProperty = DependencyProperty.RegisterAttached(
        "OwnsClip", typeof(bool), typeof(DashboardPanel), new PropertyMetadata(false));

    private static void SetClip(UIElement child, Geometry? clip)
    {
        if (clip is not null)
        {
            child.Clip = clip;
            child.SetValue(OwnsClipProperty, true);
        }
        else if ((bool)child.GetValue(OwnsClipProperty))
        {
            child.ClearValue(UIElement.ClipProperty);
            child.ClearValue(OwnsClipProperty);
        }
    }

    private void OnBandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var band in e.OldItems?.OfType<DashboardBand>() ?? []) band.PropertyChanged -= OnBandChanged;
        foreach (var band in e.NewItems?.OfType<DashboardBand>() ?? []) band.PropertyChanged += OnBandChanged;
        InvalidateMeasure();
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The panel itself writes the extent and viewport during arrange: only the inputs relayout
        if (e.PropertyName is nameof(DashboardBand.Mode) or nameof(DashboardBand.HorizontalOffset)) InvalidateMeasure();
    }
}
