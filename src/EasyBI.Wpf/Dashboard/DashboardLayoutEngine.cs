namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Pure layout operations on a <see cref="DashboardLayoutDefinition"/>; each one returns a new, normalized layout:
/// <list type="bullet">
/// <item>in a <see cref="BandMode.Fit"/> band, an item added or moved next to another one joins its container when the
/// side follows the container's direction, otherwise the two are wrapped in a new container (side by side or stacked);</item>
/// <item>new and moved items are greedy (no width constraint): they share the room with their siblings, so a grid
/// band never has a gap; constraints come from resizing, and <see cref="SetAuto"/> removes them;</item>
/// <item>containers with a single child, or inside a container of the same direction, are merged away;</item>
/// <item>a band emptied by a move or a removal disappears.</item>
/// </list>
/// Nodes and bands are identified by their <see cref="LayoutNode.Id"/> and <see cref="BandLayout.Id"/>.
/// <see cref="DashboardLayout"/> runs these operations on a <see cref="DashboardPanel"/>.
/// </summary>
public static class DashboardLayoutEngine
{
    /// <summary>Smallest fixed width of an item in scrolling and wrapping bands.</summary>
    public const double MinWidth = 160;

    /// <summary>Width a band is assumed to have when converting columns to pixels and back without a better one.</summary>
    public const double ReferenceWidth = 1200;

    // --- Normalizing and placing visuals ---

    /// <summary>
    /// Clamps the constraints, drops those that mean nothing where the node is (a width inside a stack...), merges
    /// containers with a single child or of the same direction as their parent, and removes empty containers.
    /// </summary>
    public static DashboardLayoutDefinition Normalize(DashboardLayoutDefinition layout)
    {
        var columns = Math.Max(1, layout.Columns);
        return layout with
        {
            Columns = columns,
            RowHeight = Math.Max(20, layout.RowHeight),
            Bands = layout.Bands.Select(b => b with { Items = NormalizeBand(b.Mode, b.Items, columns) }).ToArray(),
        };
    }

    /// <summary>
    /// Makes a saved layout match the visuals to show: items referencing an unknown (or already shown) visual become
    /// empty, visuals without an item are added at the end two per line, and an empty layout gets one empty item.
    /// </summary>
    public static DashboardLayoutDefinition Place(DashboardLayoutDefinition? layout, IEnumerable<string> visualIds)
    {
        layout = Normalize(layout ?? new DashboardLayoutDefinition());
        var ids = visualIds.Distinct().ToList();
        var known = ids.ToHashSet();
        var shown = new HashSet<string>();
        LayoutNode Check(LayoutNode node) => node.IsItem
            ? node.VisualId is { } id && (!known.Contains(id) || !shown.Add(id)) ? node with { VisualId = null } : node
            : node with { Children = node.Children.Select(Check).ToArray() };
        var bands = layout.Bands.Select(b => b with { Items = b.Items.Select(Check).ToArray() }).Where(b => b.Items.Count > 0).ToList();

        var unplaced = ids.Where(id => !shown.Contains(id)).ToList();
        if (unplaced.Count > 0)
        {
            var lines = unplaced.Chunk(2).Select(pair => pair.Length == 1
                ? LayoutNode.Item(pair[0])
                : LayoutNode.Row(LayoutNode.Item(pair[0]), LayoutNode.Item(pair[1])));
            if (bands.LastOrDefault() is { Mode: BandMode.Fit } last)
                bands[^1] = last with { Items = [.. last.Items, .. lines] };
            else
                bands.Add(new BandLayout { Items = lines.ToArray() });
        }
        if (bands.Count == 0) bands.Add(new BandLayout { Items = [LayoutNode.Item()] });
        return Normalize(layout with { Bands = bands });
    }

    // --- Finding ---

    /// <summary>Index of the band holding a node, or -1.</summary>
    /// <summary>
    /// The part of a layout showing some visuals (copied items): each band keeps only their items, with their sizes and
    /// their arrangement (containers left with one child are simplified), and bands left empty are dropped.
    /// </summary>
    public static DashboardLayoutDefinition Extract(DashboardLayoutDefinition layout, IReadOnlySet<string> visualIds)
    {
        LayoutNode? Prune(LayoutNode node) => node.IsItem
            ? node.VisualId is { } id && visualIds.Contains(id) ? node : null
            : node.Children.Select(Prune).OfType<LayoutNode>().ToArray() switch
            {
                [] => null,
                // A container left with one child is that child, at the container's width
                [var only] => only with { ColumnSpan = only.ColumnSpan ?? node.ColumnSpan },
                var children => node with { Children = children },
            };
        var bands = layout.Bands.Select(b => b with { Items = b.Items.Select(Prune).OfType<LayoutNode>().ToArray() }).Where(b => b.Items.Count > 0).ToArray();
        return Normalize(layout with { Bands = bands });
    }

    /// <summary>
    /// Inserts copied bands (see <see cref="Extract"/>) after the line of the item showing <paramref name="anchorVisualId"/>,
    /// or at the end without one: the lines of a grid band go into the anchor's band when it is a grid too, other bands come
    /// as new bands after it. Widths are converted to the layout's columns.
    /// </summary>
    public static DashboardLayoutDefinition Insert(DashboardLayoutDefinition layout, DashboardLayoutDefinition copied, string? anchorVisualId)
    {
        layout = Normalize(layout);
        var columns = layout.Columns;
        var scale = (double)columns / Math.Max(1, copied.Columns);
        LayoutNode Scale(LayoutNode node) => node with
        {
            ColumnSpan = node.ColumnSpan is { } span ? Math.Clamp((int)Math.Round(span * scale), 1, columns) : null,
            Children = node.Children.Select(Scale).ToArray(),
        };

        var bands = layout.Bands.ToList();
        var band = bands.FindIndex(b => b.Items.Any(line => anchorVisualId is not null && line.Items().Any(i => i.VisualId == anchorVisualId)));
        if (band < 0) band = bands.Count - 1;
        var line = band < 0 ? -1 : anchorVisualId is null
            ? bands[band].Items.Count - 1
            : bands[band].Items.ToList().FindIndex(l => l.Items().Any(i => i.VisualId == anchorVisualId));
        var nextBand = band + 1;
        foreach (var copy in copied.Bands)
        {
            var items = copy.Items.Select(Scale).ToArray();
            if (band >= 0 && copy.Mode == BandMode.Fit && bands[band].Mode == BandMode.Fit)
            {
                var lines = bands[band].Items.ToList();
                lines.InsertRange(line + 1, items);
                bands[band] = bands[band] with { Items = lines };
                line += items.Length;
            }
            else
            {
                bands.Insert(nextBand++, new BandLayout { Mode = copy.Mode, Items = items });
            }
        }
        return Normalize(layout with { Bands = bands });
    }

    public static int BandOf(DashboardLayoutDefinition layout, string nodeId) =>
        layout.Bands.Select((b, i) => (b, i)).FirstOrDefault(x => x.b.Items.Any(n => n.Descendants().Any(d => d.Id == nodeId)), (null!, -1)).Item2;

    public static LayoutNode? Find(DashboardLayoutDefinition layout, string nodeId) =>
        layout.Bands.SelectMany(b => b.Items).SelectMany(n => n.Descendants()).FirstOrDefault(n => n.Id == nodeId);

    /// <summary>The container holding a node, or null for a line of a grid band (or an item of another band).</summary>
    public static LayoutNode? ParentOf(DashboardLayoutDefinition layout, string nodeId) =>
        layout.Bands.SelectMany(b => b.Items).SelectMany(n => n.Descendants()).FirstOrDefault(n => n.Children.Any(c => c.Id == nodeId));

    /// <summary>
    /// The node whose width an item's width is: the item, or the stack it is in (and so on up), since a stack's
    /// children take its whole width.
    /// </summary>
    public static LayoutNode? WidthNodeOf(DashboardLayoutDefinition layout, string itemId)
    {
        var node = Find(layout, itemId);
        while (node is not null && ParentOf(layout, node.Id) is { Split: SplitDirection.Column } stack) node = stack;
        return node;
    }

    /// <summary>True when an item has a size constraint (it is not fully greedy).</summary>
    public static bool IsConstrained(DashboardLayoutDefinition layout, string itemId) =>
        Find(layout, itemId) is { } item && (item.RowSpan is not null || item.Width is not null || WidthNodeOf(layout, itemId)?.ColumnSpan is not null);

    // --- Adding ---

    /// <summary>Adds an item at the end of a band (a new line in a grid band); a new grid band is created past the last one.</summary>
    public static DashboardLayoutDefinition Add(DashboardLayoutDefinition layout, int bandIndex, LayoutNode item) =>
        InsertAt(layout, bandIndex, int.MaxValue, item);

    /// <summary>
    /// Inserts an item at position <paramref name="index"/> of a band: as a new line of a grid band (it takes the
    /// whole width), or among the items of a scrolling or wrapping band. A new grid band is created past the last one.
    /// </summary>
    public static DashboardLayoutDefinition InsertAt(DashboardLayoutDefinition layout, int bandIndex, int index, LayoutNode item)
    {
        layout = Normalize(layout);
        if (bandIndex < 0 || bandIndex >= layout.Bands.Count)
            return InsertBand(layout, layout.Bands.Count, BandMode.Fit, [item]);
        var band = layout.Bands[bandIndex];
        var items = band.Items.ToList();
        items.Insert(Math.Clamp(index, 0, items.Count), Greedy(item));
        return Normalize(WithBand(layout, band with { Items = items }));
    }

    /// <summary>
    /// Adds an item next to another node. In a grid band: on the anchor's left or right (above or below), the item
    /// joins the anchor's container if it lays its children side by side (stacks them), otherwise the anchor and the
    /// item are wrapped in a new container; when a line has no room left for one more column, its constraints are
    /// dropped so that everyone shares it. In scrolling and wrapping bands, the item comes after (right, below) or
    /// before (left, above) the anchor.
    /// </summary>
    public static DashboardLayoutDefinition AddNextTo(DashboardLayoutDefinition layout, string anchorId, ItemSide side, LayoutNode item)
    {
        layout = Normalize(layout);
        var bandIndex = BandOf(layout, anchorId);
        return bandIndex < 0 ? Add(layout, layout.Bands.Count - 1, item) : Normalize(PlaceNextTo(layout, bandIndex, anchorId, side, Greedy(item)));
    }

    // --- Moving ---

    /// <summary>
    /// Moves an item next to another node, with the rules of <see cref="AddNextTo"/>. The item becomes greedy in width
    /// and keeps its height. A band the item leaves empty disappears.
    /// </summary>
    public static DashboardLayoutDefinition MoveNextTo(DashboardLayoutDefinition layout, string itemId, string anchorId, ItemSide side)
    {
        layout = Normalize(layout);
        if (itemId == anchorId || Find(layout, itemId) is not { IsItem: true } item || Find(layout, anchorId) is null) return layout;
        // The anchor may be the container the item leaves: it is only merged away after the move
        var without = Detach(layout, itemId);
        var bandIndex = BandOf(without, anchorId);
        if (bandIndex < 0) return layout;
        var moved = PlaceNextTo(without, bandIndex, anchorId, side, Greedy(item));
        return DropEmptiedBands(layout, Normalize(moved));
    }

    /// <summary>
    /// Moves an item to position <paramref name="index"/> of a band: a new line of a grid band, or a place among the
    /// items of a scrolling or wrapping band. The index refers to the band as it is before the move, so "insert before
    /// the line (item) at index i" works within one band too.
    /// </summary>
    public static DashboardLayoutDefinition MoveTo(DashboardLayoutDefinition layout, string itemId, int bandIndex, int index)
    {
        layout = Normalize(layout);
        if (Find(layout, itemId) is not { IsItem: true } item || bandIndex < 0 || bandIndex >= layout.Bands.Count) return layout;
        // Inserted first under a temporary id, so that the index is the one of the band before the move
        var temporary = Guid.NewGuid().ToString("N");
        var band = layout.Bands[bandIndex];
        var items = band.Mode == BandMode.Fit ? band.Items.ToList() : band.Items.SelectMany(i => i.Items()).ToList();
        items.Insert(Math.Clamp(index, 0, items.Count), Greedy(item) with { Id = temporary });
        var inserted = Detach(WithBand(layout, band with { Items = items }), itemId);
        var moved = Normalize(Rename(inserted, temporary, itemId));
        return DropEmptiedBands(layout, moved);
    }

    /// <summary>
    /// Moves an item one step with the keyboard. Down: an item sharing its line goes to a new line below it; alone on
    /// its line, it joins the next line (on its left), and from the last one (or from a scrolling or wrapping band) it
    /// goes to the first line of the band below, created if there is none. Up does the same upwards (joining the line
    /// above on its right, then to the end of the band above), so that Down and Up undo each other. With
    /// <paramref name="toNewBand"/>, Down and Up put the item in a new grid band below or above its band. Left and
    /// Right swap the item with its neighbour on its line (in a scrolling or wrapping band, with the previous or next
    /// item); at the end of the line nothing moves.
    /// </summary>
    public static DashboardLayoutDefinition Step(DashboardLayoutDefinition layout, string itemId, ItemSide direction, bool toNewBand = false)
    {
        layout = Normalize(layout);
        var bandIndex = BandOf(layout, itemId);
        if (bandIndex < 0 || Find(layout, itemId) is not { IsItem: true }) return layout;
        var band = layout.Bands[bandIndex];
        var lineIndex = band.Items.ToList().FindIndex(l => l.Items().Any(i => i.Id == itemId));
        var flow = band.Items.SelectMany(i => i.Items()).ToList();

        if (direction is ItemSide.Left or ItemSide.Right)
        {
            var neighbours = band.Mode == BandMode.Fit ? band.Items[lineIndex].Items().ToList() : flow;
            var position = neighbours.FindIndex(i => i.Id == itemId);
            var neighbour = position + (direction == ItemSide.Right ? 1 : -1);
            if (neighbour < 0 || neighbour >= neighbours.Count) return layout;
            return band.Mode == BandMode.Fit
                ? MoveNextTo(layout, itemId, neighbours[neighbour].Id, direction)
                : MoveTo(layout, itemId, bandIndex, direction == ItemSide.Right ? position + 2 : position - 1);
        }

        var down = direction == ItemSide.Below;
        if (toNewBand) return MoveToNewBand(layout, itemId, down ? bandIndex + 1 : bandIndex);
        if (band.Mode == BandMode.Fit)
        {
            // Indexes count the item's line before the move
            if (!band.Items[lineIndex].IsItem) return MoveTo(layout, itemId, bandIndex, down ? lineIndex + 1 : lineIndex);
            if (down ? lineIndex < band.Items.Count - 1 : lineIndex > 0)
            {
                var next = band.Items[lineIndex + (down ? 1 : -1)].Items().ToArray();
                return down ? MoveNextTo(layout, itemId, next[0].Id, ItemSide.Left) : MoveNextTo(layout, itemId, next[^1].Id, ItemSide.Right);
            }
        }
        var target = bandIndex + (down ? 1 : -1);
        if (target < 0 || target >= layout.Bands.Count) return MoveToNewBand(layout, itemId, down ? bandIndex + 1 : bandIndex);
        var targetBand = layout.Bands[target];
        var count = targetBand.Mode == BandMode.Fit ? targetBand.Items.Count : targetBand.Items.SelectMany(i => i.Items()).Count();
        return MoveTo(layout, itemId, target, down ? 0 : count);
    }

    /// <summary>
    /// Extends an item over the free room next to it, without shrinking its neighbours: in <paramref name="width"/>, when
    /// the items beside it all have a fixed width (or it is alone on a narrowed line), or in height, when it is in a stack
    /// whose items all have a fixed height. The item becomes greedy in that dimension; without free room nothing changes.
    /// </summary>
    public static DashboardLayoutDefinition Fill(DashboardLayoutDefinition layout, string itemId, bool width)
    {
        layout = Normalize(layout);
        var bandIndex = BandOf(layout, itemId);
        if (bandIndex < 0 || Find(layout, itemId) is not { IsItem: true } item) return layout;
        var band = layout.Bands[bandIndex];

        if (band.Mode != BandMode.Fit)
        {
            var items = band.Items.SelectMany(i => i.Items()).ToArray();
            return width && item.Width is not null && items.All(i => i.Width is not null)
                ? Normalize(Update(layout, itemId, i => i with { Width = null }))
                : layout;
        }
        if (width)
        {
            if (WidthNodeOf(layout, itemId) is not { ColumnSpan: not null } node) return layout;
            var siblings = ParentOf(layout, node.Id)?.Children ?? [];
            return siblings.All(s => s.ColumnSpan is not null)
                ? Normalize(Update(layout, node.Id, n => n with { ColumnSpan = null }))
                : layout;
        }
        // The stack whose items all have a fixed height, from the item up
        for (var node = item; ParentOf(layout, node.Id) is { } parent; node = parent)
        {
            if (parent.Split == SplitDirection.Column && node.RowSpan is not null && parent.Children.All(c => c.RowSpan is not null))
                return Normalize(Update(layout, node.Id, n => n with { RowSpan = null }));
        }
        return layout;
    }

    private static DashboardLayoutDefinition MoveToNewBand(DashboardLayoutDefinition layout, string itemId, int index) =>
        MoveTo(InsertBand(layout, index, BandMode.Fit, []), itemId, index, 0);

    // --- Sizes ---

    /// <summary>
    /// Constrains an item of a grid band: <paramref name="columnSpan"/> fixes its width (the width of the stack it is
    /// in, if any), <paramref name="rowSpan"/> its height; null leaves a dimension as it is. A width never leaves less
    /// than one column to each sibling.
    /// </summary>
    public static DashboardLayoutDefinition Resize(DashboardLayoutDefinition layout, string itemId, int? columnSpan, int? rowSpan)
    {
        layout = Normalize(layout);
        if (Find(layout, itemId) is not { IsItem: true }) return layout;
        if (rowSpan is { } rows) layout = Update(layout, itemId, i => i with { RowSpan = Math.Max(1, rows) });
        if (columnSpan is { } span && WidthNodeOf(layout, itemId) is { } node)
        {
            var bandIndex = BandOf(layout, node.Id);
            var columns = Columns(layout, bandIndex);
            var parent = ParentOf(layout, node.Id);
            var room = parent is null
                ? layout.Columns
                : columns[parent.Id].Span - parent.Children.Where(c => c.Id != node.Id).Sum(c => c.ColumnSpan ?? 1);
            layout = Update(layout, node.Id, n => n with { ColumnSpan = Math.Clamp(span, 1, Math.Max(1, room)) });
        }
        return Normalize(layout);
    }

    /// <summary>Constrains an item of a scrolling or wrapping band: width in pixels and height in rows (null leaves them as they are).</summary>
    public static DashboardLayoutDefinition ResizeWidth(DashboardLayoutDefinition layout, string itemId, double? width, int? rowSpan) =>
        Normalize(Update(Normalize(layout), itemId, i => i with
        {
            Width = width is { } w ? Math.Max(MinWidth, w) : i.Width,
            RowSpan = rowSpan is { } rows ? Math.Max(1, rows) : i.RowSpan,
        }));

    /// <summary>Removes the size constraints of an item: it is greedy again, in width and height.</summary>
    public static DashboardLayoutDefinition SetAuto(DashboardLayoutDefinition layout, string itemId)
    {
        layout = Normalize(layout);
        if (WidthNodeOf(layout, itemId) is { } node) layout = Update(layout, node.Id, n => n with { ColumnSpan = null });
        return Normalize(Update(layout, itemId, i => i with { RowSpan = null, Width = null }));
    }

    /// <summary>Removes an item; its siblings take its room, and its band disappears when it was the last item.</summary>
    public static DashboardLayoutDefinition Remove(DashboardLayoutDefinition layout, string itemId)
    {
        layout = Normalize(layout);
        return DropEmptiedBands(layout, Normalize(Detach(layout, itemId)));
    }

    // --- Bands ---

    /// <summary>Inserts a band holding <paramref name="items"/> (one per line in a grid band) at <paramref name="index"/>.</summary>
    public static DashboardLayoutDefinition InsertBand(DashboardLayoutDefinition layout, int index, BandMode mode, IReadOnlyList<LayoutNode> items, string? bandId = null)
    {
        layout = Normalize(layout);
        var band = new BandLayout { Id = bandId ?? Guid.NewGuid().ToString("N"), Mode = mode, Items = items.Select(Greedy).ToArray() };
        var bands = layout.Bands.ToList();
        bands.Insert(Math.Clamp(index, 0, bands.Count), band);
        return Normalize(layout with { Bands = bands });
    }

    /// <summary>Removes a band and its items.</summary>
    public static DashboardLayoutDefinition RemoveBand(DashboardLayoutDefinition layout, int index) =>
        index < 0 || index >= layout.Bands.Count ? layout
        : Normalize(layout with { Bands = layout.Bands.Where((_, i) => i != index).ToArray() });

    /// <summary>
    /// Changes how a band lays out its items, keeping their order and their size on screen. To a grid, the lines are
    /// those a wrapping band of <paramref name="bandWidth"/> would make; from a grid, items keep their width in pixels
    /// when it was constrained, and stay greedy otherwise.
    /// </summary>
    public static DashboardLayoutDefinition SetBandMode(
        DashboardLayoutDefinition layout, int index, BandMode mode, double bandWidth = ReferenceWidth, double gap = 0, double minItemWidth = 280)
    {
        layout = Normalize(layout);
        if (index < 0 || index >= layout.Bands.Count || layout.Bands[index].Mode == mode) return layout;
        var band = layout.Bands[index];
        bandWidth = bandWidth > 0 ? bandWidth : ReferenceWidth;
        var settings = new ArrangeSettings(bandWidth, layout.Columns, layout.RowHeight, gap, minItemWidth);
        IReadOnlyList<LayoutNode> items;
        if (band.Mode == BandMode.Fit)
        {
            var columns = Columns(layout, index);
            items = band.Items.SelectMany(i => i.Items()).Select(i => i with
            {
                ColumnSpan = null,
                Width = WidthNodeOf(layout, i.Id) is { ColumnSpan: not null } node ? Math.Round(settings.WidthOf(columns[node.Id].Span)) : null,
            }).ToArray();
        }
        else if (mode == BandMode.Fit)
        {
            // The lines a wrapping band makes, each item keeping its width in columns
            var wrapped = BandArrangement.Arrange(band with { Mode = BandMode.Wrap }, settings);
            items = band.Items.GroupBy(i => wrapped.Items[i.Id].Top).Select(line => Line(line.Select(i => i with
            {
                Width = null,
                ColumnSpan = i.Width is { } w ? Math.Clamp((int)Math.Round((w + gap) / Math.Max(1, settings.ColumnWidth + gap)), 1, layout.Columns) : null,
            }).ToArray())).ToArray();
        }
        else
        {
            items = band.Items;
        }
        return Normalize(WithBand(layout, band with { Mode = mode, Items = items }));
    }

    // --- Helpers ---

    /// <summary>The grid columns covered by every node of a grid band.</summary>
    private static IReadOnlyDictionary<string, (int Start, int Span)> Columns(DashboardLayoutDefinition layout, int bandIndex) =>
        BandArrangement.Arrange(layout.Bands[bandIndex] with { Mode = BandMode.Fit }, new ArrangeSettings(ReferenceWidth, layout.Columns, layout.RowHeight, 0, 1)).Columns;

    private static LayoutNode Greedy(LayoutNode node) => node with { ColumnSpan = null };

    private static LayoutNode Line(LayoutNode[] items) => items.Length == 1 ? items[0] : LayoutNode.Row(items);

    private static DashboardLayoutDefinition PlaceNextTo(DashboardLayoutDefinition layout, int bandIndex, string anchorId, ItemSide side, LayoutNode item)
    {
        var band = layout.Bands[bandIndex];
        var after = side is ItemSide.Right or ItemSide.Below;
        if (band.Mode != BandMode.Fit)
        {
            var items = band.Items.SelectMany(i => i.Items()).ToList();
            var anchor = Math.Max(0, items.FindIndex(i => i.Descendants().Any(d => d.Id == anchorId)));
            items.Insert(anchor + (after ? 1 : 0), item);
            return WithBand(layout, band with { Items = items });
        }

        var sideways = side is ItemSide.Right or ItemSide.Left;
        var parent = ParentOf(layout, anchorId);
        // The lines of a grid band are stacked: they behave as a stack's children
        var direction = parent?.Split ?? SplitDirection.Column;
        if (direction == (sideways ? SplitDirection.Row : SplitDirection.Column))
        {
            if (parent is null)
            {
                var lines = band.Items.ToList();
                lines.Insert(lines.FindIndex(l => l.Id == anchorId) + (after ? 1 : 0), item);
                return WithBand(layout, band with { Items = lines });
            }
            var children = parent.Children.ToList();
            children.Insert(children.FindIndex(c => c.Id == anchorId) + (after ? 1 : 0), item);
            if (direction == SplitDirection.Row)
            {
                // No column left for the newcomer: everyone shares the line
                var span = Columns(layout, bandIndex)[parent.Id].Span;
                if (children.Sum(c => c.ColumnSpan ?? 1) > span) children = children.Select(c => c with { ColumnSpan = null }).ToList();
            }
            return Update(layout, parent.Id, p => p with { Children = children });
        }

        // Across the container: the anchor and the item make a new container in its place
        return Update(layout, anchorId, anchor =>
        {
            LayoutNode[] pair = after ? [anchor, item] : [item, anchor];
            if (sideways) return LayoutNode.Row(pair);
            // A stack takes the width its first child had in the line
            var stack = LayoutNode.Column(after ? [anchor with { ColumnSpan = null }, item] : [item, anchor with { ColumnSpan = null }]);
            return stack with { ColumnSpan = anchor.ColumnSpan };
        });
    }

    /// <summary>Removes a node without merging anything (containers it leaves empty or alone are merged by <see cref="Normalize"/>).</summary>
    private static DashboardLayoutDefinition Detach(DashboardLayoutDefinition layout, string nodeId) =>
        layout with { Bands = layout.Bands.Select(b => b with { Items = Without(b.Items, nodeId) }).ToArray() };

    private static IReadOnlyList<LayoutNode> Without(IReadOnlyList<LayoutNode> nodes, string nodeId) =>
        nodes.Where(n => n.Id != nodeId).Select(n => n.IsItem ? n : n with { Children = Without(n.Children, nodeId) }).ToArray();

    private static DashboardLayoutDefinition Update(DashboardLayoutDefinition layout, string nodeId, Func<LayoutNode, LayoutNode> change)
    {
        IReadOnlyList<LayoutNode> Map(IReadOnlyList<LayoutNode> nodes) =>
            nodes.Select(n => n.Id == nodeId ? change(n) : n.IsItem ? n : n with { Children = Map(n.Children) }).ToArray();
        return layout with { Bands = layout.Bands.Select(b => b with { Items = Map(b.Items) }).ToArray() };
    }

    private static DashboardLayoutDefinition Rename(DashboardLayoutDefinition layout, string from, string to) => Update(layout, from, n => n with { Id = to });

    private static DashboardLayoutDefinition WithBand(DashboardLayoutDefinition layout, BandLayout band) =>
        layout with { Bands = layout.Bands.Select(b => b.Id == band.Id ? band : b).ToArray() };

    /// <summary>Removes the bands an operation emptied (bands that were already empty stay).</summary>
    private static DashboardLayoutDefinition DropEmptiedBands(DashboardLayoutDefinition before, DashboardLayoutDefinition after) =>
        after with { Bands = after.Bands.Where(b => b.Items.Count > 0 || before.Bands.Any(o => o.Id == b.Id && o.Items.Count == 0)).ToArray() };

    private static IReadOnlyList<LayoutNode> NormalizeBand(BandMode mode, IReadOnlyList<LayoutNode> items, int columns)
    {
        if (mode != BandMode.Fit)
            return items.SelectMany(i => i.Items()).Select(i => Clamp(i with { ColumnSpan = null }, columns)).ToArray();
        // Lines behave as the children of a stack: a stack among them is split into lines
        return items.SelectMany(i => Simplify(i, columns))
            .SelectMany(l => l.Split == SplitDirection.Column ? l.Children : [l])
            .Select(l => l.IsItem ? l : l with { ColumnSpan = null })
            .ToArray();
    }

    private static IEnumerable<LayoutNode> Simplify(LayoutNode node, int columns)
    {
        if (node.IsItem)
        {
            yield return Clamp(node, columns);
            yield break;
        }
        var split = node.Split!.Value;
        var children = node.Children
            .SelectMany(c => Simplify(c, columns))
            // A container of the same direction as its parent adds nothing: its children join the parent
            .SelectMany(c => c.Split == split ? c.Children : [c])
            // Constraints that mean nothing here: a width inside a stack, the height of a container side by side
            .Select(c => split == SplitDirection.Column ? c with { ColumnSpan = null } : c.IsItem ? c : c with { RowSpan = null })
            .ToArray();
        if (children.Length == 0) yield break;
        // A container with one child is that child, unless its constraint along the container would be lost
        if (children.Length == 1 && (split == SplitDirection.Row ? children[0].ColumnSpan : children[0].RowSpan) is null)
        {
            var child = children[0];
            yield return child with { ColumnSpan = child.ColumnSpan ?? node.ColumnSpan, RowSpan = child.RowSpan ?? node.RowSpan };
            yield break;
        }
        yield return node with { Children = children, Width = null, VisualId = null };
    }

    private static LayoutNode Clamp(LayoutNode node, int columns) => node with
    {
        ColumnSpan = node.ColumnSpan is { } span ? Math.Clamp(span, 1, columns) : null,
        RowSpan = node.RowSpan is { } rows ? Math.Max(1, rows) : null,
        Width = node.Width is { } width ? Math.Max(MinWidth, width) : null,
    };
}
