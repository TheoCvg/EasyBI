using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Tests;

public class DragAndDropTests
{
    // Two lines of chips: [a][b][c] / [d]
    private static readonly Rect[] Chips =
    [
        new(0, 0, 100, 30), new(110, 0, 100, 30), new(220, 0, 100, 30),
        new(0, 40, 100, 30),
    ];

    [Theory]
    [InlineData(10, 15, 0)]   // left half of a
    [InlineData(60, 15, 1)]   // right half of a
    [InlineData(300, 15, 3)]  // right half of c: end of the first line, before d
    [InlineData(400, 15, 3)]  // past the end of the first line
    [InlineData(10, 55, 3)]   // left half of d
    [InlineData(90, 55, 4)]   // right half of d: at the end
    [InlineData(500, 200, 4)] // below everything
    public void Insertion_index_reads_a_wrapping_flow_line_by_line(double x, double y, int expected) =>
        Assert.Equal(expected, InsertionIndex.At(Chips, new Point(x, y)));

    [Fact]
    public void Insertion_index_in_a_single_line_only_looks_at_x()
    {
        Rect[] cards = [new(0, 0, 100, 300), new(110, 0, 100, 50)];

        Assert.Equal(1, InsertionIndex.At(cards, new Point(150, 280), singleLine: true));
        Assert.Equal(2, InsertionIndex.At(cards, new Point(150, 280))); // below the second card's line
    }

    [Fact]
    public void Insertion_index_in_a_vertical_stack_only_looks_at_y()
    {
        Rect[] rows = [new(0, 0, 200, 20), new(0, 20, 200, 20), new(0, 40, 200, 20)];

        Assert.Equal(0, InsertionIndex.At(rows, new Point(500, 5), Orientation.Vertical));
        Assert.Equal(2, InsertionIndex.At(rows, new Point(-50, 35), Orientation.Vertical));
        Assert.Equal(3, InsertionIndex.At(rows, new Point(0, 55), Orientation.Vertical));
    }

    [Fact]
    public void Items_without_bounds_are_skipped_but_keep_their_index()
    {
        Rect[] items = [new(0, 0, 100, 30), Rect.Empty, new(220, 0, 100, 30)];

        Assert.Equal(2, InsertionIndex.At(items, new Point(150, 15)));
    }

    [Fact]
    public void Insertion_line_sits_between_two_items_of_a_line()
    {
        Assert.Equal(new Rect(104, 0, 2, 30), InsertionIndex.IndicatorBounds(Chips, 1));
        // Before the first item of a line, along its left edge; after the last item, along its right edge
        Assert.Equal(new Rect(-1, 40, 2, 30), InsertionIndex.IndicatorBounds(Chips, 3));
        Assert.Equal(new Rect(99, 40, 2, 30), InsertionIndex.IndicatorBounds(Chips, 4));
        Assert.Equal(Rect.Empty, InsertionIndex.IndicatorBounds([], 0));
    }

    [Fact]
    public void Insertion_line_in_a_vertical_stack_is_horizontal()
    {
        Rect[] rows = [new(0, 0, 200, 20), new(0, 30, 200, 20)];

        Assert.Equal(new Rect(0, 24, 200, 2), InsertionIndex.IndicatorBounds(rows, 1, Orientation.Vertical));
        Assert.Equal(new Rect(0, 49, 200, 2), InsertionIndex.IndicatorBounds(rows, 2, Orientation.Vertical));
    }

    [Theory]
    [InlineData(200, 200, 0, 0)]      // middle: no scrolling
    [InlineData(200, 30, 0, -250)]    // a quarter into the top zone (40 px): a quarter of the speed
    [InlineData(200, 395, 0, 875)]    // 35 px into the bottom zone
    [InlineData(200, 1000, 0, 1000)]  // beyond the edge: full speed
    [InlineData(-5, 200, -1000, 0)]
    public void Auto_scroll_speeds_up_towards_and_beyond_the_edges(double x, double y, double vx, double vy)
    {
        var velocity = AutoScroller.VelocityAt(new Rect(0, 0, 400, 400), new Point(x, y), edgeSize: 40, maxSpeed: 1000);

        Assert.Equal(vx, velocity.X, 6);
        Assert.Equal(vy, velocity.Y, 6);
    }

    [Fact]
    public void Auto_scroll_zones_shrink_in_a_small_viewport()
    {
        // 60 px high: zones of 20 px, so the centre does not scroll
        var velocity = AutoScroller.VelocityAt(new Rect(0, 0, 400, 60), new Point(200, 30), edgeSize: 40, maxSpeed: 1000);

        Assert.Equal(0, velocity.Y);
    }

    [Fact]
    public void Dragged_items_travel_under_their_own_format_and_their_type() => Sta.Run(() =>
    {
        var item = new Uri("https://example.org");

        var data = DragSource.CreateData(item);

        Assert.Same(item, DragSource.ItemOf(data));
        Assert.Same(item, data.GetData(typeof(Uri)));
        Assert.Equal("text", DragSource.ItemOf(new DataObject(DataFormats.UnicodeText, "text")));
    });
}
