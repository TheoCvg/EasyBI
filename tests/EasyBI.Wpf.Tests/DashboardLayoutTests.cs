using System.Windows;
using System.Windows.Controls;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardLayoutTests
{
    private static Border Child(DashboardPanel panel, int band, int column, int row, int columnSpan = 6, int rowSpan = 3, string? tag = null)
    {
        var child = new Border { Tag = tag };
        DashboardPanel.SetBand(child, band);
        DashboardPanel.SetColumn(child, column);
        DashboardPanel.SetRow(child, row);
        DashboardPanel.SetColumnSpan(child, columnSpan);
        DashboardPanel.SetRowSpan(child, rowSpan);
        panel.Children.Add(child);
        return child;
    }

    private static (int Band, int Column, int Row) Place(UIElement child) =>
        (DashboardPanel.GetBand(child), DashboardPanel.GetColumn(child), DashboardPanel.GetRow(child));

    [Fact]
    public void Moving_a_child_pushes_the_one_it_lands_on_down() => Sta.Run(() =>
    {
        var panel = new DashboardPanel();
        var a = Child(panel, 0, 0, 0);
        var b = Child(panel, 0, 6, 0);

        DashboardLayout.Move(b, 0, 0, 0);

        Assert.Equal((0, 0, 0), Place(b));
        Assert.Equal((0, 0, 3), Place(a));
    });

    [Fact]
    public void Moving_the_last_child_out_of_a_band_removes_it_unless_asked_to_keep_it() => Sta.Run(() =>
    {
        var panel = new DashboardPanel { Bands = { new DashboardBand { Mode = BandMode.Scroll }, new DashboardBand() } };
        var a = Child(panel, 0, 0, 0);
        var b = Child(panel, 1, 0, 0);
        var grid = panel.Bands[1];

        DashboardLayout.Move(a, 1, 6, 0, keepEmptyBands: true);
        Assert.Equal(2, panel.Bands.Count);
        Assert.Equal((1, 6, 0), Place(a));

        DashboardLayout.RemoveEmptyBands(panel);
        Assert.Same(grid, Assert.Single(panel.Bands)); // the band object (and its scroll position) is kept
        Assert.Equal((0, 6, 0), Place(a));
        Assert.Equal((0, 0, 0), Place(b));
    });

    [Fact]
    public void Adding_and_removing_children_updates_the_panel() => Sta.Run(() =>
    {
        var panel = new DashboardPanel();
        var a = Child(panel, 0, 0, 0, 4, 2);
        var right = new Border();
        var below = new Border();

        DashboardLayout.AddNextTo(a, right, ItemSide.Right);
        DashboardLayout.AddNextTo(a, below, ItemSide.Below);
        Assert.Equal([a, right, below], panel.Children.Cast<UIElement>());
        Assert.Equal((0, 4, 0), Place(right));
        Assert.Equal((0, 0, 2), Place(below));

        DashboardLayout.Remove(right);
        Assert.Equal([a, below], panel.Children.Cast<UIElement>());
    });

    [Fact]
    public void Inserting_a_band_shifts_the_following_bands() => Sta.Run(() =>
    {
        var panel = new DashboardPanel { Bands = { new DashboardBand() } };
        var a = Child(panel, 0, 0, 0);
        var n = new Border();

        DashboardLayout.InsertBand(panel, 0, BandMode.Wrap, n);

        Assert.Equal([BandMode.Wrap, BandMode.Fit], panel.Bands.Select(b => b.Mode));
        Assert.Equal(0, DashboardPanel.GetBand(n));
        Assert.Equal(1, DashboardPanel.GetBand(a));
    });

    [Fact]
    public void A_snapshot_puts_children_and_bands_back() => Sta.Run(() =>
    {
        var panel = new DashboardPanel { Bands = { new DashboardBand(), new DashboardBand { Mode = BandMode.Wrap } } };
        var a = Child(panel, 0, 0, 0);
        var b = Child(panel, 1, 0, 0);
        var snapshot = DashboardLayout.Capture(panel);

        DashboardLayout.Move(b, 0, 0, 0);
        DashboardLayout.SetFullRow(a, true);
        snapshot.Restore();

        Assert.Equal(2, panel.Bands.Count);
        Assert.Equal((0, 0, 0), Place(a));
        Assert.Equal((1, 0, 0), Place(b));
        Assert.False(DashboardPanel.GetIsFullRow(a));
    });

    [Fact]
    public void A_layout_round_trips_and_keeps_existing_children() => Sta.Run(() =>
    {
        var panel = new DashboardPanel { Bands = { new DashboardBand(), new DashboardBand { Mode = BandMode.Scroll } } };
        var a = Child(panel, 0, 0, 0, 8, 2, tag: "a");
        var b = Child(panel, 1, 0, 0, tag: "b");
        DashboardPanel.SetItemWidth(b, 420);

        var saved = DashboardLayout.GetLayout(panel, e => (string?)((FrameworkElement)e).Tag);
        panel.Children.Clear();
        panel.Children.Add(a);
        panel.Bands.Clear();
        var created = new List<UIElement>();
        DashboardLayout.SetLayout(panel, saved, item =>
        {
            if (item.VisualId == "a") return a;
            var element = new Border { Tag = item.VisualId };
            created.Add(element);
            return element;
        });

        Assert.Equal([BandMode.Fit, BandMode.Scroll], panel.Bands.Select(x => x.Mode));
        Assert.Equal((0, 0, 0), Place(a));
        Assert.Equal(8, DashboardPanel.GetColumnSpan(a));
        var restored = Assert.Single(created);
        Assert.Equal((1, 0, 0), Place(restored));
        Assert.Equal(420, DashboardPanel.GetItemWidth(restored));
    });

    [Fact]
    public void Every_operation_raises_arrangement_changed() => Sta.Run(() =>
    {
        var panel = new DashboardPanel();
        var a = Child(panel, 0, 0, 0);
        var raised = 0;
        panel.ArrangementChanged += (_, _) => raised++;

        DashboardLayout.Resize(a, 4, 2);
        DashboardLayout.Update(panel, (layout, idOf) => DashboardLayoutEngine.SetFullRow(layout, idOf(a), true));

        Assert.Equal(2, raised);
        Assert.True(DashboardPanel.GetIsFullRow(a));
    });
}
