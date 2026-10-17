using System.Windows;
using System.Windows.Media;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Moves the children of a <see cref="DashboardPanel"/> while keeping its layout consistent (no overlap in grid
/// bands, full rows, empty bands removed...). Each method reads the panel's attached properties into a
/// <see cref="DashboardLayoutDefinition"/>, runs the matching <see cref="DashboardLayoutEngine"/> operation and
/// writes the result back, then raises <see cref="DashboardPanel.ArrangementChangedEvent"/>.
/// <see cref="Update"/> runs any custom operation the same way.
/// </summary>
/// <remarks>
/// Methods that add or remove children need a panel whose <c>Children</c> can be modified (not the items host of
/// an <c>ItemsControl</c>).
/// </remarks>
public static class DashboardLayout
{
    /// <summary>The panel hosting an item, or null when the item is not in a <see cref="DashboardPanel"/>.</summary>
    public static DashboardPanel? PanelOf(UIElement item) => VisualTreeHelper.GetParent(item) as DashboardPanel;

    // --- Reading and writing a whole layout ---

    /// <summary>
    /// The current layout of a panel, ready to be saved. <paramref name="visualIdOf"/> gives the id of the visual an
    /// item shows (null for an empty item); every item is empty when it is not given.
    /// </summary>
    public static DashboardLayoutDefinition GetLayout(DashboardPanel panel, Func<UIElement, string?>? visualIdOf = null)
    {
        var session = Read(panel);
        return session.Layout with
        {
            Bands = session.Layout.Bands.Select(b => b with
            {
                Items = b.Items.Select(i => i with { VisualId = visualIdOf?.Invoke(session.Elements[i.Id]), Id = Guid.NewGuid().ToString("N") }).ToArray(),
            }).ToArray(),
        };
    }

    /// <summary>
    /// Shows a layout: <paramref name="elementFor"/> gives the element of each item (an existing child is kept,
    /// so it does not render again); children it does not return are removed.
    /// </summary>
    public static void SetLayout(DashboardPanel panel, DashboardLayoutDefinition layout, Func<ItemLayout, UIElement> elementFor)
    {
        layout = DashboardLayoutEngine.Normalize(layout);
        var elements = new Dictionary<string, UIElement>();
        foreach (var item in layout.Bands.SelectMany(b => b.Items))
            elements[item.Id] = elementFor(item);

        panel.Columns = layout.Columns;
        panel.RowHeight = layout.RowHeight;
        var bands = layout.Bands.Select((b, i) => i < panel.Bands.Count ? panel.Bands[i] : new DashboardBand()).ToArray();
        Write(panel, layout, bands.Select((band, i) => (layout.Bands[i].Id, band)).ToDictionary(), elements, removeOthers: true);
    }

    /// <summary>Remembers where every child is, to put them back with <see cref="DashboardLayoutSnapshot.Restore"/>.</summary>
    public static DashboardLayoutSnapshot Capture(DashboardPanel panel) => new(panel);

    // --- Operations on items ---

    /// <summary>
    /// Moves an item to a cell of a grid band; the items it lands on move down. <paramref name="keepEmptyBands"/>
    /// keeps a band the item leaves empty (while dragging, so that bands do not shift).
    /// </summary>
    public static void Move(UIElement item, int band, int column, int row, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.Move(layout, id, band, column, row), keepEmptyBands);

    /// <summary>Moves an item to position <paramref name="index"/> of a scrolling or wrapping band (index in the band before the move).</summary>
    public static void MoveTo(UIElement item, int band, int index, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.MoveTo(layout, id, band, index), keepEmptyBands);

    /// <summary>
    /// Moves an item next to <paramref name="anchor"/> (see <see cref="DashboardLayoutEngine.MoveNextTo"/>): in a grid
    /// band, between two items of a line, which make room. <paramref name="keepEmptyBands"/> as for <see cref="Move"/>.
    /// </summary>
    public static void MoveNextTo(UIElement item, UIElement anchor, ItemSide side, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id, idOf) => DashboardLayoutEngine.MoveNextTo(layout, id, idOf(anchor), side), keepEmptyBands);

    /// <summary>Changes the columns and rows an item covers in a grid band.</summary>
    public static void Resize(UIElement item, int columnSpan, int rowSpan) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.Resize(layout, id, columnSpan, rowSpan));

    /// <summary>Changes the width and height (in rows) of an item in a scrolling or wrapping band.</summary>
    public static void ResizeWidth(UIElement item, double width, int rowSpan) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.ResizeWidth(layout, id, width, rowSpan));

    public static void SetFullRow(UIElement item, bool isFullRow) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.SetFullRow(layout, id, isFullRow));

    /// <summary>Adds a child at the end of a band (a new band past the last one).</summary>
    public static void Add(DashboardPanel panel, UIElement item, int band) =>
        Update(panel, (layout, idOf) => DashboardLayoutEngine.Add(layout, band, NewItem(idOf(item))), item);

    /// <summary>Adds a child to the right of (or below) <paramref name="anchor"/>.</summary>
    public static void AddNextTo(UIElement anchor, UIElement item, ItemSide side) =>
        Update(RequirePanel(anchor), (layout, idOf) => DashboardLayoutEngine.AddNextTo(layout, idOf(anchor), side, NewItem(idOf(item))), item);

    /// <summary>Removes the full row of <paramref name="anchor"/> and adds a child next to it.</summary>
    public static void SplitFullRow(UIElement anchor, UIElement item) =>
        Update(RequirePanel(anchor), (layout, idOf) => DashboardLayoutEngine.SplitFullRow(layout, idOf(anchor), NewItem(idOf(item))), item);

    /// <summary>Removes a child; its band disappears when it was the last one.</summary>
    public static void Remove(UIElement item) => UpdateItem(item, DashboardLayoutEngine.Remove);

    /// <summary>Re-applies the layout rules (after attached properties were set by hand).</summary>
    public static void Normalize(DashboardPanel panel) => Update(panel, (layout, _) => DashboardLayoutEngine.Normalize(layout));

    // --- Operations on bands ---

    /// <summary>Inserts a band at <paramref name="index"/>, holding <paramref name="items"/> (new children).</summary>
    public static void InsertBand(DashboardPanel panel, int index, BandMode mode, params UIElement[] items) =>
        Update(panel, (layout, idOf) => DashboardLayoutEngine.InsertBand(layout, index, mode, items.Select(i => NewItem(idOf(i))).ToArray()), items);

    /// <summary>Removes a band and its children.</summary>
    public static void RemoveBand(DashboardPanel panel, int index) =>
        Update(panel, (layout, _) => DashboardLayoutEngine.RemoveBand(layout, index));

    /// <summary>Changes the mode of a band; items keep their size on screen.</summary>
    public static void SetBandMode(DashboardPanel panel, int index, BandMode mode) =>
        Update(panel, (layout, _) => DashboardLayoutEngine.SetBandMode(layout, index, mode, panel.ActualWidth, panel.Gap));

    /// <summary>Removes the bands left without items (after a drag that kept them).</summary>
    public static void RemoveEmptyBands(DashboardPanel panel) =>
        Update(panel, (layout, _) => layout with { Bands = layout.Bands.Where(b => b.Items.Count > 0).ToArray() });

    /// <summary>
    /// Runs a custom operation: <paramref name="operation"/> receives the current layout and a function giving the
    /// item id of a child (or of one of <paramref name="newItems"/>, which are added to the panel if the result places them).
    /// </summary>
    public static void Update(
        DashboardPanel panel,
        Func<DashboardLayoutDefinition, Func<UIElement, string>, DashboardLayoutDefinition> operation,
        params UIElement[] newItems)
    {
        var session = Read(panel);
        var elements = new Dictionary<string, UIElement>(session.Elements);
        var ids = elements.ToDictionary(e => e.Value, e => e.Key);
        foreach (var item in newItems)
        {
            if (ids.ContainsKey(item)) continue;
            var id = "new" + ids.Count;
            ids[item] = id;
            elements[id] = item;
        }

        var result = operation(session.Layout, element => ids.TryGetValue(element, out var id) ? id : "");
        Write(panel, result, session.Bands, elements, removeOthers: true);
    }

    private static void UpdateItem(UIElement item, Func<DashboardLayoutDefinition, string, DashboardLayoutDefinition> operation, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id, _) => operation(layout, id), keepEmptyBands);

    private static void UpdateItem(
        UIElement item, Func<DashboardLayoutDefinition, string, Func<UIElement, string>, DashboardLayoutDefinition> operation, bool keepEmptyBands)
    {
        var panel = RequirePanel(item);
        Update(panel, (layout, idOf) =>
        {
            var result = operation(layout, idOf(item), idOf);
            if (!keepEmptyBands) return result;
            // The engine drops a band the item left empty: put it back where it was
            var kept = layout.Bands.Select(b => result.Bands.FirstOrDefault(r => r.Id == b.Id) ?? b with { Items = [] })
                .Concat(result.Bands.Where(r => layout.Bands.All(b => b.Id != r.Id)));
            return result with { Bands = kept.ToArray() };
        });
    }

    private static DashboardPanel RequirePanel(UIElement item) =>
        PanelOf(item) ?? throw new InvalidOperationException("The element is not a child of a DashboardPanel.");

    private static ItemLayout NewItem(string id) => new() { Id = id };

    // --- Panel <-> definition ---

    private sealed record Session(
        DashboardLayoutDefinition Layout,
        IReadOnlyDictionary<string, UIElement> Elements,
        IReadOnlyDictionary<string, DashboardBand> Bands);

    private static Session Read(DashboardPanel panel)
    {
        var children = panel.Children.Cast<UIElement>().ToList();
        var elements = new Dictionary<string, UIElement>();
        var bands = new Dictionary<string, DashboardBand>();
        var layoutBands = new List<BandLayout>();
        for (var b = 0; b < panel.BandCount; b++)
        {
            var mode = panel.ModeOf(b);
            var items = panel.ChildrenOf(b).Select((child, order) =>
            {
                var id = "c" + children.IndexOf(child);
                elements[id] = child;
                return new ItemLayout
                {
                    Id = id,
                    Column = mode == BandMode.Fit ? DashboardPanel.GetColumn(child) : order,
                    Row = mode == BandMode.Fit ? DashboardPanel.GetRow(child) : 0,
                    ColumnSpan = DashboardPanel.GetColumnSpan(child),
                    RowSpan = DashboardPanel.GetRowSpan(child),
                    Width = DashboardPanel.GetItemWidth(child),
                    IsFullRow = DashboardPanel.GetIsFullRow(child),
                };
            }).ToArray();
            var bandId = "b" + b;
            // A panel without bands has one implicit band, made real when the layout is written back
            bands[bandId] = b < panel.Bands.Count ? panel.Bands[b] : new DashboardBand();
            layoutBands.Add(new BandLayout { Id = bandId, Mode = mode, Items = items });
        }
        var layout = new DashboardLayoutDefinition { Columns = panel.Columns, RowHeight = panel.RowHeight, Bands = layoutBands };
        return new Session(layout, elements, bands);
    }

    private static void Write(
        DashboardPanel panel,
        DashboardLayoutDefinition layout,
        IReadOnlyDictionary<string, DashboardBand> knownBands,
        IReadOnlyDictionary<string, UIElement> elements,
        bool removeOthers)
    {
        // Bands keep their identity (and scroll position) when they move
        var bands = layout.Bands.Select(b => knownBands.TryGetValue(b.Id, out var band) ? band : new DashboardBand()).ToList();
        for (var i = 0; i < bands.Count; i++) bands[i].Mode = layout.Bands[i].Mode;
        Reconcile(panel.Bands, bands);

        var placed = new HashSet<UIElement>();
        for (var b = 0; b < layout.Bands.Count; b++)
        {
            var mode = layout.Bands[b].Mode;
            var items = layout.Bands[b].Items;
            for (var order = 0; order < items.Count; order++)
            {
                var item = items[order];
                if (!elements.TryGetValue(item.Id, out var element)) continue;
                placed.Add(element);
                DashboardPanel.SetBand(element, b);
                DashboardPanel.SetColumn(element, mode == BandMode.Fit ? item.Column : order);
                DashboardPanel.SetRow(element, mode == BandMode.Fit ? item.Row : 0);
                DashboardPanel.SetColumnSpan(element, item.ColumnSpan);
                DashboardPanel.SetRowSpan(element, item.RowSpan);
                DashboardPanel.SetItemWidth(element, item.Width);
                DashboardPanel.SetIsFullRow(element, item.IsFullRow);
                if (!panel.Children.Contains(element)) panel.Children.Add(element);
            }
        }
        if (removeOthers)
        {
            foreach (var child in panel.Children.Cast<UIElement>().Where(c => !placed.Contains(c)).ToList())
                panel.Children.Remove(child);
        }
        panel.RaiseEvent(new RoutedEventArgs(DashboardPanel.ArrangementChangedEvent, panel));
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="items"/> with minimal moves, keeping instances.</summary>
    internal static void Reconcile<T>(IList<T> target, IReadOnlyList<T> items) where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!items.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            var current = target.IndexOf(items[i]);
            if (current == i) continue;
            if (current >= 0) target.RemoveAt(current);
            target.Insert(i, items[i]);
        }
    }
}

/// <summary>Positions and bands of a <see cref="DashboardPanel"/> at one moment (see <see cref="DashboardLayout.Capture"/>).</summary>
public sealed class DashboardLayoutSnapshot
{
    private readonly DashboardPanel _panel;
    private readonly IReadOnlyList<(DashboardBand Band, BandMode Mode, double Offset)> _bands;
    private readonly IReadOnlyList<(UIElement Element, object[] Values)> _items;

    private static readonly DependencyProperty[] Properties =
    [
        DashboardPanel.BandProperty, DashboardPanel.ColumnProperty, DashboardPanel.RowProperty, DashboardPanel.ColumnSpanProperty,
        DashboardPanel.RowSpanProperty, DashboardPanel.ItemWidthProperty, DashboardPanel.IsFullRowProperty,
    ];

    internal DashboardLayoutSnapshot(DashboardPanel panel)
    {
        _panel = panel;
        _bands = panel.Bands.Select(b => (b, b.Mode, b.HorizontalOffset)).ToArray();
        _items = panel.Children.Cast<UIElement>().Select(c => (c, Properties.Select(c.GetValue).ToArray())).ToArray();
    }

    /// <summary>Puts every captured child and band back. Children added since stay where they are.</summary>
    public void Restore()
    {
        foreach (var (band, mode, offset) in _bands)
        {
            band.Mode = mode;
            band.HorizontalOffset = offset;
        }
        DashboardLayout.Reconcile(_panel.Bands, _bands.Select(b => b.Band).ToArray());
        foreach (var (element, values) in _items)
        {
            for (var i = 0; i < Properties.Length; i++) element.SetValue(Properties[i], values[i]);
            if (!_panel.Children.Contains(element)) _panel.Children.Add(element);
        }
        _panel.RaiseEvent(new RoutedEventArgs(DashboardPanel.ArrangementChangedEvent, _panel));
    }
}
