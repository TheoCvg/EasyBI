using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Where a new item goes relative to an existing one.</summary>
public enum ItemSide { Right, Below }

/// <summary>
/// Dashboard grid operations. Each one returns a new <see cref="ReportDefinition"/> whose visuals and layout
/// stay consistent: items stay inside the grid and never overlap (an item moved or resized pushes the others
/// down, then everything floats up into free space), every visual is shown by exactly one item, items only
/// reference existing visuals, removing an item removes its visual, and there is always at least one item.
/// </summary>
public static class ReportLayout
{
    public const int DefaultColumnSpan = 6;
    public const int DefaultRowSpan = 3;
    public const double MinRowHeight = 40;

    /// <summary>
    /// Makes the layout consistent: derives one when missing, places unplaced visuals at the bottom, empties
    /// items that reference unknown or already shown visuals, keeps one content per item, clamps positions
    /// and spans to the grid, resolves overlaps and floats items up.
    /// </summary>
    public static ReportDefinition Normalize(ReportDefinition report)
    {
        var layout = report.Layout ?? new DashboardLayout();
        var columns = Math.Max(1, layout.Columns);
        var visualIds = report.Visuals.Select(v => v.Id).ToHashSet();
        var shown = new HashSet<string>();
        var items = new List<LayoutItem>();
        foreach (var source in layout.Items)
        {
            var item = source;
            if (item.VisualId is { } id && (!visualIds.Contains(id) || !shown.Add(id)))
                item = item with { VisualId = null };
            // One content per item: a visual wins over a text, a text over an image
            if (item.VisualId is not null) item = item with { Text = null, Image = null };
            else if (item.Text is not null) item = item with { Image = null };
            items.Add(Clamp(item, columns));
        }

        var span = Math.Min(DefaultColumnSpan, columns);
        var perRow = Math.Max(1, columns / span);
        var bottom = Bottom(items);
        var index = 0;
        foreach (var visual in report.Visuals.Where(v => !shown.Contains(v.Id)))
        {
            items.Add(new LayoutItem
            {
                VisualId = visual.Id,
                Column = index % perRow * span,
                Row = bottom + index / perRow * DefaultRowSpan,
                ColumnSpan = span,
                RowSpan = DefaultRowSpan,
            });
            index++;
        }
        if (items.Count == 0)
            items.Add(new LayoutItem { ColumnSpan = span, RowSpan = DefaultRowSpan });

        return report with
        {
            Layout = layout with
            {
                Columns = columns,
                RowHeight = Math.Max(MinRowHeight, layout.RowHeight),
                Items = Arrange(items, priorityId: null),
            },
        };
    }

    /// <summary>The visual shown by an item, if any.</summary>
    public static VisualDefinition? VisualOf(ReportDefinition report, LayoutItem item) =>
        item.VisualId is { } id ? report.Visuals.FirstOrDefault(v => v.Id == id) : null;

    /// <summary>Adds <paramref name="item"/> where it asks to be; the items it overlaps move down.</summary>
    public static ReportDefinition AddItem(ReportDefinition report, LayoutItem item)
    {
        report = Normalize(report);
        return WithItems(report, [.. report.Layout!.Items, Clamp(item, report.Layout.Columns)], item.Id);
    }

    /// <summary>Adds an empty item to the right of (or below) another one, with the same height (or width).</summary>
    public static ReportDefinition AddItemNextTo(ReportDefinition report, string itemId, ItemSide side, string? newItemId = null)
    {
        report = Normalize(report);
        var layout = report.Layout!;
        if (layout.Items.FirstOrDefault(i => i.Id == itemId) is not { } anchor) return report;

        var right = anchor.Column + anchor.ColumnSpan;
        var item = side == ItemSide.Right && layout.Columns - right >= 2
            ? new LayoutItem { Column = right, Row = anchor.Row, ColumnSpan = Math.Min(DefaultColumnSpan, layout.Columns - right), RowSpan = anchor.RowSpan }
            // Below, or to the right without room: start a new line under the anchor
            : new LayoutItem { Column = anchor.Column, Row = anchor.Row + anchor.RowSpan, ColumnSpan = anchor.ColumnSpan, RowSpan = DefaultRowSpan };
        return AddItem(report, newItemId is null ? item : item with { Id = newItemId });
    }

    /// <summary>Adds an empty item under all the others.</summary>
    public static ReportDefinition AddItemAtEnd(ReportDefinition report, string? newItemId = null)
    {
        report = Normalize(report);
        var item = new LayoutItem
        {
            Row = Bottom(report.Layout!.Items),
            ColumnSpan = Math.Min(DefaultColumnSpan, report.Layout.Columns),
            RowSpan = DefaultRowSpan,
        };
        return AddItem(report, newItemId is null ? item : item with { Id = newItemId });
    }

    /// <summary>Moves an item to a cell; the items it lands on move down.</summary>
    public static ReportDefinition MoveItem(ReportDefinition report, string itemId, int column, int row) =>
        UpdateItem(report, itemId, i => i with { Column = column, Row = row });

    /// <summary>Changes the number of columns and rows an item covers; the items it now overlaps move down.</summary>
    public static ReportDefinition ResizeItem(ReportDefinition report, string itemId, int columnSpan, int rowSpan) =>
        UpdateItem(report, itemId, i => i with { ColumnSpan = columnSpan, RowSpan = rowSpan });

    /// <summary>Removes an item and the visual it shows.</summary>
    public static ReportDefinition RemoveItem(ReportDefinition report, string itemId)
    {
        report = Normalize(report);
        var visualId = report.Layout!.Items.FirstOrDefault(i => i.Id == itemId)?.VisualId;
        report = report with { Visuals = report.Visuals.Where(v => v.Id != visualId).ToArray() };
        return WithItems(report, report.Layout!.Items.Where(i => i.Id != itemId).ToList(), null);
    }

    /// <summary>Shows <paramref name="visual"/> in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetVisual(ReportDefinition report, string itemId, VisualDefinition visual)
    {
        report = Normalize(report);
        if (report.Layout!.Items.FirstOrDefault(i => i.Id == itemId) is not { } item) return report;
        if (item.VisualId is { } previous && previous != visual.Id)
            report = report with { Visuals = report.Visuals.Where(v => v.Id != previous).ToArray() };

        var visuals = report.Visuals.Any(v => v.Id == visual.Id)
            ? report.Visuals.Select(v => v.Id == visual.Id ? visual : v).ToArray()
            : [.. report.Visuals, visual];
        // Visuals and item change together: normalizing in between would place the new visual elsewhere
        var items = report.Layout.Items.Select(i =>
            i.Id == itemId ? i with { VisualId = visual.Id, Text = null, Image = null }
            : i.VisualId == visual.Id ? i with { VisualId = null }
            : i).ToList();
        return WithItems(report with { Visuals = visuals }, items, null);
    }

    /// <summary>Shows a text in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetText(ReportDefinition report, string itemId, TextContent text) =>
        SetContent(report, itemId, i => i with { VisualId = null, Text = text, Image = null });

    /// <summary>Shows an image in an item, replacing (and removing) what it showed.</summary>
    public static ReportDefinition SetImage(ReportDefinition report, string itemId, ImageContent image) =>
        SetContent(report, itemId, i => i with { VisualId = null, Text = null, Image = image });

    private static ReportDefinition SetContent(ReportDefinition report, string itemId, Func<LayoutItem, LayoutItem> change)
    {
        report = Normalize(report);
        var visualId = report.Layout!.Items.FirstOrDefault(i => i.Id == itemId)?.VisualId;
        report = report with { Visuals = report.Visuals.Where(v => v.Id != visualId).ToArray() };
        return WithItems(report, report.Layout!.Items.Select(i => i.Id == itemId ? change(i) : i).ToList(), null);
    }

    private static ReportDefinition UpdateItem(ReportDefinition report, string itemId, Func<LayoutItem, LayoutItem> change)
    {
        report = Normalize(report);
        var columns = report.Layout!.Columns;
        var items = report.Layout.Items.Select(i => i.Id == itemId ? Clamp(change(i), columns) : i).ToList();
        return WithItems(report, items, itemId);
    }

    private static ReportDefinition WithItems(ReportDefinition report, List<LayoutItem> items, string? priorityId) =>
        Normalize(report with { Layout = report.Layout! with { Items = Arrange(items, priorityId) } });

    /// <summary>
    /// Removes overlaps and gaps. The priority item (just moved, resized or added) keeps its place first;
    /// the others, from top to bottom, are pushed below what they overlap. Then every item floats up as long
    /// as nothing is above it, without ever jumping over another item.
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

    private static LayoutItem Clamp(LayoutItem item, int columns)
    {
        var span = Math.Clamp(item.ColumnSpan, 1, columns);
        return item with
        {
            ColumnSpan = span,
            RowSpan = Math.Max(1, item.RowSpan),
            Column = Math.Clamp(item.Column, 0, columns - span),
            Row = Math.Max(0, item.Row),
        };
    }
}
