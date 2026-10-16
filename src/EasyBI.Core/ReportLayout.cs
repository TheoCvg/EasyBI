using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Dashboard layout operations. Each one returns a new <see cref="ReportDefinition"/> whose visuals and
/// layout stay consistent: every visual is placed in exactly one slot, slots only reference existing
/// visuals, there is always at least one row with one slot, and removing a slot removes its visual.
/// </summary>
public static class ReportLayout
{
    public const double MinWidth = 160;
    public const double MinHeight = 120;

    /// <summary>
    /// Makes the layout consistent: derives one when missing (<paramref name="columns"/> visuals per row),
    /// appends unplaced visuals, empties slots that reference unknown or already placed visuals,
    /// drops empty rows and clamps sizes.
    /// </summary>
    public static ReportDefinition Normalize(ReportDefinition report, int columns = 2)
    {
        var visualIds = report.Visuals.Select(v => v.Id).ToHashSet();
        var placed = new HashSet<string>();
        var rows = new List<LayoutRow>();
        foreach (var row in report.Layout?.Rows ?? [])
        {
            var slots = row.Slots
                .Select(s => s.VisualId is { } id && (!visualIds.Contains(id) || !placed.Add(id)) ? s with { VisualId = null } : s)
                .Select(s => s.Width < MinWidth ? s with { Width = MinWidth } : s)
                .ToArray();
            if (slots.Length > 0)
                rows.Add(row with { Slots = slots, Height = Math.Max(MinHeight, row.Height) });
        }

        foreach (var chunk in report.Visuals.Where(v => !placed.Contains(v.Id)).Chunk(Math.Max(1, columns)))
            rows.Add(new LayoutRow { Slots = chunk.Select(v => new LayoutSlot { VisualId = v.Id }).ToArray() });
        if (rows.Count == 0)
            rows.Add(EmptyRow());

        return report with { Layout = new DashboardLayout { Rows = rows } };
    }

    /// <summary>The visual displayed in a slot, if any.</summary>
    public static VisualDefinition? VisualOf(ReportDefinition report, LayoutSlot slot) =>
        slot.VisualId is { } id ? report.Visuals.FirstOrDefault(v => v.Id == id) : null;

    /// <summary>Inserts an empty slot in a row; in a <see cref="RowOverflow.Fit"/> row it gets an average share.</summary>
    public static ReportDefinition InsertSlot(ReportDefinition report, string rowId, int index) =>
        UpdateRows(report, rows => rows.Select(row =>
        {
            if (row.Id != rowId) return row;
            var width = row.Overflow == RowOverflow.Fit && row.Slots.Count > 0 ? row.Slots.Average(s => s.Width) : LayoutSlot.DefaultWidth;
            return row with { Slots = Insert(row.Slots, index, new LayoutSlot { Width = width }) };
        }));

    /// <summary>Inserts a row holding one empty slot at <paramref name="index"/>.</summary>
    public static ReportDefinition InsertRow(ReportDefinition report, int index) =>
        UpdateRows(report, rows => Insert(rows.ToArray(), index, EmptyRow()));

    /// <summary>Removes a slot and the visual it displays.</summary>
    public static ReportDefinition RemoveSlot(ReportDefinition report, string slotId)
    {
        report = Normalize(report);
        var visualId = Slots(report).FirstOrDefault(s => s.Id == slotId)?.VisualId;
        return UpdateRows(WithoutVisual(report, visualId), rows => rows.Select(row => row with
        {
            Slots = row.Slots.Where(s => s.Id != slotId).ToArray(),
        }));
    }

    /// <summary>Removes a row and the visuals it displays.</summary>
    public static ReportDefinition RemoveRow(ReportDefinition report, string rowId)
    {
        report = Normalize(report);
        var visualIds = report.Layout!.Rows.Where(r => r.Id == rowId).SelectMany(r => r.Slots).Select(s => s.VisualId).ToHashSet();
        report = report with { Visuals = report.Visuals.Where(v => !visualIds.Contains(v.Id)).ToArray() };
        return UpdateRows(report, rows => rows.Where(r => r.Id != rowId));
    }

    /// <summary>
    /// Moves a slot to position <paramref name="index"/> of a row. The index refers to the row as it is
    /// before the move, so "insert before the slot at index i" works the same within one row and across rows.
    /// </summary>
    public static ReportDefinition MoveSlot(ReportDefinition report, string slotId, string targetRowId, int index)
    {
        report = Normalize(report);
        if (Slots(report).FirstOrDefault(s => s.Id == slotId) is not { } moved) return report;
        return UpdateRows(report, rows => rows.Select(row =>
        {
            // The moved slot is first replaced by a placeholder so that indexes stay those of the original row
            var slots = row.Slots.Select(s => s.Id == slotId ? null : s).ToList();
            if (row.Id == targetRowId) slots.Insert(Math.Clamp(index, 0, slots.Count), moved);
            return row with { Slots = slots.OfType<LayoutSlot>().ToArray() };
        }));
    }

    /// <summary>Moves a slot into a new row inserted at <paramref name="rowIndex"/> (index before the move).</summary>
    public static ReportDefinition MoveSlotToNewRow(ReportDefinition report, string slotId, int rowIndex)
    {
        report = Normalize(report);
        if (Slots(report).FirstOrDefault(s => s.Id == slotId) is not { } moved) return report;
        return UpdateRows(report, rows =>
        {
            var remaining = rows.Select(r => r with { Slots = r.Slots.Where(s => s.Id != slotId).ToArray() }).ToArray();
            return Insert(remaining, rowIndex, new LayoutRow { Slots = [moved] });
        });
    }

    /// <summary>Displays <paramref name="visual"/> in a slot, replacing (and removing) the visual it showed.</summary>
    public static ReportDefinition SetVisual(ReportDefinition report, string slotId, VisualDefinition visual)
    {
        report = Normalize(report);
        if (Slots(report).FirstOrDefault(s => s.Id == slotId) is not { } slot) return report;
        if (slot.VisualId is { } previous && previous != visual.Id)
            report = WithoutVisual(report, previous);

        var visuals = report.Visuals.Any(v => v.Id == visual.Id)
            ? report.Visuals.Select(v => v.Id == visual.Id ? visual : v).ToArray()
            : [.. report.Visuals, visual];
        // Visuals and slot change together: normalizing in between would place the new visual elsewhere
        var rows = report.Layout!.Rows.Select(row => row with
        {
            Slots = row.Slots.Select(s => s.Id == slotId ? s with { VisualId = visual.Id }
                : s.VisualId == visual.Id ? s with { VisualId = null }
                : s).ToArray(),
        }).ToArray();
        return Normalize(report with { Visuals = visuals, Layout = new DashboardLayout { Rows = rows } });
    }

    public static ReportDefinition SetRowHeight(ReportDefinition report, string rowId, double height) =>
        UpdateRows(report, rows => rows.Select(r => r.Id == rowId ? r with { Height = Math.Max(MinHeight, height) } : r));

    public static ReportDefinition SetRowOverflow(ReportDefinition report, string rowId, RowOverflow overflow) =>
        UpdateRows(report, rows => rows.Select(r => r.Id == rowId ? r with { Overflow = overflow } : r));

    /// <summary>Sets the width of some slots of a row (missing ids keep their width).</summary>
    public static ReportDefinition SetSlotWidths(ReportDefinition report, string rowId, IReadOnlyDictionary<string, double> widths) =>
        UpdateRows(report, rows => rows.Select(row => row.Id != rowId ? row : row with
        {
            Slots = row.Slots.Select(s => widths.TryGetValue(s.Id, out var w) ? s with { Width = Math.Max(MinWidth, w) } : s).ToArray(),
        }));

    private static LayoutRow EmptyRow() => new() { Slots = [new LayoutSlot()] };

    private static IEnumerable<LayoutSlot> Slots(ReportDefinition report) => report.Layout!.Rows.SelectMany(r => r.Slots);

    private static ReportDefinition WithoutVisual(ReportDefinition report, string? visualId) =>
        visualId is null ? report : report with { Visuals = report.Visuals.Where(v => v.Id != visualId).ToArray() };

    private static ReportDefinition UpdateRows(ReportDefinition report, Func<IReadOnlyList<LayoutRow>, IEnumerable<LayoutRow>> update)
    {
        report = Normalize(report);
        return Normalize(report with { Layout = new DashboardLayout { Rows = update(report.Layout!.Rows).ToArray() } });
    }

    private static T[] Insert<T>(IReadOnlyList<T> items, int index, T item)
    {
        var list = items.ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), item);
        return list.ToArray();
    }
}
