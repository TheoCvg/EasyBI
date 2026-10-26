namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// A column tier of a <see cref="DashboardPanel"/>: below <see cref="BelowWidth"/>, grid bands show
/// <see cref="Columns"/> columns instead of the layout's, and their lines are adapted (see <see cref="BandArrangement"/>).
/// The layout itself does not change.
/// </summary>
public sealed class ColumnTier
{
    /// <summary>Width of the panel under which the tier applies, in device-independent pixels.</summary>
    public double BelowWidth { get; set; }

    /// <summary>Columns shown by grid bands (never more than the layout's).</summary>
    public int Columns { get; set; } = 1;

    /// <summary>One column under 600 pixels, six under 960; the layout's columns above.</summary>
    public static IReadOnlyList<ColumnTier> Defaults { get; } =
    [
        new() { BelowWidth = 600, Columns = 1 },
        new() { BelowWidth = 960, Columns = 6 },
    ];

    /// <summary>The columns grid bands show at a width: those of the narrowest tier the width is below, the layout's otherwise.</summary>
    public static int ColumnsFor(IEnumerable<ColumnTier> tiers, double width, int layoutColumns) =>
        tiers.Where(t => width < t.BelowWidth).OrderBy(t => t.BelowWidth)
            .Select(t => Compat.Clamp(t.Columns, 1, layoutColumns)).DefaultIfEmpty(layoutColumns).First();

    /// <summary>The lines a line of a grid band shows as in a column tier (<see cref="ArrangeSettings.IsTier"/>).</summary>
    internal static IEnumerable<LayoutNode> AdaptLine(LayoutNode line, ArrangeSettings settings) => Adapt(line, settings.Columns, settings);

    // A node in a vertical place (a line of the band, a child of a stack) of `slot` columns: the lines it shows as
    private static IEnumerable<LayoutNode> Adapt(LayoutNode node, int slot, ArrangeSettings settings)
    {
        if (node.IsItem)
        {
            yield return node with { ColumnSpan = Scale(node.ColumnSpan, settings) };
            yield break;
        }
        if (node.Split == SplitDirection.Column)
        {
            yield return Stack(node, slot, settings) with { ColumnSpan = Scale(node.ColumnSpan, settings) };
            yield break;
        }
        var perLine = Math.Max(1, slot / ArrangeSettings.MinTierSpan);
        var children = node.Children;
        if (children.Count <= perLine)
        {
            yield return node with { ColumnSpan = Scale(node.ColumnSpan, settings), Children = Fit(children, slot, settings) };
            yield break;
        }
        // Too many items side by side: broken into lines, each child alone on its line taking the whole width
        for (var i = 0; i * perLine < children.Count; i++)
        {
            var chunk = children.Skip(i * perLine).Take(perLine).ToArray();
            yield return chunk.Length == 1
                ? Side(chunk[0] with { ColumnSpan = null, RowSpan = chunk[0].RowSpan ?? node.RowSpan }, slot, settings)
                : new LayoutNode { Id = $"{node.Id}#{i}", Split = SplitDirection.Row, RowSpan = node.RowSpan, Children = Fit(chunk, slot, settings) };
        }
    }

    // Children side by side in `slot` columns: all constrained, they keep their proportions and fill the slot;
    // otherwise the constrained ones are scaled and the others share the rest
    private static LayoutNode[] Fit(IReadOnlyList<LayoutNode> children, int slot, ArrangeSettings settings)
    {
        var spans = children.All(c => c.ColumnSpan is not null)
            ? Proportions(children.Select(c => c.ColumnSpan!.Value).ToArray(), slot).Select(s => (int?)s).ToArray()
            : children.Select(c => Scale(c.ColumnSpan, settings)).ToArray();
        return children.Select((c, i) => Side(c with { ColumnSpan = spans[i] }, spans[i] ?? slot / children.Count, settings)).ToArray();
    }

    // A child of a line, given its columns: a stack adapts its own children
    private static LayoutNode Side(LayoutNode child, int slot, ArrangeSettings settings) =>
        child.Split == SplitDirection.Column ? Stack(child, slot, settings) : child;

    private static LayoutNode Stack(LayoutNode stack, int slot, ArrangeSettings settings) =>
        stack with { Children = stack.Children.SelectMany(c => Adapt(c, Math.Max(1, slot), settings)).ToArray() };

    private static int? Scale(int? span, ArrangeSettings settings) =>
        span is { } value && settings.LayoutColumns is { } layout
            ? Compat.Clamp((int)Math.Round(value * (double)settings.Columns / layout), 1, settings.Columns)
            : span;

    /// <summary>Splits <paramref name="total"/> columns in proportion to <paramref name="weights"/>, at least one each (largest remainders first).</summary>
    internal static int[] Proportions(IReadOnlyList<int> weights, int total)
    {
        var sum = Math.Max(1, weights.Sum(w => Math.Max(1, w)));
        var exact = weights.Select(w => Math.Max(1, w) * (double)total / sum).ToArray();
        var spans = exact.Select(e => Math.Max(1, (int)Math.Floor(e))).ToArray();
        foreach (var i in Enumerable.Range(0, spans.Length).OrderByDescending(i => exact[i] - Math.Floor(exact[i])))
        {
            if (spans.Sum() >= total) break;
            spans[i]++;
        }
        return spans;
    }
}
