using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

public class SlotTests
{
    private static Rect BoundsIn(UIElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));

    private static void Layout(UIElement element, double width = 400, double height = 300)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
    }

    private static bool IsShown(UIElement element, Visual root) =>
        element.IsDescendantOf(root) && element.RenderSize.Width > 0;

    [Fact]
    public void Slot_content_goes_after_the_default_content_by_default() => Sta.Run(() =>
    {
        var main = new Border { Width = 50 };
        var added = new Border { Width = 20 };
        var host = new SlotHost { Slot = "Test", Content = main };
        Slots.Add(host, "Test", added);

        Layout(host);

        Assert.True(BoundsIn(added, host).Left >= BoundsIn(main, host).Right);
    });

    [Fact]
    public void Slot_content_goes_before_or_after_the_default_content_in_declaration_order() => Sta.Run(() =>
    {
        var main = new Border();
        var before = new Border { Height = 10 };
        var after1 = new Border { Height = 10 };
        var after2 = new Border { Height = 10 };
        var host = new SlotHost { Slot = "Test", Content = main, Orientation = Orientation.Vertical };
        Slots.Add(host, "Test", after1);
        Slots.Add(host, "Test", before, SlotMode.Before);
        Slots.Add(host, "Test", after2);

        Layout(host);

        Assert.Equal(0, BoundsIn(before, host).Top);
        Assert.Equal(10, BoundsIn(main, host).Top);
        Assert.Equal(new[] { before, main, after1, after2 }, new[] { before, main, after1, after2 }.OrderBy(e => BoundsIn(e, host).Top));
    });

    [Fact]
    public void Default_content_fills_the_space_left_by_the_slots() => Sta.Run(() =>
    {
        var main = new Border();
        var added = new Border { Width = 100 };
        var host = new SlotHost { Slot = "Test", Content = main };
        Slots.Add(host, "Test", added);

        Layout(host, 400);

        Assert.Equal(300, main.RenderSize.Width);
    });

    [Fact]
    public void A_replacing_slot_shows_its_content_instead_of_the_default_content() => Sta.Run(() =>
    {
        var main = new Border { Width = 50 };
        var replacement = new Border { Width = 20 };
        var host = new SlotHost { Slot = "Test", Content = main };

        Slots.Add(host, "Test", replacement, SlotMode.Replace);
        Layout(host);

        Assert.True(host.IsDefaultContentReplaced);
        Assert.False(main.IsDescendantOf(host));
        Assert.True(IsShown(replacement, host));
    });

    [Fact]
    public void Hiding_a_slot_keeps_the_content_added_around_it() => Sta.Run(() =>
    {
        var main = new Border { Width = 50 };
        var added = new Border { Width = 20 };
        var host = new SlotHost { Slot = "Test", Content = main };

        Slots.Add(host, "Test", added);
        Slots.Hide(host, "Test");
        Layout(host);

        Assert.False(main.IsDescendantOf(host));
        Assert.True(IsShown(added, host));
    });

    [Fact]
    public void Slots_of_another_name_are_ignored() => Sta.Run(() =>
    {
        var main = new Border();
        var host = new SlotHost { Slot = "Test", Content = main };

        Slots.Hide(host, "Other");
        Slots.Add(host, "Other", new Border());
        Layout(host);

        Assert.False(host.IsDefaultContentReplaced);
        Assert.Equal(1, VisualTreeHelper.GetChildrenCount(VisualTreeHelper.GetChild(host, 0)));
    });

    [Fact]
    public void Removing_a_slot_or_changing_its_mode_updates_the_host() => Sta.Run(() =>
    {
        var main = new Border();
        var host = new SlotHost { Slot = "Test", Content = main };
        var slot = Slots.Hide(host, "Test");
        Assert.True(host.IsDefaultContentReplaced);

        slot.Mode = SlotMode.After;
        Assert.False(host.IsDefaultContentReplaced);

        slot.Mode = SlotMode.Hide;
        Slots.GetItems(host).Remove(slot);
        Layout(host);
        Assert.False(host.IsDefaultContentReplaced);
        Assert.True(main.IsDescendantOf(host));
    });

    [Fact]
    public void Slots_declared_on_an_ancestor_apply_to_the_hosts_below_it() => Sta.Run(() =>
    {
        var host = new SlotHost { Slot = "Test", Content = new Border() };
        var root = new Border { Child = new Border { Child = host } };

        Slots.Hide(root, "Test");

        Assert.True(host.IsDefaultContentReplaced);
    });

    [Fact]
    public void Slot_content_takes_the_data_context_of_the_host() => Sta.Run(() =>
    {
        var added = new TextBlock();
        var host = new SlotHost { Slot = "Test", DataContext = "context" };
        Slots.Add(host, "Test", added);

        Layout(host);

        Assert.Equal("context", added.DataContext);
    });

    [Fact]
    public void A_template_slot_is_shown_once_per_host_with_the_data_context_of_each() => Sta.Run(() =>
    {
        var template = (DataTemplate)XamlReader.Parse(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Text="{Binding}" />
            </DataTemplate>
            """);
        var first = new SlotHost { Slot = "Test", DataContext = "first" };
        var second = new SlotHost { Slot = "Test", DataContext = "second" };
        var root = new StackPanel { Children = { first, second } };
        Slots.AddTemplate(root, "Test", template);

        Layout(root);

        Assert.Equal(["first", "second"], new[] { first, second }.Select(h => FindText(h)!.Text));
    });

    [Fact]
    public void An_element_shown_in_two_places_is_reported() => Sta.Run(() =>
    {
        var root = new StackPanel();
        Slots.Add(root, "Test", new Border());
        root.Children.Add(new SlotHost { Slot = "Test" });

        var error = Assert.Throws<InvalidOperationException>(() => root.Children.Add(new SlotHost { Slot = "Test" }));
        Assert.Contains("ContentTemplate", error.Message);
    });

    [Fact]
    public void Slots_are_declared_in_xaml() => Sta.Run(() =>
    {
        var root = (Border)XamlReader.Parse(
            """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:bi="https://github.com/TheoCvg/EasyBI">
                <bi:Slots.Items>
                    <bi:Slot Name="Test"><TextBlock Text="added" /></bi:Slot>
                    <bi:Slot Name="Test" Mode="Hide" />
                </bi:Slots.Items>
                <bi:SlotHost Slot="Test"><TextBlock Text="default" /></bi:SlotHost>
            </Border>
            """);

        var host = (SlotHost)root.Child;
        Layout(root);

        Assert.True(host.IsDefaultContentReplaced);
        Assert.Equal("added", FindText(host)!.Text);
    });

    [Fact]
    public void Designer_parts_can_be_hidden_and_extended() => Sta.Run(() =>
    {
        var designer = new ReportDesigner();
        var button = new Button();
        Slots.Hide(designer, ReportDesignerSlots.FiltersShelf);
        Slots.Add(designer, ReportDesignerSlots.Header, button);

        Layout(designer, 1000, 700);

        Assert.True(Host(designer, ReportDesignerSlots.FiltersShelf).IsDefaultContentReplaced);
        Assert.False(Host(designer, ReportDesignerSlots.AxisShelf).IsDefaultContentReplaced);
        Assert.True(IsShown(button, designer));
    });

    [Fact]
    public void Every_designer_and_filter_bar_slot_has_a_host() => Sta.Run(() =>
    {
        var designer = new ReportDesigner();
        var bar = new FilterBar();
        Layout(designer);
        Layout(bar);

        foreach (var (type, component) in new (Type, FrameworkElement)[] { (typeof(ReportDesignerSlots), designer), (typeof(FilterBarSlots), bar) })
        {
            // The slicer slot is in the template of each global filter
            foreach (var name in type.GetFields().Select(f => (string)f.GetValue(null)!).Where(n => n != FilterBarSlots.Slicer))
                Assert.NotNull(Host(component, name));
        }
    });

    private static SlotHost Host(DependencyObject root, string name) =>
        Descendants(root).OfType<SlotHost>().SingleOrDefault(h => h.Slot == name)!;

    private static TextBlock? FindText(DependencyObject root) => Descendants(root).OfType<TextBlock>().FirstOrDefault();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
