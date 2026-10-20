using System.Windows;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Grid bands of a narrow panel show fewer columns and adapt their lines, without changing the layout.</summary>
public class ColumnTierTests
{
    // 6 columns of 90 px with gaps of 10 px (a column every 100 px) for a layout counted in 12; rows are 100 px high
    private static readonly ArrangeSettings Six = new(Width: 590, Columns: 6, RowHeight: 100, Gap: 10, MinItemWidth: 280, LayoutColumns: 12);

    private static readonly ArrangeSettings One = new(Width: 400, Columns: 1, RowHeight: 100, Gap: 10, MinItemWidth: 280, LayoutColumns: 12);

    private static LayoutNode I(string id, int? columns = null, int? rows = null) =>
        new() { Id = id, VisualId = id, ColumnSpan = columns, RowSpan = rows };

    private static BandArrangement Arrange(ArrangeSettings settings, params LayoutNode[] lines) =>
        BandArrangement.Arrange(new BandLayout { Items = lines }, settings);

    private static (int Start, int Span)[] Columns(BandArrangement band, params string[] ids) => ids.Select(id => band.Columns[id]).ToArray();

    [Fact]
    public void A_single_column_stacks_every_item_at_full_width()
    {
        var band = Arrange(One, LayoutNode.Row(I("a", columns: 4), I("b"), I("c", rows: 2)), I("d", columns: 6));

        Assert.All(new[] { "a", "b", "c", "d" }, id => Assert.Equal(new Rect(0, band.Items[id].Y, 400, band.Items[id].Height), band.Items[id]));
        Assert.Equal([0d, 330, 660, 880], new[] { "a", "b", "c", "d" }.Select(id => band.Items[id].Y));
        Assert.Equal(210, band.Items["c"].Height); // its height is kept
        Assert.Empty(band.FreeSpaces);
    }

    [Fact]
    public void Each_line_of_the_layout_reports_one_bound_so_that_line_indexes_do_not_change()
    {
        var band = Arrange(One, LayoutNode.Row(I("a"), I("b")), I("c"));

        Assert.Equal([new Rect(0, 0, 400, 650), new Rect(0, 660, 400, 320)], band.Lines);
    }

    [Fact]
    public void Six_columns_keep_two_items_side_by_side_and_break_longer_lines()
    {
        var band = Arrange(Six, LayoutNode.Row(I("a"), I("b"), I("c"), I("d"), I("e")));

        Assert.Equal([(0, 3), (3, 3), (0, 3), (3, 3), (0, 6)], Columns(band, "a", "b", "c", "d", "e"));
        Assert.Equal(band.Items["a"].Y, band.Items["b"].Y);
        Assert.True(band.Items["c"].Y > band.Items["a"].Y);
        Assert.Single(band.Lines);
    }

    [Fact]
    public void Constrained_items_keep_their_proportions_and_fill_their_line()
    {
        var band = Arrange(Six, LayoutNode.Row(I("a", columns: 8), I("b", columns: 4)));

        Assert.Equal([(0, 4), (4, 2)], Columns(band, "a", "b"));
        Assert.Empty(band.FreeSpaces);
    }

    [Fact]
    public void A_constrained_item_is_scaled_and_a_greedy_one_takes_the_rest()
    {
        var band = Arrange(Six, LayoutNode.Row(I("a", columns: 3), I("b")));

        Assert.Equal([(0, 2), (2, 4)], Columns(band, "a", "b"));
    }

    [Fact]
    public void Stacks_keep_their_items_stacked()
    {
        var band = Arrange(Six, LayoutNode.Row(I("a"), LayoutNode.Column(I("b"), I("c")) with { Id = "stack" }));

        Assert.Equal([(0, 3), (3, 3), (3, 3)], Columns(band, "a", "b", "c"));
        Assert.True(band.Items["c"].Y > band.Items["b"].Y);
    }

    [Fact]
    public void The_layout_columns_apply_above_every_tier()
    {
        Assert.Equal(1, ColumnTier.ColumnsFor(ColumnTier.Defaults, 500, 12));
        Assert.Equal(6, ColumnTier.ColumnsFor(ColumnTier.Defaults, 700, 12));
        Assert.Equal(12, ColumnTier.ColumnsFor(ColumnTier.Defaults, 960, 12));
        Assert.Equal(4, ColumnTier.ColumnsFor([new ColumnTier { BelowWidth = 600, Columns = 6 }], 500, 4)); // never more than the layout
        Assert.Equal(12, ColumnTier.ColumnsFor([], 300, 12));
    }

    [Fact]
    public void A_narrow_panel_shows_a_tier_without_changing_its_layout() => Sta.Run(() =>
    {
        var layout = new DashboardLayoutDefinition { Bands = [new BandLayout { Items = [LayoutNode.Row(I("a", columns: 8), I("b", columns: 4))] }] };
        var panel = new DashboardPanel { Layout = layout, Gap = 10 };
        foreach (var id in new[] { "a", "b" })
        {
            var child = new System.Windows.Controls.Border();
            DashboardPanel.SetItemId(child, id);
            panel.Children.Add(child);
        }

        panel.Measure(new Size(700, 1000));
        panel.Arrange(new Rect(0, 0, 700, 1000));

        Assert.Equal(6, panel.ActiveColumns);
        Assert.Equal((0, 4), panel.GetColumns("a"));
        Assert.Equal(8, panel.ToLayoutSpan(4)); // a resize counts in the layout's columns
        Assert.Same(layout, panel.Layout);
    });
}
