using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Dashboard.Behaviors;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Tests;

public class DashboardSlotTests
{
    private static VisualDefinition Text(string id) => new() { Id = id, Mark = MarkType.Text, Text = new TextContent { Text = id } };

    private static DataTemplate TagTemplate(string tag) => (DataTemplate)XamlReader.Parse(
        $$"""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <Button Tag="{{tag}}" Content="{Binding Visual.Id}" />
        </DataTemplate>
        """);

    private static DashboardView View(params VisualDefinition[] visuals)
    {
        var view = new DashboardView();
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = visuals } });
        return view;
    }

    private static void Layout(UIElement element)
    {
        element.Measure(new Size(1000, 700));
        element.Arrange(new Rect(0, 0, 1000, 700));
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

    private static Button[] Tagged(DependencyObject root, string tag) =>
        Descendants<Button>(root).Where(b => Equals(b.Tag, tag)).ToArray();

    [Fact]
    public void Item_content_slot_is_shown_in_each_item_with_the_item_as_data_context() => Sta.Run(() =>
    {
        var view = View(Text("a"), Text("b"));
        Slots.AddTemplate(view, DashboardSlots.ItemContent, TagTemplate("footer"));

        Layout(view);

        var items = view.Items.ToArray();
        Assert.Equal(["a", "b"], items.Select(i => Tagged(i, "footer").Single().Content));
    });

    [Fact]
    public void Empty_items_offer_the_choices_added_to_their_slot() => Sta.Run(() =>
    {
        var view = View();
        Slots.AddTemplate(view, DashboardSlots.EmptyItemChoices, TagTemplate("choice"));
        Slots.AddTemplate(view, DashboardSlots.ItemContent, TagTemplate("footer"));

        Layout(view);

        var item = Assert.Single(view.Items);
        Assert.Single(Tagged(item, "choice"));
        Assert.All(Tagged(item, "footer"), footer => Assert.Equal(0, footer.ActualHeight)); // an empty item has no content to add a footer to
    });

    [Fact]
    public void Body_slot_adds_content_above_the_scrolling_dashboard() => Sta.Run(() =>
    {
        var view = View(Text("a"));
        var header = new TextBlock { Text = "header", Height = 30 };
        Slots.Add(view, DashboardSlots.Body, header, SlotMode.Before);

        Layout(view);

        var scroller = Descendants<ScrollViewer>(view).First();
        Assert.Equal(30, scroller.TranslatePoint(new Point(), view).Y);
    });

    [Fact]
    public void Item_toolbar_slot_follows_the_hovered_item() => Sta.Run(() =>
    {
        var view = View(Text("a"), Text("b"));
        Slots.AddTemplate(view, DashboardSlots.ItemToolbar, TagTemplate("tool"));
        var window = new Window { Content = view, Width = 1000, Height = 700, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var behavior = Interaction.GetBehaviors(view).OfType<ItemToolbarBehavior>().Single();
            var overlay = DashboardOverlay.For(view);

            overlay.Lock(view.Items.ElementAt(1));
            window.UpdateLayout();

            var tool = Tagged(overlay, "tool").Single();
            Assert.Equal("b", tool.Content);
            Assert.NotNull(behavior);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Item_commands_in_the_toolbar_slot_act_on_the_hovered_item() => Sta.Run(() =>
    {
        var view = View(Text("a"), Text("b"));
        Slots.AddTemplate(view, DashboardSlots.ItemToolbar, TagTemplate("tool"));
        var window = new Window { Content = view, Width = 1000, Height = 700, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            var overlay = DashboardOverlay.For(view);
            overlay.Lock(view.Items.ElementAt(1));
            window.UpdateLayout();

            var tool = Tagged(overlay, "tool").Single();
            Assert.True(DashboardCommands.RemoveItem.CanExecute(null, tool));
            DashboardCommands.RemoveItem.Execute(null, tool);

            Assert.Equal(["a"], view.Items.Select(i => i.Visual?.Id));
        }
        finally
        {
            window.Close();
        }
    });
}
