using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Where a new item goes relative to an existing one.</summary>
public enum ItemSide { Right, Below }

/// <summary>
/// Dashboard layout operations. Each one returns a new <see cref="ReportDefinition"/> whose visuals and
/// layout stay consistent:
/// <list type="bullet">
/// <item>in a <see cref="BandMode.Fit"/> band, items stay inside the grid and never overlap: an item moved,
/// resized or added pushes the others down, then everything floats up without jumping over another item;</item>
/// <item>a full-row item covers all the columns (Fit) or the whole band width (Scroll, Wrap);</item>
/// <item>every visual is shown by exactly one item, items only reference existing visuals, and removing an
/// item removes its visual;</item>
/// <item>empty bands disappear, and there is always at least one band with one item.</item>
/// </list>
/// </summary>
public static class ReportLayout
{
    public const int DefaultColumnSpan = 6;
    public const int DefaultRowSpan = 3;
    public const double MinRowHeight = 40;
    public const double MinWidth = 160;

    /// <summary>Width a Fit band is assumed to have when converting column spans to pixel widths and back.</summary>
    public const double ReferenceWidth = 1200;

    /// <summary>
    /// Makes the layout consistent: derives one when missing, places unplaced visuals, empties items that
    /// reference unknown or already shown visuals, keeps one content per item, clamps positions and sizes,
    /// resolves overlaps and floats items up in grid bands, drops empty bands.
    /// </summary>
    public static ReportDefinition Normalize(ReportDefinition report)
    {
        var layout = report.Layout ?? new DashboardLayout();
        var columns = Math.Max(1, layout.Columns);
        var visualIds = report.Visuals.Select(v => v.Id).ToHashSet();
        var shown = new HashSet<string>();

        var bands = new List<LayoutBand>();
        foreach (var band in layout.Bands)
        {
            var items = band.Items.Select(source =>
            {
                var item = source;
                if (item.VisualId is { } id && (!visualIds.Contains(id) || !shown.Add(id)))
                    item = item with { VisualId = null };
                // One content per item: a visual wins over a text, a text over an image
                if (item.VisualId is not null) item = item with { Text = null, Image = null };
                else if (item.Text is not null) item = item with { Image = null };
                return Clamp(item, band.Mode, columns);
            }).ToList();
            if (items.Count > 0)
                bands.Add(band with { Items = Arranged(band.Mode, items, null) });
        }

        var unplaced = report.Visuals.Where(v => !shown.Contains(v.Id)).ToList();
        if (unplaced.Count > 0)
        {
            var target = bands.LastOrDefault() is { Mode: BandMode.Fit } last ? last : new LayoutBand();
            var span = Math.Min(DefaultColumnSpan, columns);
            var perRow = Math.Max(1, columns / span);
            var bottom = Bottom(target.Items);
            var added = unplaced.Select((visual, index) => new LayoutItem
            {
                VisualId = visual.Id,
                Column = index % perRow * span,
                Row = bottom + index / perRow * DefaultRowSpan,
                ColumnSpan = span,
                RowSpan = DefaultRowSpan,
            });
            var filled = target with { Items = Arranged(BandMode.Fit, [.. target.Items, .. added], null) };
            if (ReferenceEquals(target, bands.LastOrDefault())) bands[^1] = filled;
            else bands.Add(filled);
        }

        if (bands.Count == 0)
            bands.Add(new LayoutBand { Items = [EmptyItem(columns)] });

        return report with
        {
            Layout = layout with { Columns = columns, RowHeight = Math.Max(MinRowHeight, layout.RowHeight), Bands = bands },
        };
    }

    /// <summary>The visual shown by an item, if any.</summary>
    public static VisualDefinition? VisualOf(ReportDefinition report, LayoutItem item) =>
        item.VisualId is { } id ? report.Visuals.FirstOrDefault(v => v.Id == id) : null;

    /// <summary>The band holding an item.</summary>
    public static LayoutBand? BandOf(ReportDefinition report, string itemId) =>
        report.Layout?.Bands.FirstOrDefault(b => b.Items.Any(i => i.Id == itemId));

    // --- Adding ---

    /// <summary>
    /// Adds an empty item to the right of (or below) another one. In a grid band, "right" needs two free
    /// columns, otherwise the item goes below; a full-row item has no right side. In a scrolling or wrapping
    /// band, the new item comes right after the other one.
    /// </summary>
    public static ReportDefinition AddItemNextTo(ReportDefinition report, string itemId, ItemSide side, string? newItemId = null)
    {
        report = Normalize(report);
        var columns = report.Layout!.Columns;
        if (BandOf(report, itemId) is not { } band) return report;
        var anchor = band.Items.First(i => i.Id == itemId);
        var id = newItemId ?? Guid.NewGuid().ToString("N");

        if (band.Mode != BandMode.Fit)
        {
            var item = new LayoutItem { Id = id, RowSpan = anchor.RowSpan };
            var index = band.Items.ToList().IndexOf(anchor) + 1;
            return WithBand(report, band with { Items = Insert(band.Items, index, item) }, null);
        }

        var right = anchor.Column + anchor.ColumnSpan;
        var gridItem = side == ItemSide.Right && !anchor.IsFullRow && columns - right >= 2
            ? new LayoutItem { Id = id, Column = right, Row = anchor.Row, ColumnSpan = Math.Min(DefaultColumnSpan, columns - right), RowSpan = anchor.RowSpan }
            // Below, or to the right without room: start a new line under the anchor
            : new LayoutItem { Id = id, Column = anchor.Column, Row = anchor.Row + anchor.RowSpan, ColumnSpan = anchor.ColumnSpan, RowSpan = DefaultRowSpan };
        return WithBand(report, band with { Items = [.. band.Items, gridItem] }, id);
    }

    /// <summary>Adds an empty item at the end of the last band.</summary>
    public static ReportDefinition AddItemAtEnd(ReportDefinition report, string? newItemId = null)
    {
        report = Normalize(report);
        var band = report.Layout!.Bands[^1];
        var item = new LayoutItem
        {
            Id = newItemId ?? Guid.NewGuid().ToString("N"),
            Row = Bottom(band.Items),
            ColumnSpan = Math.Min(DefaultColumnSpan, report.Layout.Columns),
        };
        return WithBand(report, band with { Items = [.. band.Items, item] }, item.Id);
    }

    /// <summary>Inserts a band holding one empty item at <paramref name="index"/>.</summary>
    public static ReportDefinition InsertBand(ReportDefinition report, int index, BandMode mode = BandMode.Fit)
    {
        report = Normalize(report);
        var band = new LayoutBand { Mode = mode, Items = [EmptyItem(report.Layout!.Columns)] };
        return WithBands(report, Insert(report.Layout.Bands, index, band), null);
    }

    // --- Bands ---

    /// <summary>Removes a band and the visuals it shows.</summary>
    public static ReportDefinition RemoveBand(ReportDefinition report, string bandId)
    {
        report = Normalize(report);
        var visualIds = report.Layout!.Bands.Where(b => b.Id == bandId).SelectMany(b => b.Items).Select(i => i.VisualId).ToHashSet();
        report = report with { Visuals = report.Visuals.Where(v => !visualIds.Contains(v.Id)).ToArray() };
        return WithBands(report, report.Layout!.Bands.Where(b => b.Id != bandId).ToArray(), null);
    }

    /// <summary>
    /// Changes how a band lays out its items. From a grid, items keep their order (top to bottom, left to
    /// right) and get a width proportional to their column span; to a grid, they flow left to right with a
    /// column span proportional to their width. Pass the band's actual width and the gap between items so
    /// that items keep their size on screen.
    /// </summary>
    public static ReportDefinition SetBandMode(ReportDefinition report, string bandId, BandMode mode, double bandWidth = ReferenceWidth, double gap = 0)
    {
        bandWidth = bandWidth > 0 ? bandWidth : ReferenceWidth;
        report = Normalize(report);
        var columns = report.Layout!.Columns;
        if (report.Layout.Bands.FirstOrDefault(b => b.Id == bandId) is not { } band || band.Mode == mode) return report;

        // Same geometry as the grid: a span of s columns is s column widths plus the s - 1 gaps between them
        var columnWidth = Math.Max(1, (bandWidth - (columns - 1) * gap) / columns);
        IReadOnlyList<LayoutItem> items;
        if (band.Mode == BandMode.Fit)
        {
            items = band.Items.OrderBy(i => i.Row).ThenBy(i => i.Column)
                .Select(i => i with { Width = Math.Max(MinWidth, Math.Round(i.ColumnSpan * columnWidth + (i.ColumnSpan - 1) * gap)) })
                .ToArray();
        }
        else if (mode == BandMode.Fit)
        {
            var flowed = new List<LayoutItem>();
            int column = 0, row = 0, lineHeight = 0;
            foreach (var item in band.Items)
            {
                var span = item.IsFullRow ? columns : Math.Clamp((int)Math.Round((item.Width + gap) / (columnWidth + gap)), 1, columns);
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
            items = flowed;
        }
        else
        {
            items = band.Items; // Scroll <-> Wrap: same order and widths
        }
        return WithBand(report, band with { Mode = mode, Items = items }, null);
    }

    // --- Moving and resizing ---

    /// <summary>Moves an item to a cell of a grid band; the items it lands on move down.</summary>
    public static ReportDefinition MoveItem(ReportDefinition report, string itemId, string bandId, int column, int row) =>
        Move(report, itemId, bandId, (target, item) =>
            [.. target.Items.Where(i => i.Id != itemId), item with { Column = column, Row = row }]);

    /// <summary>
    /// Moves an item to position <paramref name="index"/> of a scrolling or wrapping band. The index refers to
    /// the band as it is before the move, so "insert before the item at index i" works within one band too.
    /// </summary>
    public static ReportDefinition MoveItemTo(ReportDefinition report, string itemId, string bandId, int index) =>
        Move(report, itemId, bandId, (target, item) =>
        {
            // The moved item is first replaced by a placeholder so that indexes stay those of the original band
            var items = target.Items.Select(i => i.Id == itemId ? null : i).ToList();
            items.Insert(Math.Clamp(index, 0, items.Count), item);
            return items.OfType<LayoutItem>().ToArray();
        });

    /// <summary>Changes the columns and rows an item covers in a grid band; the items it now overlaps move down.</summary>
    public static ReportDefinition ResizeItem(ReportDefinition report, string itemId, int columnSpan, int rowSpan) =>
        UpdateItem(report, itemId, i => i with { ColumnSpan = columnSpan, RowSpan = rowSpan });

    /// <summary>Changes the width and height of an item in a scrolling or wrapping band.</summary>
    public static ReportDefinition ResizeItemWidth(ReportDefinition report, string itemId, double width, int rowSpan) =>
        UpdateItem(report, itemId, i => i with { Width = width, RowSpan = rowSpan });

    /// <summary>
    /// Makes an item take the whole width of its band. Turned off, the item keeps its current size and
    /// can then be narrowed to make room for others.
    /// </summary>
    public static ReportDefinition SetFullRow(ReportDefinition report, string itemId, bool isFullRow) =>
        UpdateItem(report, itemId, i => isFullRow
            ? i with { IsFullRow = true }
            : i with { IsFullRow = false, ColumnSpan = i.IsFullRow ? report.Layout?.Columns ?? i.ColumnSpan : i.ColumnSpan });

    /// <summary>
    /// Gives up the full row of an item to add an empty item to its right: in a grid band the item keeps
    /// half of the columns and the new item takes the other half; in other bands the new item follows it.
    /// </summary>
    public static ReportDefinition SplitFullRow(ReportDefinition report, string itemId, string? newItemId = null)
    {
        report = Normalize(report);
        var columns = report.Layout!.Columns;
        var half = Math.Max(1, columns - Math.Min(DefaultColumnSpan, columns / 2));
        report = UpdateItem(report, itemId, i => i with
        {
            IsFullRow = false,
            ColumnSpan = BandOf(report, itemId)?.Mode == BandMode.Fit ? half : i.ColumnSpan,
        });
        return AddItemNextTo(report, itemId, ItemSide.Right, newItemId);
    }

    // --- Content ---

    /// <summary>Removes an item and the visual it shows.</summary>
    public static ReportDefinition RemoveItem(ReportDefinition report, string itemId)
    {
        report = Normalize(report);
        var visualId = Items(report).FirstOrDefault(i => i.Id == itemId)?.VisualId;
        report = report with { Visuals = report.Visuals.Where(v => v.Id != visualId).ToArray() };
        return WithBands(report, report.Layout!.Bands.Select(b => b with { Items = b.Items.Where(i => i.Id != itemId).ToArray() }).ToArray(), null);
    }

    /// <summary>Shows <paramref name="visual"/> in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetVisual(ReportDefinition report, string itemId, VisualDefinition visual)
    {
        report = Normalize(report);
        if (Items(report).FirstOrDefault(i => i.Id == itemId) is not { } item) return report;
        if (item.VisualId is { } previous && previous != visual.Id)
            report = report with { Visuals = report.Visuals.Where(v => v.Id != previous).ToArray() };

        var visuals = report.Visuals.Any(v => v.Id == visual.Id)
            ? report.Visuals.Select(v => v.Id == visual.Id ? visual : v).ToArray()
            : [.. report.Visuals, visual];
        // Visuals and item change together: normalizing in between would place the new visual elsewhere
        return WithBands(report with { Visuals = visuals }, MapItems(report, i =>
            i.Id == itemId ? i with { VisualId = visual.Id, Text = null, Image = null }
            : i.VisualId == visual.Id ? i with { VisualId = null }
            : i), null);
    }

    /// <summary>Shows a text in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetText(ReportDefinition report, string itemId, TextContent text) =>
        SetContent(report, itemId, i => i with { VisualId = null, Text = text, Image = null });

    /// <summary>Shows an image in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetImage(ReportDefinition report, string itemId, ImageContent image) =>
        SetContent(report, itemId, i => i with { VisualId = null, Text = null, Image = image });

    // --- Helpers ---

    private static ReportDefinition SetContent(ReportDefinition report, string itemId, Func<LayoutItem, LayoutItem> change)
    {
        report = Normalize(report);
        var visualId = Items(report).FirstOrDefault(i => i.Id == itemId)?.VisualId;
        report = report with { Visuals = report.Visuals.Where(v => v.Id != visualId).ToArray() };
        return WithBands(report, MapItems(report, i => i.Id == itemId ? change(i) : i), null);
    }

    private static ReportDefinition Move(
        ReportDefinition report, string itemId, string bandId, Func<LayoutBand, LayoutItem, IReadOnlyList<LayoutItem>> insert)
    {
        report = Normalize(report);
        if (Items(report).FirstOrDefault(i => i.Id == itemId) is not { } item
            || report.Layout!.Bands.FirstOrDefault(b => b.Id == bandId) is not { } target) return report;

        // The item leaves every other band; the target band places it (and drops its previous copy)
        var bands = report.Layout.Bands.Select(b =>
            b.Id == target.Id ? b with { Items = insert(b, item) }
            : b with { Items = b.Items.Where(i => i.Id != itemId).ToArray() }).ToArray();
        return WithBands(report, bands, itemId);
    }

    private static ReportDefinition UpdateItem(ReportDefinition report, string itemId, Func<LayoutItem, LayoutItem> change)
    {
        report = Normalize(report);
        return WithBands(report, MapItems(report, i => i.Id == itemId ? change(i) : i), itemId);
    }

    private static IEnumerable<LayoutItem> Items(ReportDefinition report) => report.Layout!.Bands.SelectMany(b => b.Items);

    private static LayoutBand[] MapItems(ReportDefinition report, Func<LayoutItem, LayoutItem> change) =>
        report.Layout!.Bands.Select(b => b with { Items = b.Items.Select(change).ToArray() }).ToArray();

    private static ReportDefinition WithBand(ReportDefinition report, LayoutBand band, string? priorityId) =>
        WithBands(report, report.Layout!.Bands.Select(b => b.Id == band.Id ? band : b).ToArray(), priorityId);

    /// <summary>Applies new bands; the band holding <paramref name="priorityId"/> is arranged with that item first.</summary>
    private static ReportDefinition WithBands(ReportDefinition report, IReadOnlyList<LayoutBand> bands, string? priorityId)
    {
        var columns = report.Layout!.Columns;
        var arranged = bands.Select(b => priorityId is not null && b.Mode == BandMode.Fit && b.Items.Any(i => i.Id == priorityId)
            ? b with { Items = Arranged(BandMode.Fit, b.Items.Select(i => Clamp(i, BandMode.Fit, columns)).ToList(), priorityId) }
            : b).ToArray();
        return Normalize(report with { Layout = report.Layout with { Bands = arranged } });
    }

    private static IReadOnlyList<LayoutItem> Arranged(BandMode mode, List<LayoutItem> items, string? priorityId) =>
        mode == BandMode.Fit ? Arrange(items, priorityId) : items;

    /// <summary>
    /// Removes overlaps and gaps in a grid band. The priority item (just moved, resized or added) keeps its
    /// place first; the others, from top to bottom, are pushed below what they overlap. Then every item floats
    /// up as long as nothing is above it, without ever jumping over another item.
    /// </summary>
    private static IReadOnlyList<LayoutItem> Arrange(List<LayoutItem> items, string? priorityId)
    {
        var placed = new List<LayoutItem>();
        var priority = priorityId is null ? null : items.FirstOrDefault(i => i.Id == priorityId);
        if (priority is not null) placed.Add(priority);
        foreach (var item in items.Where(i => !ReferenceEquals(i, priority)).OrderBy(i => i.Row).ThenBy(i => i.Column))
        {
            var row = item.Row;
            while (Collides(item with { Row = row }, placed)) row++;
            placed.Add(item with { Row = row });
        }

        var compacted = new List<LayoutItem>();
        foreach (var item in placed.OrderBy(i => i.Row).ThenBy(i => i.Column))
        {
            var row = item.Row;
            while (row > 0 && !Collides(item with { Row = row - 1 }, compacted)) row--;
            compacted.Add(item with { Row = row });
        }
        // Keep the original order of the items (it is the save order)
        return items.Select(i => compacted.First(c => c.Id == i.Id)).ToArray();
    }

    private static bool Collides(LayoutItem item, IEnumerable<LayoutItem> others) => others.Any(o =>
        o.Id != item.Id
        && item.Column < o.Column + o.ColumnSpan && o.Column < item.Column + item.ColumnSpan
        && item.Row < o.Row + o.RowSpan && o.Row < item.Row + item.RowSpan);

    private static int Bottom(IEnumerable<LayoutItem> items) => items.Select(i => i.Row + i.RowSpan).DefaultIfEmpty(0).Max();

    private static LayoutItem EmptyItem(int columns) => new() { ColumnSpan = Math.Min(DefaultColumnSpan, columns) };

    private static LayoutItem Clamp(LayoutItem item, BandMode mode, int columns)
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
        return list.ToArray();
    }
}
