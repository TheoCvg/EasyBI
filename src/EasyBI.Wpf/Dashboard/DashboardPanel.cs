using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Room left in a container of a <see cref="DashboardPanel"/>, in panel coordinates (see <see cref="FreeSpace"/>).</summary>
public sealed record DashboardFreeSpace(int Band, Rect Bounds, string AnchorId, ItemSide Side);

/// <summary>
/// Lays out a dashboard from its <see cref="Layout"/>: a stack of bands (<see cref="BandMode"/>), each holding a tree
/// of items (see <see cref="BandArrangement"/> for the rules). Children are matched to the layout's items by the
/// attached <see cref="ItemIdProperty"/>; children without an item are not shown. <see cref="DashboardLayout"/> changes
/// the layout while keeping it consistent.
/// </summary>
public class DashboardPanel : Panel
{
    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
        nameof(Layout), typeof(DashboardLayoutDefinition), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(new DashboardLayoutDefinition(), FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, _) => ((DashboardPanel)d).OnLayoutChanged()));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(DashboardPanel), new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Vertical space between two bands.</summary>
    public static readonly DependencyProperty BandSpacingProperty = DependencyProperty.Register(
        nameof(BandSpacing), typeof(double), typeof(DashboardPanel), new FrameworkPropertyMetadata(24d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Width of unconstrained items in scrolling and wrapping bands before they grow to fill the line.</summary>
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(280d, FrameworkPropertyMetadataOptions.AffectsMeasure), v => (double)v >= 1);

    /// <summary>
    /// Column tiers of a narrow panel (see <see cref="ColumnTier"/>); null uses <see cref="ColumnTier.Defaults"/>, an
    /// empty list always shows the layout's columns.
    /// </summary>
    public static readonly DependencyProperty ColumnTiersProperty = DependencyProperty.Register(
        nameof(ColumnTiers), typeof(IEnumerable<ColumnTier>), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The layout item (<see cref="LayoutNode.Id"/>) a child shows.</summary>
    public static readonly DependencyProperty ItemIdProperty = DependencyProperty.RegisterAttached(
        "ItemId", typeof(string), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Raised (bubbling) after <see cref="DashboardLayout"/> changed the arrangement of the panel.</summary>
    public static readonly RoutedEvent ArrangementChangedEvent = EventManager.RegisterRoutedEvent(
        "ArrangementChanged", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DashboardPanel));

    private readonly Dictionary<string, DashboardBand> _bandsById = [];
    private readonly Dictionary<UIElement, Rect> _slots = [];
    private readonly List<Rect> _bandBounds = [];
    private readonly List<IReadOnlyList<Rect>> _lineBounds = [];
    private readonly List<DashboardFreeSpace> _free = [];
    private readonly Dictionary<string, (int Start, int Span)> _columns = [];
    private Dictionary<string, int> _bandOfNode = [];
    private double _lastWidth;
    private bool _arranging;

    public DashboardPanel()
    {
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

    /// <summary>The arrangement shown. Change it through <see cref="DashboardLayout"/> to keep it consistent.</summary>
    public DashboardLayoutDefinition Layout
    {
        get => (DashboardLayoutDefinition)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Live state of the bands (scroll positions), one per band of the layout.</summary>
    public ObservableCollection<DashboardBand> Bands { get; } = [];

    /// <summary>Columns of the layout: the widths of grid items are counted in them.</summary>
    public int Columns => Math.Max(1, Layout.Columns);

    public IEnumerable<ColumnTier>? ColumnTiers
    {
        get => (IEnumerable<ColumnTier>?)GetValue(ColumnTiersProperty);
        set => SetValue(ColumnTiersProperty, value);
    }

    /// <summary>Columns grid bands show at the last arranged width: fewer than <see cref="Columns"/> in a column tier.</summary>
    public int ActiveColumns => ColumnsAt(_lastWidth);

    /// <summary>A width in columns shown (<see cref="ActiveColumns"/>) as a width in columns of the layout.</summary>
    public int ToLayoutSpan(int activeSpan) =>
        ActiveColumns == Columns ? activeSpan : Math.Clamp((int)Math.Round(activeSpan * (double)Columns / ActiveColumns), 1, Columns);

    private int ColumnsAt(double width) => ColumnTier.ColumnsFor(ColumnTiers ?? ColumnTier.Defaults, width, Columns);

    public double RowHeight => Layout.RowHeight;

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

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public static string? GetItemId(UIElement element) => (string?)element.GetValue(ItemIdProperty);
    public static void SetItemId(UIElement element, string? value) => element.SetValue(ItemIdProperty, value);

    /// <summary>Number of bands the panel shows (at least one).</summary>
    public int BandCount => Math.Max(1, Layout.Bands.Count);

    /// <summary>Mode of a band; <see cref="BandMode.Fit"/> for the implicit band of an empty layout.</summary>
    public BandMode ModeOf(int band) => band >= 0 && band < Layout.Bands.Count ? Layout.Bands[band].Mode : BandMode.Fit;

    /// <summary>The child showing a layout item, if any.</summary>
    public UIElement? ElementOf(string itemId) => InternalChildren.Cast<UIElement>().FirstOrDefault(c => GetItemId(c) == itemId);

    /// <summary>Band of a child, or -1 when the layout does not place it.</summary>
    public int BandIndexOf(UIElement child) => GetItemId(child) is { } id && _bandOfNode.TryGetValue(id, out var band) ? band : -1;

    /// <summary>Children of a band, in reading order.</summary>
    public IReadOnlyList<UIElement> ChildrenOf(int band)
    {
        if (band < 0 || band >= Layout.Bands.Count) return [];
        var elements = new Dictionary<string, UIElement>();
        foreach (UIElement child in InternalChildren)
            if (GetItemId(child) is { } id) elements[id] = child;
        return Layout.Bands[band].Items.SelectMany(i => i.Items()).Select(i => elements.GetValueOrDefault(i.Id)).OfType<UIElement>().ToArray();
    }

    /// <summary>Width of one grid column shown, for the last arranged width.</summary>
    public double ColumnWidth => Settings(_lastWidth, 0).ColumnWidth;

    /// <summary>Distance between the starts of two neighbouring columns.</summary>
    public double ColumnPitch => ColumnWidth + Gap;

    /// <summary>Distance between the starts of two neighbouring rows.</summary>
    public double RowPitch => RowHeight + Gap;

    /// <summary>Height of <paramref name="rows"/> rows, gaps between them included.</summary>
    public double HeightOf(int rows) => Settings(_lastWidth, 0).HeightOf(rows);

    /// <summary>Bounds of a band after the last arrange, in panel coordinates (excluding the spacing below it).</summary>
    public Rect GetBandBounds(int band) => band >= 0 && band < _bandBounds.Count ? _bandBounds[band] : Rect.Empty;

    /// <summary>Bounds of the lines of a band (grid lines, or lines of a wrapping band), in panel coordinates.</summary>
    public IReadOnlyList<Rect> GetLineBounds(int band) => band >= 0 && band < _lineBounds.Count ? _lineBounds[band] : [];

    /// <summary>Where a child was placed by the last arrange (scroll offset included), in panel coordinates.</summary>
    public Rect GetItemBounds(UIElement child) => _slots.TryGetValue(child, out var rect) ? rect : Rect.Empty;

    /// <summary>The grid columns a layout node of a grid band covers, after the last arrange.</summary>
    public (int Start, int Span)? GetColumns(string nodeId) => _columns.TryGetValue(nodeId, out var columns) ? columns : null;

    /// <summary>Room left in containers whose children are all constrained, after the last arrange.</summary>
    public IReadOnlyList<DashboardFreeSpace> FreeSpaces => _free;

    /// <summary>
    /// The current geometry (bands, lines, items and free spaces as arranged last). Capture it before a drag to hit-test
    /// against the layout from before the drag (see <see cref="DashboardPanelGeometry"/>).
    /// </summary>
    public DashboardPanelGeometry CaptureGeometry() => new(this, _bandBounds, _lineBounds, _free, GetItemBounds);

    /// <summary>The band under a vertical position: the band whose bounds (with the spacing below) contain it, or the nearest.</summary>
    public int BandAt(double y) => CaptureGeometry().BandAt(y);

    /// <inheritdoc cref="DashboardPanelGeometry.FlowIndexAt"/>
    public int FlowIndexAt(int band, Point point, UIElement? excluding = null) => CaptureGeometry().FlowIndexAt(band, point, excluding);

    /// <inheritdoc cref="DashboardPanelGeometry.EdgeAt"/>
    public (UIElement Anchor, ItemSide Side)? EdgeAt(Point point, double edgeZone, UIElement? excluding = null) =>
        CaptureGeometry().EdgeAt(point, edgeZone, excluding);

    /// <summary>
    /// The side of <paramref name="bounds"/> whose edge is within <paramref name="edgeZone"/> of <paramref name="point"/>
    /// (at most a quarter of the item, so that its middle stays free), the nearest one in a corner; null in the middle.
    /// A point outside the bounds is beyond the edge it crossed.
    /// </summary>
    public static ItemSide? SideAt(Rect bounds, Point point, double edgeZone)
    {
        var zoneX = Math.Min(edgeZone, bounds.Width / 4);
        var zoneY = Math.Min(edgeZone, bounds.Height / 4);
        (ItemSide Side, double Distance, double Zone)[] edges =
        [
            (ItemSide.Left, point.X - bounds.Left, zoneX),
            (ItemSide.Right, bounds.Right - point.X, zoneX),
            (ItemSide.Above, point.Y - bounds.Top, zoneY),
            (ItemSide.Below, bounds.Bottom - point.Y, zoneY),
        ];
        var near = edges.Where(e => e.Distance <= e.Zone).OrderBy(e => e.Distance).ToArray();
        return near.Length > 0 ? near[0].Side : null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? DashboardLayoutEngine.ReferenceWidth : availableSize.Width;
        var height = LayoutBands(width, (child, rect) => child.Measure(rect.Size));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _lastWidth = finalSize.Width;
        _slots.Clear();
        _bandBounds.Clear();
        _free.Clear();
        _lineBounds.Clear();
        _columns.Clear();
        _arranging = true;
        try
        {
            LayoutBands(finalSize.Width, (child, rect) =>
            {
                _slots[child] = rect;
                child.Arrange(rect);
            });
        }
        finally
        {
            _arranging = false;
        }
        // Children the layout does not place are not shown
        foreach (UIElement child in InternalChildren)
            if (!_slots.ContainsKey(child)) child.Arrange(new Rect(0, 0, 0, 0));
        Arranged?.Invoke(this, EventArgs.Empty);
        return finalSize;
    }

    private ArrangeSettings Settings(double width, double offset)
    {
        var columns = ColumnsAt(width);
        return new(Math.Max(0, width), columns, RowHeight, Gap, MinItemWidth, offset, columns < Columns ? Columns : null);
    }

    /// <summary>Places every child for a given width; returns the total height. The arrange pass also records the bounds.</summary>
    private double LayoutBands(double width, Action<UIElement, Rect> place)
    {
        var elements = new Dictionary<string, UIElement>();
        foreach (UIElement child in InternalChildren)
            if (GetItemId(child) is { } id) elements[id] = child;

        var bands = Layout.Bands.Count > 0 ? Layout.Bands : [new BandLayout()];
        var top = 0d;
        for (var b = 0; b < bands.Count; b++)
        {
            if (b > 0) top += BandSpacing;
            var state = b < Bands.Count ? Bands[b] : null;
            var arrangement = BandArrangement.Arrange(bands[b], Settings(width, state?.HorizontalOffset ?? 0));
            var scrolls = bands[b].Mode == BandMode.Scroll && arrangement.ExtentWidth > width + 0.5;
            foreach (var (id, rect) in arrangement.Items)
            {
                if (!elements.TryGetValue(id, out var child)) continue;
                var bounds = new Rect(rect.X, rect.Y + top, rect.Width, rect.Height);
                // Items partly outside a scrolling band are clipped at its edges
                SetClip(child, scrolls && (bounds.Left < 0 || bounds.Right > width)
                    ? new RectangleGeometry(new Rect(-bounds.Left, 0, width, bounds.Height))
                    : null);
                place(child, bounds);
            }
            if (_arranging)
            {
                // Only the arrange pass publishes the scroll state (measure may run with another width)
                if (state is not null)
                {
                    state.ExtentWidth = arrangement.ExtentWidth;
                    state.ViewportWidth = width;
                    state.HorizontalOffset = Math.Clamp(state.HorizontalOffset, 0, Math.Max(0, arrangement.ExtentWidth - width));
                }
                _bandBounds.Add(new Rect(0, top, width, arrangement.Height));
                _lineBounds.Add(arrangement.Lines.Select(l => new Rect(l.X, l.Y + top, l.Width, l.Height)).ToArray());
                foreach (var space in arrangement.FreeSpaces)
                    _free.Add(new DashboardFreeSpace(b, new Rect(space.Bounds.X, space.Bounds.Y + top, space.Bounds.Width, space.Bounds.Height), space.AnchorId, space.Side));
                foreach (var (id, columns) in arrangement.Columns) _columns[id] = columns;
            }
            top += arrangement.Height;
        }
        return top;
    }

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

    private void OnLayoutChanged()
    {
        var layout = Layout;
        _bandOfNode = layout.Bands.SelectMany((b, i) => b.Items.SelectMany(n => n.Descendants()).Select(n => (n.Id, Band: i)))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Band);
        // Band states keep their identity (and scroll position) when bands move
        var bands = layout.Bands.Select(b =>
        {
            if (!_bandsById.TryGetValue(b.Id, out var band))
            {
                band = new DashboardBand();
                band.PropertyChanged += OnBandChanged;
                _bandsById[b.Id] = band;
            }
            band.Mode = b.Mode;
            return band;
        }).ToArray();
        foreach (var gone in _bandsById.Where(p => layout.Bands.All(b => b.Id != p.Key)).ToArray())
        {
            gone.Value.PropertyChanged -= OnBandChanged;
            _bandsById.Remove(gone.Key);
        }
        DashboardLayout.Reconcile(Bands, bands);
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The panel itself writes the extent and viewport during arrange: only the scroll position relayouts
        if (e.PropertyName == nameof(DashboardBand.HorizontalOffset)) InvalidateMeasure();
    }
}
