using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Where an item dropped among others goes, and where to draw the insertion line. Pure geometry on the bounds of the
/// items in display order, so that lists, shelves and dashboard bands share the same rules.
/// <see cref="Rect.Empty"/> bounds (an item being dragged, an item not displayed) are skipped.
/// </summary>
public static class InsertionIndex
{
    /// <summary>
    /// Index before which an item dropped at <paramref name="point"/> goes.
    /// <see cref="Orientation.Horizontal"/>: items read line by line, left to right (a wrapping flow);
    /// <see cref="Orientation.Vertical"/>: items stacked top to bottom.
    /// With <paramref name="singleLine"/>, horizontal items are all on one line (a scrolling band): only X counts.
    /// </summary>
    public static int At(IReadOnlyList<Rect> items, Point point, Orientation orientation = Orientation.Horizontal, bool singleLine = false)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var bounds = items[i];
            if (bounds.IsEmpty) continue;
            bool before;
            if (orientation == Orientation.Vertical)
            {
                before = point.Y < bounds.Top + bounds.Height / 2;
            }
            else
            {
                var sameLine = singleLine || (point.Y >= bounds.Top && point.Y <= bounds.Bottom);
                before = sameLine ? point.X < bounds.Left + bounds.Width / 2 : point.Y < bounds.Top;
            }
            if (before) return i;
        }
        return items.Count;
    }

    /// <summary>
    /// Bounds of the line showing an insertion before <paramref name="index"/> (after the last item when it equals the
    /// count): between two items of the same line, or along the edge of the item it touches. Empty without items.
    /// </summary>
    public static Rect IndicatorBounds(IReadOnlyList<Rect> items, int index, Orientation orientation = Orientation.Horizontal, double thickness = 2)
    {
        var shown = items.Where(r => !r.IsEmpty).ToList();
        if (shown.Count == 0) return Rect.Empty;
        // Index among the displayed items
        var at = Math.Clamp(items.Take(Math.Clamp(index, 0, items.Count)).Count(r => !r.IsEmpty), 0, shown.Count);
        var previous = at > 0 ? shown[at - 1] : (Rect?)null;
        var next = at < shown.Count ? shown[at] : (Rect?)null;

        if (orientation == Orientation.Vertical)
        {
            var reference = next ?? previous!.Value;
            var y = (previous, next) switch
            {
                ({ } p, { } n) => (p.Bottom + n.Top) / 2,
                (null, { } n) => n.Top,
                _ => previous!.Value.Bottom,
            };
            return new Rect(reference.Left, y - thickness / 2, reference.Width, thickness);
        }
        else
        {
            var reference = next ?? previous!.Value;
            var x = (previous, next) switch
            {
                ({ } p, { } n) when OnSameLine(p, n) => (p.Right + n.Left) / 2,
                (_, { } n) => n.Left,
                _ => previous!.Value.Right,
            };
            return new Rect(x - thickness / 2, reference.Top, thickness, reference.Height);
        }
    }

    private static bool OnSameLine(Rect a, Rect b) => a.Top < b.Bottom && b.Top < a.Bottom;
}
