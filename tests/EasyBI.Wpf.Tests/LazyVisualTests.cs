using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Only the visuals in or near the visible part of a dashboard query their data.</summary>
public class LazyVisualTests
{
    private static VisualDefinition Table(string id) =>
        new() { Id = id, Model = "sales", Type = VisualType.Table, Axis = [new("Region")], Values = [new("Amount")] };

    // Loaded, queued refreshes, viewport updates and query continuations run on the dispatcher
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static Window Show(FrameworkElement content, double height)
    {
        var window = new Window { Content = content, Width = 900, Height = height, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        Pump();
        return window;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    // Twelve tables, one per line: a line is three rows (320 px) high
    private static DashboardView Dashboard(EasyBIContext context)
    {
        var view = new DashboardView { Context = context };
        var visuals = Enumerable.Range(0, 12).Select(i => Table($"t{i}")).ToArray();
        view.Load(new DashboardDocument
        {
            Report = new ReportDefinition { Visuals = visuals },
            Layout = new DashboardLayoutDefinition { Bands = [new BandLayout { Items = visuals.Select(v => LayoutNode.Item(v.Id)).ToArray() }] },
        });
        return view;
    }

    [Fact]
    public void An_inactive_visual_view_waits_to_be_active_to_query() => Sta.Run(() =>
    {
        var model = new CountingModel("sales");
        var context = EasyBIContext.Create().AddModel(model);
        var view = new VisualView { Context = context, Definition = Table("t"), IsActive = false };
        var window = Show(view, 300);
        Assert.Equal(0, model.Queries);

        view.IsActive = true;
        Pump();
        Assert.Equal(1, model.Queries);

        // Changes while inactive wait too, then give one query
        view.IsActive = false;
        context.RefreshData();
        context.RefreshData();
        Pump();
        Assert.Equal(1, model.Queries);
        view.IsActive = true;
        Pump();
        Assert.Equal(2, model.Queries);
        window.Close();
    });

    [Fact]
    public void A_dashboard_loads_the_visible_items_then_the_others_as_they_scroll_into_view() => Sta.Run(() =>
    {
        var model = new CountingModel("sales");
        var view = Dashboard(EasyBIContext.Create().AddModel(model));
        var window = Show(view, 500);
        Pump();

        var items = view.Items.ToArray();
        Assert.True(items[0].IsInViewport);
        Assert.False(items[^1].IsInViewport);
        Assert.InRange(model.Queries, 1, 6); // the visible lines and a screen below, not the twelve

        Descendants<ScrollViewer>(view).First().ScrollToBottom();
        Pump();
        Pump();

        Assert.True(items[^1].IsInViewport);
        Assert.False(items[0].IsInViewport);
        window.Close();
    });

    [Fact]
    public void A_dashboard_can_load_every_visual() => Sta.Run(() =>
    {
        var model = new CountingModel("sales");
        var view = Dashboard(EasyBIContext.Create().AddModel(model));
        view.LoadsVisibleItemsOnly = false;
        var window = Show(view, 500);
        Pump();

        Assert.All(view.Items, i => Assert.True(i.IsInViewport));
        Assert.Equal(12, model.Queries);
        window.Close();
    });
}
