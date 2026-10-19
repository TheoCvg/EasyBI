using System.Windows;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Geometry of a band: greedy widths and heights, free spaces, wrapping and scrolling.</summary>
public class BandArrangementTests
{
    // 12 columns of 90 px with gaps of 10 px: a column starts every 100 px, n columns are 100n - 10 wide,
    // n rows are 110n - 10 high
    private static readonly ArrangeSettings Grid = new(Width: 1190, Columns: 12, RowHeight: 100, Gap: 10, MinItemWidth: 280);

    private static LayoutNode I(string id, int? columns = null, int? rows = null, double? width = null) =>
        new() { Id = id, VisualId = id, ColumnSpan = columns, RowSpan = rows, Width = width };

    private static LayoutNode Row(params LayoutNode[] children) => LayoutNode.Row(children) with { Id = "row" };

    private static LayoutNode Col(params LayoutNode[] children) => LayoutNode.Column(children) with { Id = "col" };

    private static BandArrangement Arrange(BandMode mode, params LayoutNode[] items) =>
        BandArrangement.Arrange(new BandLayout { Mode = mode, Items = items }, Grid);

    [Fact]
    public void Greedy_items_share_the_line()
    {
        var band = Arrange(BandMode.Fit, Row(I("a"), I("b"), I("c")));

        Assert.Equal([new Rect(0, 0, 390, 320), new Rect(400, 0, 390, 320), new Rect(800, 0, 390, 320)], new[] { "a", "b", "c" }.Select(id => band.Items[id]));
        Assert.Empty(band.FreeSpaces);
    }

    [Fact]
    public void Constrained_items_keep_their_columns_and_the_others_share_the_rest()
    {
        var band = Arrange(BandMode.Fit, Row(I("a", columns: 3), I("b"), I("c")));

        // 9 columns left for two: the first takes the odd one
        Assert.Equal([(0, 3), (3, 5), (8, 4)], new[] { "a", "b", "c" }.Select(id => band.Columns[id]));
    }

    [Fact]
    public void Constrained_items_alone_leave_a_free_space()
    {
        var line = Arrange(BandMode.Fit, Row(I("a", columns: 3), I("b", columns: 4)));
        var item = Arrange(BandMode.Fit, I("a", columns: 6));

        Assert.Equal(new FreeSpace(new Rect(700, 0, 490, 320), "b", ItemSide.Right), Assert.Single(line.FreeSpaces));
        Assert.Equal(new FreeSpace(new Rect(600, 0, 590, 320), "a", ItemSide.Right), Assert.Single(item.FreeSpaces));
    }

    [Fact]
    public void A_constrained_height_sets_the_line_and_the_others_stretch()
    {
        var band = Arrange(BandMode.Fit, Row(I("a", rows: 2), I("b")));

        Assert.Equal(210, band.Items["a"].Height);
        Assert.Equal(210, band.Items["b"].Height);
        Assert.Equal(210, band.Height);
    }

    [Fact]
    public void A_stack_next_to_a_tall_item_shares_its_height()
    {
        var band = Arrange(BandMode.Fit, Row(I("big", columns: 6, rows: 2), Col(I("x"), I("y"))));

        Assert.Equal(new Rect(600, 0, 590, 100), band.Items["x"]);
        Assert.Equal(new Rect(600, 110, 590, 100), band.Items["y"]);
        Assert.Equal(210, band.Items["big"].Height);
    }

    [Fact]
    public void A_stack_that_does_not_fit_makes_the_line_taller()
    {
        // Three stacked items need 3 rows at least: the constrained item stretches with them
        var band = Arrange(BandMode.Fit, Row(I("big", rows: 1), Col(I("x"), I("y"), I("z"))));

        Assert.Equal(320, band.Items["big"].Height);
        Assert.Equal(100, band.Items["z"].Height);
    }

    [Fact]
    public void Without_constraints_a_line_takes_the_height_its_items_prefer()
    {
        var band = Arrange(BandMode.Fit, Row(I("a"), Col(I("x"), I("y"))));

        Assert.Equal(650, band.Height); // two stacked items of 3 rows
        Assert.Equal(650, band.Items["a"].Height);
    }

    [Fact]
    public void A_stack_of_constrained_items_leaves_a_free_space_below()
    {
        var band = Arrange(BandMode.Fit, Row(I("a", rows: 3), Col(I("x", rows: 1))));

        Assert.Equal(new FreeSpace(new Rect(600, 110, 590, 210), "x", ItemSide.Below), Assert.Single(band.FreeSpaces));
    }

    [Fact]
    public void Lines_are_stacked_with_a_gap()
    {
        var band = Arrange(BandMode.Fit, I("title", rows: 1), Row(I("a"), I("b")));

        Assert.Equal(new Rect(0, 0, 1190, 100), band.Items["title"]);
        Assert.Equal(110, band.Items["a"].Top);
        Assert.Equal([new Rect(0, 0, 1190, 100), new Rect(0, 110, 1190, 320)], band.Lines);
        Assert.Equal(430, band.Height);
    }

    [Fact]
    public void Wrapping_items_start_at_the_minimum_width_then_share_their_line()
    {
        // 4 x 280 + 3 gaps = 1150: four per line, then the 40 px left are shared
        var band = Arrange(BandMode.Wrap, I("a"), I("b"), I("c"), I("d"), I("e"));

        Assert.Equal(290, band.Items["a"].Width);
        Assert.Equal(900, band.Items["d"].Left);
        Assert.Equal(new Rect(0, 330, 1190, 320), band.Items["e"]); // alone on its line: all of it
        Assert.Equal(2, band.Lines.Count);
    }

    [Fact]
    public void Wrapping_constrained_items_keep_their_width_and_leave_a_free_space()
    {
        var band = Arrange(BandMode.Wrap, I("a", width: 300), I("b", width: 300));

        Assert.Equal(new Rect(310, 0, 300, 320), band.Items["b"]);
        Assert.Equal(new FreeSpace(new Rect(620, 0, 570, 320), "b", ItemSide.Right), Assert.Single(band.FreeSpaces));
    }

    [Fact]
    public void A_scrolling_band_grows_its_items_when_they_fit_and_scrolls_when_they_do_not()
    {
        var fits = Arrange(BandMode.Scroll, I("a"), I("b"));
        var scrolls = BandArrangement.Arrange(
            new BandLayout { Mode = BandMode.Scroll, Items = [I("a", width: 600), I("b", width: 600), I("c")] },
            Grid with { HorizontalOffset = 100 });

        Assert.Equal(590, fits.Items["a"].Width);
        Assert.Equal(new Rect(-100, 0, 600, 320), scrolls.Items["a"]);
        Assert.Equal(1120, scrolls.Items["c"].Left);
        Assert.Equal(600 + 600 + 280 + 20, scrolls.ExtentWidth);
    }

    [Theory]
    [InlineData(12, new int[0], new[] { 4, 4, 4 }, 3)]
    [InlineData(12, new[] { 3 }, new[] { 3, 5, 4 }, 3)]
    [InlineData(12, new[] { 8, 8 }, new[] { 8, 3, 1 }, 3)] // the last constraint gives way so that everyone gets a column
    [InlineData(2, new int[0], new[] { 1, 1, 0 }, 3)]      // more children than columns
    public void Columns_are_distributed_constraints_first(int total, int[] constraints, int[] expected, int count)
    {
        var spans = BandArrangement.Distribute(total, Enumerable.Range(0, count).Select(i => i < constraints.Length ? constraints[i] : (int?)null).ToArray());

        Assert.Equal(expected, spans);
    }
}
