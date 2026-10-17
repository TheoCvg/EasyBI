using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardPanelTests
{
    // 12 columns of 90 px with gaps of 10 px: 12 x 90 + 11 x 10 = 1190
    private const double Width = 1190;

    private static DashboardPanel Panel(params BandMode[] modes)
    {
        var panel = new DashboardPanel { Gap = 10, RowHeight = 100, BandSpacing = 20 };
        foreach (var mode in modes) panel.Bands.Add(new DashboardBand { Mode = mode });
        return panel;
    }

    private static Border Child(DashboardPanel panel, int band, int column, int row, int columnSpan, int rowSpan, double width = 300, bool fullRow = false)
    {
        var child = new Border();
        DashboardPanel.SetBand(child, band);
        DashboardPanel.SetColumn(child, column);
        DashboardPanel.SetRow(child, row);
        DashboardPanel.SetColumnSpan(child, columnSpan);
        DashboardPanel.SetRowSpan(child, rowSpan);
        DashboardPanel.SetItemWidth(child, width);
        DashboardPanel.SetIsFullRow(child, fullRow);
        panel.Children.Add(child);
        return child;
    }

    private static Size Layout(DashboardPanel panel, double width = Width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
        return panel.DesiredSize;
    }

    [Fact]
    public void Grid_band_places_children_on_cells_with_spans() => Sta.Run(() =>
    {
        var panel = Panel(BandMode.Fit);
        var big = Child(panel, 0, 0, 0, 8, 2);
        var small = Child(panel, 0, 8, 1, 4, 1);
        var full = Child(panel, 0, 5, 2, 3, 1, fullRow: true);

        var size = Layout(panel);

        Assert.Equal(new Rect(0, 0, 8 * 90 + 7 * 10, 210), panel.GetItemBounds(big));
        Assert.Equal(new Rect(800, 110, 4 * 90 + 3 * 10, 100), panel.GetItemBounds(small));
        Assert.Equal(new Rect(0, 220, Width, 100), panel.GetItemBounds(full)); // full row: every column
        Assert.Equal(320, size.Height);
    });

    [Fact]
    public void Bands_are_stacked_with_spacing_between_them() => Sta.Run(() =>
    {
        var panel = Panel(BandMode.Fit, BandMode.Wrap);
        Child(panel, 0, 0, 0, 6, 1);
        var second = Child(panel, 1, 0, 0, 6, 2);

        var size = Layout(panel);

        Assert.Equal(new Rect(0, 0, Width, 100), panel.GetBandBounds(0));
        Assert.Equal(new Rect(0, 120, Width, 210), panel.GetBandBounds(1));
        Assert.Equal(120, panel.GetItemBounds(second).Top);
        Assert.Equal(330, size.Height);
        Assert.Equal(1, panel.BandAt(125));
    });

    [Fact]
    public void Wrap_band_lines_children_up_in_order_and_wraps() => Sta.Run(() =>
    {
        var panel = Panel(BandMode.Wrap);
        var third = Child(panel, 0, 2, 0, 1, 1, width: 500);
        var first = Child(panel, 0, 0, 0, 1, 1, width: 500);
        var second = Child(panel, 0, 1, 0, 1, 2, width: 500);

        Layout(panel);

        Assert.Equal(new Rect(0, 0, 500, 100), panel.GetItemBounds(first));   // order comes from Column, not from Children
        Assert.Equal(new Rect(510, 0, 500, 210), panel.GetItemBounds(second));
        Assert.Equal(new Rect(0, 220, 500, 100), panel.GetItemBounds(third)); // does not fit: next line, below the tallest
    });

    [Fact]
    public void Scroll_band_shifts_and_clips_children_by_its_offset() => Sta.Run(() =>
    {
        var panel = Panel(BandMode.Scroll);
        var first = Child(panel, 0, 0, 0, 1, 1, width: 800);
        var second = Child(panel, 0, 1, 0, 1, 1, width: 800);

        Layout(panel);
        var band = panel.Bands[0];
        Assert.Equal((1610d, Width, true), (band.ExtentWidth, band.ViewportWidth, band.CanScroll));
        Assert.NotNull(second.Clip); // partly outside the band

        band.HorizontalOffset = 10000; // clamped to the extent
        Layout(panel);

        Assert.Equal(1610 - Width, band.HorizontalOffset);
        Assert.Equal(-(1610 - Width), panel.GetItemBounds(first).Left);
        Assert.Null(second.Clip); // now fully visible
    });

    [Fact]
    public void Grid_cells_and_flow_positions_are_found_from_points() => Sta.Run(() =>
    {
        var panel = Panel(BandMode.Fit, BandMode.Wrap);
        Child(panel, 0, 0, 0, 6, 1);
        var a = Child(panel, 1, 0, 0, 1, 1, width: 300);
        var b = Child(panel, 1, 1, 0, 1, 1, width: 300);
        Layout(panel);

        Assert.Equal((3, 1), panel.CellAt(0, new Point(290, 105), 6));
        Assert.Equal((6, 0), panel.CellAt(0, new Point(5000, -40), 6)); // clamped so that the span fits

        var top = panel.GetBandBounds(1).Top;
        Assert.Equal(0, panel.FlowIndexAt(1, new Point(100, top + 50)));
        Assert.Equal(1, panel.FlowIndexAt(1, new Point(200, top + 50)));
        Assert.Equal(2, panel.FlowIndexAt(1, new Point(500, top + 50)));
        Assert.Equal(1, panel.FlowIndexAt(1, new Point(100, top + 50), excluding: a)); // before b
    });

    [Fact]
    public void A_panel_without_bands_is_one_grid_band() => Sta.Run(() =>
    {
        var panel = Panel();
        var child = Child(panel, 3, 6, 0, 6, 1); // band index past the end: last band

        Layout(panel);

        Assert.Equal(1, panel.BandCount);
        Assert.Equal(600, panel.GetItemBounds(child).Left);
    });
}
