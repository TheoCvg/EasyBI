using System.Windows;
using System.Windows.Media;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Changes the <see cref="DashboardPanel.Layout"/> of a panel while keeping it consistent: each method runs the
/// matching <see cref="DashboardLayoutEngine"/> operation, adds or removes the children it places or drops, then raises
/// <see cref="DashboardPanel.ArrangementChangedEvent"/>. <see cref="Update"/> runs any custom operation the same way.
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
    /// item shows (null for an empty item); items keep the visual of the layout when it is not given.
    /// </summary>
    public static DashboardLayoutDefinition GetLayout(DashboardPanel panel, Func<UIElement, string?>? visualIdOf = null)
    {
        if (visualIdOf is null) return panel.Layout;
        LayoutNode Visual(LayoutNode node) => node.IsItem
            ? node with { VisualId = panel.ElementOf(node.Id) is { } element ? visualIdOf(element) : null }
            : node with { Children = node.Children.Select(Visual).ToArray() };
        return panel.Layout with { Bands = panel.Layout.Bands.Select(b => b with { Items = b.Items.Select(Visual).ToArray() }).ToArray() };
    }

    /// <summary>
    /// Shows a layout: <paramref name="elementFor"/> gives the element of each item (an existing child is kept,
    /// so it does not render again); children it does not return are removed.
    /// </summary>
    public static void SetLayout(DashboardPanel panel, DashboardLayoutDefinition layout, Func<LayoutNode, UIElement> elementFor)
    {
        layout = DashboardLayoutEngine.Normalize(layout);
        var elements = layout.Bands.SelectMany(b => b.Items).SelectMany(n => n.Items()).ToDictionary(i => i.Id, elementFor);
        Write(panel, layout, elements);
    }

    /// <summary>Remembers the layout and the children, to put them back with <see cref="DashboardLayoutSnapshot.Restore"/>.</summary>
    public static DashboardLayoutSnapshot Capture(DashboardPanel panel) => new(panel);

    // --- Operations on items ---

    /// <summary>Adds a child next to <paramref name="anchor"/> (see <see cref="DashboardLayoutEngine.AddNextTo"/>).</summary>
    public static void AddNextTo(UIElement anchor, UIElement item, ItemSide side) =>
        AddNextTo(RequirePanel(anchor), RequireId(anchor), item, side);

    /// <summary>Adds a child next to a layout node: an item or a container (the anchor of a <see cref="DashboardFreeSpace"/>).</summary>
    public static void AddNextTo(DashboardPanel panel, string anchorId, UIElement item, ItemSide side) =>
        Update(panel, (layout, idOf) => DashboardLayoutEngine.AddNextTo(layout, anchorId, side, LayoutNode.Item() with { Id = idOf(item) }), item);

    /// <summary>Adds a child at the end of a band (a new line of a grid band; a new band past the last one).</summary>
    public static void Add(DashboardPanel panel, UIElement item, int band) => InsertAt(panel, item, band, int.MaxValue);

    /// <summary>Inserts a child at position <paramref name="index"/> of a band (see <see cref="DashboardLayoutEngine.InsertAt"/>).</summary>
    public static void InsertAt(DashboardPanel panel, UIElement item, int band, int index) =>
        Update(panel, (layout, idOf) => DashboardLayoutEngine.InsertAt(layout, band, index, LayoutNode.Item() with { Id = idOf(item) }), item);

    /// <summary>
    /// Moves a child next to <paramref name="anchor"/> (see <see cref="DashboardLayoutEngine.MoveNextTo"/>).
    /// <paramref name="keepEmptyBands"/> keeps a band the child leaves empty (while dragging, so that bands do not shift).
    /// </summary>
    public static void MoveNextTo(UIElement item, UIElement anchor, ItemSide side, bool keepEmptyBands = false) =>
        MoveNextTo(item, RequireId(anchor), side, keepEmptyBands);

    /// <summary>Moves a child next to a layout node: an item or a container (the anchor of a <see cref="DashboardFreeSpace"/>).</summary>
    public static void MoveNextTo(UIElement item, string anchorId, ItemSide side, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.MoveNextTo(layout, id, anchorId, side), keepEmptyBands);

    /// <summary>
    /// Moves a child to position <paramref name="index"/> of a band, counted before the move: a new line of a grid band,
    /// or a place in a scrolling or wrapping band.
    /// </summary>
    public static void MoveTo(UIElement item, int band, int index, bool keepEmptyBands = false) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.MoveTo(layout, id, band, index), keepEmptyBands);

    /// <summary>Constrains the width (grid columns) and height (rows) of a child of a grid band; null leaves a dimension as it is.</summary>
    public static void Resize(UIElement item, int? columnSpan, int? rowSpan) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.Resize(layout, id, columnSpan, rowSpan));

    /// <summary>Constrains the width (pixels) and height (rows) of a child of a scrolling or wrapping band; null leaves a dimension as it is.</summary>
    public static void ResizeWidth(UIElement item, double? width, int? rowSpan) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.ResizeWidth(layout, id, width, rowSpan));

    /// <summary>Moves a child one step with the keyboard (see <see cref="DashboardLayoutEngine.Step"/>).</summary>
    public static void Step(UIElement item, ItemSide direction, bool toNewBand = false) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.Step(layout, id, direction, toNewBand));

    /// <summary>Extends a child over the free room next to it (see <see cref="DashboardLayoutEngine.Fill"/>).</summary>
    public static void Fill(UIElement item, bool width) =>
        UpdateItem(item, (layout, id) => DashboardLayoutEngine.Fill(layout, id, width));

    /// <summary>Removes the size constraints of a child: it is greedy again.</summary>
    public static void SetAuto(UIElement item) => UpdateItem(item, DashboardLayoutEngine.SetAuto);

    /// <summary>True when a child has a size constraint.</summary>
    public static bool IsConstrained(UIElement item) =>
        PanelOf(item) is { } panel && DashboardPanel.GetItemId(item) is { } id && DashboardLayoutEngine.IsConstrained(panel.Layout, id);

    /// <summary>Removes a child; its siblings take its room and its band disappears when it was the last child.</summary>
    public static void Remove(UIElement item) => UpdateItem(item, DashboardLayoutEngine.Remove);

    /// <summary>Re-applies the layout rules.</summary>
    public static void Normalize(DashboardPanel panel) => Update(panel, (layout, _) => DashboardLayoutEngine.Normalize(layout));

    // --- Operations on bands ---

    /// <summary>Inserts a band at <paramref name="index"/>, holding <paramref name="items"/> (new children).</summary>
    public static void InsertBand(DashboardPanel panel, int index, BandMode mode, params UIElement[] items) =>
        Update(panel, (layout, idOf) => DashboardLayoutEngine.InsertBand(layout, index, mode, items.Select(i => LayoutNode.Item() with { Id = idOf(i) }).ToArray()), items);

    /// <summary>Removes a band and its children.</summary>
    public static void RemoveBand(DashboardPanel panel, int index) =>
        Update(panel, (layout, _) => DashboardLayoutEngine.RemoveBand(layout, index));

    /// <summary>Changes the mode of a band; items keep their order and their size on screen.</summary>
    public static void SetBandMode(DashboardPanel panel, int index, BandMode mode) =>
        Update(panel, (layout, _) => DashboardLayoutEngine.SetBandMode(layout, index, mode, panel.ActualWidth, panel.Gap, panel.MinItemWidth));

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
        var elements = new Dictionary<string, UIElement>();
        foreach (UIElement child in panel.Children)
            if (DashboardPanel.GetItemId(child) is { } id) elements[id] = child;
        foreach (var item in newItems)
        {
            if (DashboardPanel.GetItemId(item) is { } existing && elements.ContainsKey(existing)) continue;
            var id = Guid.NewGuid().ToString("N");
            DashboardPanel.SetItemId(item, id);
            elements[id] = item;
        }
        var result = operation(panel.Layout, element => DashboardPanel.GetItemId(element) ?? "");
        Write(panel, result, elements);
    }

    private static void UpdateItem(UIElement item, Func<DashboardLayoutDefinition, string, DashboardLayoutDefinition> operation, bool keepEmptyBands = false)
    {
        var panel = RequirePanel(item);
        var id = RequireId(item);
        Update(panel, (layout, _) =>
        {
            var result = operation(layout, id);
            if (!keepEmptyBands) return result;
            // The engine drops a band the item left empty: put it back where it was
            var kept = layout.Bands.Select(b => result.Bands.FirstOrDefault(r => r.Id == b.Id) ?? b with { Items = [] })
                .Concat(result.Bands.Where(r => layout.Bands.All(b => b.Id != r.Id)));
            return result with { Bands = kept.ToArray() };
        });
    }

    private static DashboardPanel RequirePanel(UIElement item) =>
        PanelOf(item) ?? throw new InvalidOperationException("The element is not a child of a DashboardPanel.");

    private static string RequireId(UIElement item) =>
        DashboardPanel.GetItemId(item) ?? throw new InvalidOperationException("The element is not an item of the panel's layout.");

    /// <summary>Shows a layout: places the elements of its items, removes the other children.</summary>
    private static void Write(DashboardPanel panel, DashboardLayoutDefinition layout, IReadOnlyDictionary<string, UIElement> elements)
    {
        var placed = new HashSet<UIElement>();
        foreach (var item in layout.Bands.SelectMany(b => b.Items).SelectMany(n => n.Items()))
        {
            if (!elements.TryGetValue(item.Id, out var element)) continue;
            DashboardPanel.SetItemId(element, item.Id);
            placed.Add(element);
            if (!panel.Children.Contains(element)) panel.Children.Add(element);
        }
        foreach (var child in panel.Children.Cast<UIElement>().Where(c => !placed.Contains(c)).ToList())
            panel.Children.Remove(child);
        panel.Layout = layout;
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

/// <summary>The layout and children of a <see cref="DashboardPanel"/> at one moment (see <see cref="DashboardLayout.Capture"/>).</summary>
public sealed class DashboardLayoutSnapshot
{
    private readonly DashboardPanel _panel;
    private readonly DashboardLayoutDefinition _layout;
    private readonly IReadOnlyList<UIElement> _children;
    private readonly IReadOnlyList<(DashboardBand Band, double Offset)> _offsets;

    internal DashboardLayoutSnapshot(DashboardPanel panel)
    {
        _panel = panel;
        _layout = panel.Layout;
        _children = panel.Children.Cast<UIElement>().ToArray();
        _offsets = panel.Bands.Select(b => (b, b.HorizontalOffset)).ToArray();
    }

    /// <summary>Puts the captured layout and children back. Children added since stay, but are not shown.</summary>
    public void Restore()
    {
        foreach (var child in _children)
            if (!_panel.Children.Contains(child)) _panel.Children.Add(child);
        _panel.Layout = _layout;
        foreach (var (band, offset) in _offsets) band.HorizontalOffset = offset;
        _panel.RaiseEvent(new RoutedEventArgs(DashboardPanel.ArrangementChangedEvent, _panel));
    }
}
