namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Pure layout operations on a <see cref="DashboardLayoutDefinition"/>; each one returns a new, consistent layout:
/// <list type="bullet">
/// <item>in a <see cref="BandMode.Fit"/> band, items stay inside the grid and never overlap: an item moved, resized
/// or added pushes the others down, then everything floats up without jumping over another item;</item>
/// <item>a full-row item covers all the columns (Fit) or the whole band width (Scroll, Wrap);</item>
/// <item>a band emptied by a move or a removal disappears.</item>
/// </list>
/// Items and bands are identified by their <see cref="ItemLayout.Id"/> and <see cref="BandLayout.Id"/>.
/// <see cref="DashboardLayout"/> runs these operations on the children of a <see cref="DashboardPanel"/>.
/// </summary>
public static class DashboardLayoutEngine
{
    public const double MinRowHeight = 40;
    public const double MinWidth = 160;

    /// <summary>Width a Fit band is assumed to have when converting column spans to pixel widths and back.</summary>
    public const double ReferenceWidth = 1200;

    /// <summary>Clamps sizes and positions, then resolves overlaps and floats items up in grid bands.</summary>
    public static DashboardLayoutDefinition Normalize(DashboardLayoutDefinition layout)
    {
        var columns = Math.Max(1, layout.Columns);
        return layout with
        {
            Columns = columns,
            RowHeight = Math.Max(MinRowHeight, layout.RowHeight),
            Bands = layout.Bands.Select(b => b with { Items = Arranged(b.Mode, b.Items.Select(i => Clamp(i, b.Mode, columns)).ToList(), null) }).ToArray(),
        };
    }

    /// <summary>
    /// Makes a saved layout match the visuals to show: items referencing an unknown (or already shown) visual become
    /// empty, visuals without an item are added at the end, and an empty layout gets one empty item to start from.
    /// </summary>
    public static DashboardLayoutDefinition Place(DashboardLayoutDefinition? layout, IEnumerable<string> visualIds)
    {
        layout ??= new DashboardLayoutDefinition();
        var ids = visualIds.Distinct().ToList();
        var known = ids.ToHashSet();
        var shown = new HashSet<string>();
        var bands = layout.Bands
            .Select(b => b with
            {
                Items = b.Items.Select(i => i.VisualId is { } id && (!known.Contains(id) || !shown.Add(id)) ? i with { VisualId = null } : i).ToArray(),
            })
            .Where(b => b.Items.Count > 0)
            .ToList();
        layout = Normalize(layout with { Bands = bands });

        var unplaced = ids.Where(id => !shown.Contains(id)).ToList();
        if (unplaced.Count > 0)
        {
            var columns = layout.Columns;
            var target = layout.Bands.LastOrDefault() is { Mode: BandMode.Fit } last ? last : new BandLayout();
            var span = Math.Min(ItemLayout.DefaultColumnSpan, columns);
            var perRow = Math.Max(1, columns / span);
            var bottom = Bottom(target.Items);
            var added = unplaced.Select((id, index) => new ItemLayout
            {
                VisualId = id,
                Column = index % perRow * span,
                Row = bottom + index / perRow * ItemLayout.DefaultRowSpan,
                ColumnSpan = span,
            });
            var filled = target with { Items = [.. target.Items, .. added] };
            layout = layout with
            {
                Bands = ReferenceEquals(target, layout.Bands.LastOrDefault()) ? [.. layout.Bands.SkipLast(1), filled] : [.. layout.Bands, filled],
            };
        }
        if (layout.Bands.Count == 0)
            layout = layout with { Bands = [new BandLayout { Items = [new ItemLayout { ColumnSpan = Math.Min(ItemLayout.DefaultColumnSpan, layout.Columns) }] }] };
        return Normalize(layout);
    }

    /// <summary>Index of the band holding an item, or -1.</summary>
    public static int BandOf(DashboardLayoutDefinition layout, string itemId)
    {
        for (var i = 0; i < layout.Bands.Count; i++)
            if (layout.Bands[i].Items.Any(item => item.Id == itemId)) return i;
        return -1;
    }

    public static ItemLayout? Find(DashboardLayoutDefinition layout, string itemId) =>
        layout.Bands.SelectMany(b => b.Items).FirstOrDefault(i => i.Id == itemId);

    // --- Adding ---

    /// <summary>Adds an item at the end of a band (below everything in a grid band); a new band is created past the last one.</summary>
    public static DashboardLayoutDefinition Add(DashboardLayoutDefinition layout, int bandIndex, ItemLayout item)
    {
        layout = Normalize(layout);
        if (bandIndex < 0 || bandIndex >= layout.Bands.Count)
            return InsertBand(layout, layout.Bands.Count, BandMode.Fit, [item]);
        var band = layout.Bands[bandIndex];
        var added = band.Mode == BandMode.Fit
            ? item with { Column = 0, Row = Bottom(band.Items), ColumnSpan = Math.Min(item.ColumnSpan, layout.Columns) }
            : item;
        return WithBand(layout, band with { Items = [.. band.Items, added] }, item.Id);
    }

    /// <summary>
    /// Adds an item to the right of (or below) another one. In a grid band, "right" needs two free columns,
    /// otherwise the item goes below; a full-row item has no right side. In a scrolling or wrapping band, the
    /// new item comes right after the other one, with its height.
    /// </summary>
    public static DashboardLayoutDefinition AddNextTo(DashboardLayoutDefinition layout, string anchorId, ItemSide side, ItemLayout item)
    {
        layout = Normalize(layout);
        var bandIndex = BandOf(layout, anchorId);
        if (bandIndex < 0) return Add(layout, layout.Bands.Count - 1, item);
        var band = layout.Bands[bandIndex];
        var anchor = band.Items.First(i => i.Id == anchorId);

        if (band.Mode != BandMode.Fit)
        {
            var index = band.Items.ToList().IndexOf(anchor) + 1;
            return WithBand(layout, band with { Items = Insert(band.Items, index, item with { RowSpan = anchor.RowSpan, IsFullRow = false }) }, null);
        }

        var columns = layout.Columns;
        var right = anchor.Column + anchor.ColumnSpan;
        var placed = side == ItemSide.Right && !anchor.IsFullRow && columns - right >= 2
            ? item with { Column = right, Row = anchor.Row, ColumnSpan = Math.Min(ItemLayout.DefaultColumnSpan, columns - right), RowSpan = anchor.RowSpan, IsFullRow = false }
            // Below, or to the right without room: start a new line under the anchor
            : item with { Column = anchor.Column, Row = anchor.Row + anchor.RowSpan, ColumnSpan = anchor.ColumnSpan, RowSpan = ItemLayout.DefaultRowSpan, IsFullRow = false };
        return WithBand(layout, band with { Items = [.. band.Items, placed] }, placed.Id);
    }

    /// <summary>
    /// Gives up the full row of an item to add another one to its right: in a grid band the item keeps half of the
    /// columns and the new item takes the other half; in other bands the new item follows it.
    /// </summary>
    public static DashboardLayoutDefinition SplitFullRow(DashboardLayoutDefinition layout, string itemId, ItemLayout item)
    {
        layout = Normalize(layout);
        var columns = layout.Columns;
        var half = Math.Max(1, columns - Math.Min(ItemLayout.DefaultColumnSpan, columns / 2));
        var isGrid = BandOf(layout, itemId) is var b and >= 0 && layout.Bands[b].Mode == BandMode.Fit;
        layout = UpdateItem(layout, itemId, i => i with { IsFullRow = false, ColumnSpan = isGrid ? half : i.ColumnSpan });
        return AddNextTo(layout, itemId, ItemSide.Right, item);
    }

    // --- Bands ---

    /// <summary>Inserts a band holding <paramref name="items"/> at <paramref name="index"/>.</summary>
    public static DashboardLayoutDefinition InsertBand(DashboardLayoutDefinition layout, int index, BandMode mode, IReadOnlyList<ItemLayout> items, string? bandId = null)
    {
        layout = Normalize(layout);
        var flowed = mode == BandMode.Fit ? Flow(items, layout.Columns) : items;
        var band = new BandLayout { Id = bandId ?? Guid.NewGuid().ToString("N"), Mode = mode, Items = flowed };
        return Normalize(layout with { Bands = Insert(layout.Bands, index, band) });
    }

    /// <summary>Removes a band and its items.</summary>
    public static DashboardLayoutDefinition RemoveBand(DashboardLayoutDefinition layout, int index) =>
        index < 0 || index >= layout.Bands.Count ? layout
        : Normalize(layout with { Bands = layout.Bands.Where((_, i) => i != index).ToArray() });

    /// <summary>
    /// Changes how a band lays out its items. From a grid, items keep their order (top to bottom, left to right)
    /// and get a width proportional to their column span; to a grid, they flow left to right with a column span
    /// proportional to their width. Pass the band's actual width and the gap between items so that items keep their
    /// size on screen.
    /// </summary>
    public static DashboardLayoutDefinition SetBandMode(DashboardLayoutDefinition layout, int index, BandMode mode, double bandWidth = ReferenceWidth, double gap = 0)
    {
        layout = Normalize(layout);
        if (index < 0 || index >= layout.Bands.Count || layout.Bands[index].Mode == mode) return layout;
        var band = layout.Bands[index];
        var columns = layout.Columns;
        bandWidth = bandWidth > 0 ? bandWidth : ReferenceWidth;

        // Same geometry as the grid: a span of s columns is s column widths plus the s - 1 gaps between them
        var columnWidth = Math.Max(1, (bandWidth - (columns - 1) * gap) / columns);
        IReadOnlyList<ItemLayout> items;
        if (band.Mode == BandMode.Fit)
        {
            items = band.Items.OrderBy(i => i.Row).ThenBy(i => i.Column)
                .Select(i => i with { Width = Math.Max(MinWidth, Math.Round(i.ColumnSpan * columnWidth + (i.ColumnSpan - 1) * gap)) })
                .ToArray();
        }
        else if (mode == BandMode.Fit)
        {
            items = Flow(band.Items.Select(i => i with
            {
                ColumnSpan = Math.Clamp((int)Math.Round((i.Width + gap) / (columnWidth + gap)), 1, columns),
            }).ToArray(), columns);
        }
        else
        {
            items = band.Items; // Scroll <-> Wrap: same order and widths
        }
        return WithBand(layout, band with { Mode = mode, Items = items }, null);
    }

    // --- Moving and resizing ---

    /// <summary>Moves an item to a cell of a grid band; the items it lands on move down.</summary>
    public static DashboardLayoutDefinition Move(DashboardLayoutDefinition layout, string itemId, int bandIndex, int column, int row) =>
        MoveCore(layout, itemId, bandIndex, (target, item) =>
            [.. target.Items.Where(i => i.Id != itemId), item with { Column = column, Row = row }]);

    /// <summary>
    /// Moves an item to position <paramref name="index"/> of a scrolling or wrapping band. The index refers to the
    /// band as it is before the move, so "insert before the item at index i" works within one band too.
    /// </summary>
    public static DashboardLayoutDefinition MoveTo(DashboardLayoutDefinition layout, string itemId, int bandIndex, int index) =>
        MoveCore(layout, itemId, bandIndex, (target, item) =>
        {
            // The moved item is first replaced by a placeholder so that indexes stay those of the original band
            var items = target.Items.Select(i => i.Id == itemId ? null : i).ToList();
            items.Insert(Math.Clamp(index, 0, items.Count), item);
            return items.OfType<ItemLayout>().ToArray();
        });

    /// <summary>Changes the columns and rows an item covers in a grid band; the items it now overlaps move down.</summary>
    public static DashboardLayoutDefinition Resize(DashboardLayoutDefinition layout, string itemId, int columnSpan, int rowSpan) =>
        UpdateItem(Normalize(layout), itemId, i => i with { ColumnSpan = columnSpan, RowSpan = rowSpan });

    /// <summary>Changes the width and height of an item in a scrolling or wrapping band.</summary>
    public static DashboardLayoutDefinition ResizeWidth(DashboardLayoutDefinition layout, string itemId, double width, int rowSpan) =>
        UpdateItem(Normalize(layout), itemId, i => i with { Width = width, RowSpan = rowSpan });

    /// <summary>
    /// Makes an item take the whole width of its band. Turned off, the item keeps its current size and can then be
    /// narrowed to make room for others.
    /// </summary>
    public static DashboardLayoutDefinition SetFullRow(DashboardLayoutDefinition layout, string itemId, bool isFullRow)
    {
        layout = Normalize(layout);
        return UpdateItem(layout, itemId, i => isFullRow
            ? i with { IsFullRow = true }
            : i with { IsFullRow = false, ColumnSpan = i.IsFullRow ? layout.Columns : i.ColumnSpan });
    }

    /// <summary>Removes an item; its band disappears when it was the last one.</summary>
    public static DashboardLayoutDefinition Remove(DashboardLayoutDefinition layout, string itemId)
    {
        layout = Normalize(layout);
        var bands = layout.Bands
            .Select(b => b with { Items = b.Items.Where(i => i.Id != itemId).ToArray() })
            .Where((b, index) => b.Items.Count > 0 || layout.Bands[index].Items.Count == 0)
            .ToArray();
        return Normalize(layout with { Bands = bands });
    }

    // --- Helpers ---

    private static DashboardLayoutDefinition MoveCore(
        DashboardLayoutDefinition layout, string itemId, int bandIndex, Func<BandLayout, ItemLayout, IReadOnlyList<ItemLayout>> insert)
    {
        layout = Normalize(layout);
        if (Find(layout, itemId) is not { } item || bandIndex < 0 || bandIndex >= layout.Bands.Count) return layout;

        // The item leaves every other band; the target band places it (and drops its previous copy).
        // A band the item leaves empty disappears.
        var bands = layout.Bands.Select((b, i) =>
            i == bandIndex ? b with { Items = insert(b, item) }
            : b with { Items = b.Items.Where(x => x.Id != itemId).ToArray() })
            .Where((b, i) => b.Items.Count > 0 || layout.Bands[i].Items.Count == 0)
            .ToArray();
        return WithBands(layout, bands, itemId);
    }

    private static DashboardLayoutDefinition UpdateItem(DashboardLayoutDefinition layout, string itemId, Func<ItemLayout, ItemLayout> change) =>
        WithBands(layout, layout.Bands.Select(b => b with { Items = b.Items.Select(i => i.Id == itemId ? change(i) : i).ToArray() }).ToArray(), itemId);

    private static DashboardLayoutDefinition WithBand(DashboardLayoutDefinition layout, BandLayout band, string? priorityId) =>
        WithBands(layout, layout.Bands.Select(b => b.Id == band.Id ? band : b).ToArray(), priorityId);

    /// <summary>Applies new bands; the band holding <paramref name="priorityId"/> is arranged with that item first.</summary>
    private static DashboardLayoutDefinition WithBands(DashboardLayoutDefinition layout, IReadOnlyList<BandLayout> bands, string? priorityId)
    {
        var columns = layout.Columns;
        var arranged = bands.Select(b => priorityId is not null && b.Mode == BandMode.Fit && b.Items.Any(i => i.Id == priorityId)
            ? b with { Items = Arranged(BandMode.Fit, b.Items.Select(i => Clamp(i, BandMode.Fit, columns)).ToList(), priorityId) }
            : b).ToArray();
        return Normalize(layout with { Bands = arranged });
    }

    /// <summary>Places items left to right on grid lines, in their order.</summary>
    private static IReadOnlyList<ItemLayout> Flow(IReadOnlyList<ItemLayout> items, int columns)
    {
        var flowed = new List<ItemLayout>();
        int column = 0, row = 0, lineHeight = 0;
        foreach (var item in items)
        {
            var span = item.IsFullRow ? columns : Math.Clamp(item.ColumnSpan, 1, columns);
            if (column + span > columns)
            {
                column = 0;
                row += lineHeight;
                lineHeight = 0;
            }
            flowed.Add(item with { Column = column, Row = row, ColumnSpan = span });
            column += span;
            lineHeight = Math.Max(lineHeight, item.RowSpan);
        }
        return flowed;
    }

    private static IReadOnlyList<ItemLayout> Arranged(BandMode mode, List<ItemLayout> items, string? priorityId) =>
        mode == BandMode.Fit ? Arrange(items, priorityId) : items;

    /// <summary>
    /// Removes overlaps and gaps in a grid band. The priority item (just moved, resized or added) keeps its place
    /// first; the others, from top to bottom, are pushed below what they overlap. Then every item floats up as long
    /// as nothing is above it, without ever jumping over another item.
    /// </summary>
    private static IReadOnlyList<ItemLayout> Arrange(List<ItemLayout> items, string? priorityId)
    {
        var placed = new List<ItemLayout>();
        var priority = priorityId is null ? null : items.FirstOrDefault(i => i.Id == priorityId);
        if (priority is not null) placed.Add(priority);
        foreach (var item in items.Where(i => !ReferenceEquals(i, priority)).OrderBy(i => i.Row).ThenBy(i => i.Column))
        {
            var row = item.Row;
            while (Collides(item with { Row = row }, placed)) row++;
            placed.Add(item with { Row = row });
        }

        var compacted = new List<ItemLayout>();
        foreach (var item in placed.OrderBy(i => i.Row).ThenBy(i => i.Column))
        {
            var row = item.Row;
            while (row > 0 && !Collides(item with { Row = row - 1 }, compacted)) row--;
            compacted.Add(item with { Row = row });
        }
        // Keep the original order of the items (it is the save order)
        return items.Select(i => compacted.First(c => c.Id == i.Id)).ToArray();
    }

    private static bool Collides(ItemLayout item, IEnumerable<ItemLayout> others) => others.Any(o =>
        o.Id != item.Id
        && item.Column < o.Column + o.ColumnSpan && o.Column < item.Column + item.ColumnSpan
        && item.Row < o.Row + o.RowSpan && o.Row < item.Row + item.RowSpan);

    private static int Bottom(IEnumerable<ItemLayout> items) => items.Select(i => i.Row + i.RowSpan).DefaultIfEmpty(0).Max();

    private static ItemLayout Clamp(ItemLayout item, BandMode mode, int columns)
    {
        item = item with { RowSpan = Math.Max(1, item.RowSpan), Width = Math.Max(MinWidth, item.Width) };
        if (mode != BandMode.Fit) return item;
        var span = item.IsFullRow ? columns : Math.Clamp(item.ColumnSpan, 1, columns);
        return item with
        {
            ColumnSpan = span,
            Column = item.IsFullRow ? 0 : Math.Clamp(item.Column, 0, columns - span),
            Row = Math.Max(0, item.Row),
        };
    }

    private static T[] Insert<T>(IReadOnlyList<T> items, int index, T item)
    {
        var list = items.ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), item);
        return [.. list];
    }
}
