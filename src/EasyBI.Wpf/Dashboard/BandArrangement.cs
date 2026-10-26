using System.Windows;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Room left in a container whose children are all constrained; adding next to <paramref name="AnchorId"/> on <paramref name="Side"/> fills it.</summary>
public sealed record FreeSpace(Rect Bounds, string AnchorId, ItemSide Side);

/// <summary>Sizes a band is arranged with.</summary>
/// <param name="Width">Width of the band, in device-independent pixels.</param>
/// <param name="Columns">Columns of a grid band.</param>
/// <param name="RowHeight">Height of one row.</param>
/// <param name="Gap">Space between items.</param>
/// <param name="MinItemWidth">Width of unconstrained items of scrolling and wrapping bands, before they grow.</param>
/// <param name="HorizontalOffset">Scroll position of a scrolling band.</param>
/// <param name="LayoutColumns">
/// Columns the layout's spans are counted in, when the grid shows fewer <paramref name="Columns"/> (a column tier of a
/// narrow panel, see <see cref="DashboardPanel.ColumnTiers"/>); null when they are the same.
/// </param>
public sealed record ArrangeSettings(double Width, int Columns, double RowHeight, double Gap, double MinItemWidth, double HorizontalOffset = 0, int? LayoutColumns = null)
{
    /// <summary>Smallest width of an item side by side with others in a column tier, in columns of the tier.</summary>
    public const int MinTierSpan = 3;

    /// <summary>True when the grid shows fewer columns than the layout counts: lines are adapted (see <see cref="BandArrangement"/>).</summary>
    public bool IsTier => LayoutColumns is { } layout && layout > Columns;

    public double ColumnWidth => Math.Max(0, (Width - Gap * (Columns - 1)) / Columns);

    /// <summary>Left of a grid column.</summary>
    public double X(int column) => column * (ColumnWidth + Gap);

    /// <summary>Width of <paramref name="span"/> grid columns, gaps between them included.</summary>
    public double WidthOf(int span) => span <= 0 ? 0 : span * ColumnWidth + (span - 1) * Gap;

    /// <summary>Height of <paramref name="rows"/> rows, gaps between them included.</summary>
    public double HeightOf(int rows) => Math.Max(1, rows) * RowHeight + (Math.Max(1, rows) - 1) * Gap;
}

/// <summary>
/// Where the items of a band go (pure geometry, band coordinates: the band's top-left corner is the origin).
/// <list type="bullet">
/// <item>Grid band: lines are stacked. Along a container, constrained children take their size and the others share
/// the rest (whole columns across, pixels down); across it, every child takes the whole room. A line's height is the
/// largest constrained height of its items if any (the others stretch), otherwise the height its content prefers.</item>
/// <item>Scrolling and wrapping bands: items start at their width (the minimum width if unconstrained), lines break
/// when the next one does not fit (wrapping), then unconstrained items share the room left; in a scrolling band that
/// does not fit, items keep their width and the band scrolls.</item>
/// </list>
/// A container (or a line) whose children are all constrained and leave room reports it as a <see cref="FreeSpace"/>.
/// <para>
/// In a column tier (<see cref="ArrangeSettings.IsTier"/>), a grid band is adapted for display only: spans are scaled to
/// the tier's columns, a line whose items would get less than <see cref="ArrangeSettings.MinTierSpan"/> columns each is
/// broken into several lines (one item per line in a single column), and items that are all constrained keep their
/// proportions while filling their line. Items keep their ids, and each line of the layout reports one bound in
/// <see cref="Lines"/> (its broken lines together), so that line indexes still refer to the layout.
/// </para>
/// </summary>
public sealed class BandArrangement
{
    private readonly Dictionary<string, Rect> _items = [];
    private readonly Dictionary<string, (int Start, int Span)> _columns = [];
    private readonly List<FreeSpace> _free = [];
    private readonly List<Rect> _lines = [];
    private readonly ArrangeSettings _settings;

    private BandArrangement(ArrangeSettings settings) => _settings = settings;

    /// <summary>Bounds of each item, by node id.</summary>
    public IReadOnlyDictionary<string, Rect> Items => _items;

    /// <summary>Grid columns covered by each node of a grid band (containers included), by node id.</summary>
    public IReadOnlyDictionary<string, (int Start, int Span)> Columns => _columns;

    public IReadOnlyList<FreeSpace> FreeSpaces => _free;

    /// <summary>Bounds of the lines: the grid band's lines, or the lines a wrapping band broke its items into.</summary>
    public IReadOnlyList<Rect> Lines => _lines;

    public double Height { get; private set; }

    /// <summary>Width of the content: wider than the band when a scrolling band scrolls.</summary>
    public double ExtentWidth { get; private set; }

    public static BandArrangement Arrange(BandLayout band, ArrangeSettings settings)
    {
        var arrangement = new BandArrangement(settings);
        if (band.Items.Count == 0)
        {
            arrangement.Height = settings.RowHeight;
            arrangement.ExtentWidth = settings.Width;
        }
        else if (band.Mode == BandMode.Fit)
        {
            arrangement.ArrangeGrid(band.Items);
        }
        else
        {
            arrangement.ArrangeFlow(band.Items.SelectMany(i => i.Items()).ToArray(), band.Mode == BandMode.Wrap);
        }
        return arrangement;
    }

    /// <summary>
    /// Splits <paramref name="total"/> columns: constrained children take theirs (reduced from the last one if the
    /// others would not get a column each), unconstrained ones share the rest, the first ones taking the remainder.
    /// </summary>
    public static int[] Distribute(int total, IReadOnlyList<int?> constraints)
    {
        var spans = constraints.Select(c => c is { } span ? Math.Max(1, span) : 0).ToArray();
        var free = constraints.Count(c => c is null);
        for (var i = spans.Length - 1; i >= 0 && spans.Sum() > total - free; i--)
            if (constraints[i] is not null) spans[i] = Math.Max(1, spans[i] - (spans.Sum() - (total - free)));
        if (free > 0)
        {
            var rest = Math.Max(0, total - spans.Sum());
            var share = rest / free;
            var extra = rest % free;
            for (int i = 0, k = 0; i < spans.Length; i++)
                if (constraints[i] is null) spans[i] = share + (k++ < extra ? 1 : 0);
        }
        // More children than columns: the last ones get nothing
        for (int i = 0, left = total; i < spans.Length; i++)
        {
            spans[i] = Math.Min(spans[i], left);
            left -= spans[i];
        }
        return spans;
    }

    // --- Grid ---

    private void ArrangeGrid(IReadOnlyList<LayoutNode> lines)
    {
        var s = _settings;
        var y = 0d;
        foreach (var layoutLine in lines)
        {
            var top = y > 0 ? y + s.Gap : y;
            foreach (var line in s.IsTier ? ColumnTier.AdaptLine(layoutLine, s) : [layoutLine])
            {
                if (y > 0) y += s.Gap;
                var height = line.RowSpan is { } rows ? Math.Max(s.HeightOf(rows), Min(line)) : Preferred(line);
                // An item alone on its line may be narrowed: the rest of the line is free
                var span = line.IsItem && line.ColumnSpan is { } fixedSpan ? Compat.Clamp(fixedSpan, 1, s.Columns) : s.Columns;
                Place(line, 0, span, y, height);
                if (span < s.Columns)
                    _free.Add(new FreeSpace(new Rect(s.X(span), y, s.WidthOf(s.Columns - span), height), line.Id, ItemSide.Right));
                y += height;
            }
            _lines.Add(new Rect(0, top, s.Width, y - top));
        }
        Height = y;
        ExtentWidth = s.Width;
    }

    private void Place(LayoutNode node, int start, int span, double y, double height)
    {
        var s = _settings;
        _columns[node.Id] = (start, span);
        if (node.IsItem)
        {
            _items[node.Id] = new Rect(s.X(start), y, s.WidthOf(span), height);
            return;
        }
        var children = node.Children;
        if (node.Split == SplitDirection.Row)
        {
            var spans = Distribute(span, children.Select(c => c.ColumnSpan).ToArray());
            var column = start;
            for (var i = 0; i < children.Count; i++)
            {
                Place(children[i], column, spans[i], y, height);
                column += spans[i];
            }
            if (children.All(c => c.ColumnSpan is not null) && column < start + span)
                _free.Add(new FreeSpace(new Rect(s.X(column), y, s.WidthOf(start + span - column), height), children[^1].Id, ItemSide.Right));
        }
        else
        {
            var fixedHeight = children.Where(c => c.RowSpan is not null).Sum(c => s.HeightOf(c.RowSpan!.Value));
            var free = children.Count(c => c.RowSpan is null);
            var rest = Math.Max(0, height - fixedHeight - s.Gap * (children.Count - 1));
            var top = y;
            foreach (var child in children)
            {
                var childHeight = child.RowSpan is { } rows ? s.HeightOf(rows) : rest / free;
                Place(child, start, span, top, childHeight);
                top += childHeight + s.Gap;
            }
            if (free == 0 && rest > s.Gap + 0.5)
                _free.Add(new FreeSpace(new Rect(s.X(start), top, s.WidthOf(span), rest - s.Gap), children[^1].Id, ItemSide.Below));
        }
    }

    /// <summary>Height a node takes when nothing constrains it.</summary>
    private double Preferred(LayoutNode node)
    {
        var s = _settings;
        if (node.IsItem) return s.HeightOf(node.RowSpan ?? LayoutNode.DefaultRowSpan);
        if (node.Split == SplitDirection.Column)
            return node.Children.Sum(c => c.RowSpan is { } rows ? s.HeightOf(rows) : Preferred(c)) + s.Gap * (node.Children.Count - 1);
        // A constrained item sets the height of its line: the others only need to fit
        var constrained = node.Children.Where(c => c.IsItem && c.RowSpan is not null).ToArray();
        return constrained.Length > 0
            ? Math.Max(constrained.Max(c => s.HeightOf(c.RowSpan!.Value)), node.Children.Except(constrained).Select(Min).DefaultIfEmpty(0).Max())
            : node.Children.Max(Preferred);
    }

    /// <summary>Smallest height a node can take.</summary>
    private double Min(LayoutNode node)
    {
        var s = _settings;
        if (node.IsItem) return s.HeightOf(node.RowSpan ?? LayoutNode.MinRowSpan);
        return node.Split == SplitDirection.Column
            ? node.Children.Sum(c => c.RowSpan is { } rows ? s.HeightOf(rows) : Min(c)) + s.Gap * (node.Children.Count - 1)
            : node.Children.Max(Min);
    }

    // --- Scrolling and wrapping ---

    private void ArrangeFlow(IReadOnlyList<LayoutNode> items, bool wrap)
    {
        var s = _settings;
        var width = Math.Max(1, s.Width);
        double Basis(LayoutNode item) => Math.Min(width, Math.Max(1, item.Width ?? s.MinItemWidth));

        var lines = new List<List<LayoutNode>>();
        var used = 0d;
        foreach (var item in items)
        {
            if (lines.Count == 0 || (wrap && used + s.Gap + Basis(item) > width + 0.5))
            {
                lines.Add([]);
                used = -s.Gap;
            }
            lines[^1].Add(item);
            used += s.Gap + Basis(item);
        }

        var y = 0d;
        var extent = width;
        foreach (var line in lines)
        {
            if (y > 0) y += s.Gap;
            var height = line.Max(i => s.HeightOf(i.RowSpan ?? LayoutNode.DefaultRowSpan));
            var total = line.Sum(Basis) + s.Gap * (line.Count - 1);
            var fits = total <= width + 0.5;
            var rest = fits ? width - total : 0;
            var free = line.Count(i => i.Width is null);
            var x = fits ? 0 : -Compat.Clamp(s.HorizontalOffset, 0, total - width);
            foreach (var item in line)
            {
                var itemWidth = Basis(item) + (item.Width is null && free > 0 ? rest / free : 0);
                _items[item.Id] = new Rect(x, y, itemWidth, height);
                x += itemWidth + s.Gap;
            }
            if (fits && free == 0 && rest > s.Gap + 0.5)
                _free.Add(new FreeSpace(new Rect(x, y, rest - s.Gap, height), line[^1].Id, ItemSide.Right));
            extent = Math.Max(extent, total);
            _lines.Add(new Rect(0, y, width, height));
            y += height;
        }
        Height = y;
        ExtentWidth = extent;
    }
}
