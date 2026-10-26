using System.Windows;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardSelectionTests
{
    private static VisualDefinition Text(string id) => new() { Id = id, Type = VisualType.Text, Text = new TextContent { Text = id } };

    private static DashboardView View(params string[] ids)
    {
        var view = new DashboardView();
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = ids.Select(Text).ToArray() } });
        return view;
    }

    private static DashboardItem Item(DashboardView view, string id) => view.Items.Single(i => i.Visual?.Id == id);

    private static string?[] Selected(DashboardView view) => view.SelectedItems.Select(i => i.Visual?.Id).ToArray();

    // Shows the view in a window, behaviors attached, for the mouse and keyboard tests
    private static void Shown(DashboardView view, Action test)
    {
        var window = new Window { Content = view, Width = 900, Height = 900, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.UpdateLayout();
            test();
        }
        finally
        {
            window.Close();
        }
    }

    // Whether the dashboard took the key
    private static bool Press(DashboardView view, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(view)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        view.RaiseEvent(args);
        return args.Handled;
    }

    // WPF raises the left button events from the generic mouse down
    private static void Click(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });

    [Fact]
    public void Items_are_selected_replaced_added_or_toggled() => Sta.Run(() =>
    {
        var view = View("a", "b", "c");
        var changes = 0;
        view.SelectionChanged += (_, _) => changes++;

        view.Select(Item(view, "a"));
        view.Select([Item(view, "b"), Item(view, "c")], DashboardSelectionMode.Add);
        view.Select([Item(view, "a"), Item(view, "b")], DashboardSelectionMode.Toggle);

        Assert.Equal(["c"], Selected(view));
        Assert.Equal([false, false, true], view.Items.Select(i => i.IsSelected));
        Assert.Equal(3, changes);

        view.Select(Item(view, "c")); // no change, no event
        Assert.Equal(3, changes);
    });

    [Fact]
    public void Select_all_follows_the_reading_order_and_unselect_all_empties() => Sta.Run(() =>
    {
        var view = View("a", "b", "c");

        view.SelectAll();
        Assert.Equal(["a", "b", "c"], Selected(view));

        view.UnselectAll();
        Assert.Empty(view.SelectedItems);
        Assert.All(view.Items, i => Assert.False(i.IsSelected));
    });

    [Fact]
    public void Removing_the_selection_is_one_change_and_respects_a_veto() => Sta.Run(() =>
    {
        var view = View("a", "b", "c");
        var changes = 0;
        view.Changed += (_, _) => changes++;
        view.ItemRemoving += (_, e) => e.Cancel = e.Item.Visual?.Id == "b";
        view.Select([Item(view, "a"), Item(view, "b")]);

        Assert.Equal(1, view.RemoveSelected());

        Assert.Equal(["b", "c"], view.Items.Select(i => i.Visual!.Id));
        Assert.Equal(["b"], Selected(view)); // the removed item left the selection
        Assert.Equal(1, changes);
    });

    [Fact]
    public void Removing_a_visual_clears_its_cross_filtering_selection_only() => Sta.Run(() =>
    {
        var view = View("a", "b");
        var filters = new EasyBI.Core.FilterContext();
        VisualView.SetFilterContext(view, filters);
        RenderRequest Clicked(string id) => new(
            new VisualDefinition { Id = id, Model = "sales", Type = VisualType.Bar, Axis = [new("Region")] },
            new ResultSet([], []),
            new ChartData(null, ["North"], []) { CategoryValues = [["North"]] },
            BiTheme.Default);
        filters.Select(Clicked("a"), new CategorySelection(0));
        filters.Select(Clicked("b"), new CategorySelection(0), extend: true);

        view.Remove(Item(view, "a"));

        Assert.Equal("b", Assert.Single(filters.Selections).SourceVisualId);
    });

    [Fact]
    public void Duplicating_the_selection_copies_the_visuals_next_to_them_and_selects_the_copies() => Sta.Run(() =>
    {
        var view = View("a", "b");
        var changes = 0;
        view.Changed += (_, _) => changes++;
        view.Select(Item(view, "a"));

        var copy = Assert.Single(view.DuplicateSelected());

        Assert.NotEqual("a", copy.Visual!.Id);
        Assert.Equal(Item(view, "a").Visual! with { Id = copy.Visual!.Id }, copy.Visual);
        Assert.Equal([copy], view.SelectedItems);
        Assert.Equal(1, changes);
        // Next to its original
        var line = Assert.Single(view.Capture().Layout!.Bands[0].Items, n => n.Items().Any(i => i.VisualId == "a"));
        Assert.Equal(["a", copy.Visual.Id, "b"], line.Items().Select(i => i.VisualId));
    });

    [Fact]
    public void Selection_commands_need_a_selection_and_an_editable_dashboard() => Sta.Run(() =>
    {
        var view = View("a", "b");
        Assert.False(ApplicationCommands.Delete.CanExecute(null, view));

        view.Select(Item(view, "a"));
        Assert.True(ApplicationCommands.Delete.CanExecute(null, view));
        Assert.True(DashboardCommands.Duplicate.CanExecute(null, view));

        ApplicationCommands.Delete.Execute(null, view);
        Assert.Equal(["b"], view.Items.Select(i => i.Visual!.Id));

        view.Select(Item(view, "b"));
        view.IsReadOnly = true;
        Assert.Empty(view.SelectedItems); // a read-only dashboard has no selection
        ApplicationCommands.SelectAll.Execute(null, view);
        Assert.Empty(view.SelectedItems);
    });

    [Fact]
    public void Loading_another_document_drops_the_items_that_left() => Sta.Run(() =>
    {
        var view = View("a", "b");
        view.SelectAll();

        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = [Text("b")] } });

        Assert.Equal(["b"], Selected(view));
    });

    [Fact]
    public void A_click_selects_an_item_and_a_click_on_the_background_unselects() => Sta.Run(() =>
    {
        var view = View("a", "b");
        Shown(view, () =>
        {
            Click(Item(view, "b"));
            Assert.Equal(["b"], Selected(view));

            Click(view.Panel);
            Assert.Empty(view.SelectedItems);
        });
    });

    private static void Release(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });

    [Fact]
    public void Ctrl_click_adds_or_removes_an_item_when_the_button_is_released() => Sta.Run(() =>
    {
        var view = View("a", "b");
        Shown(view, () =>
        {
            view.Select(Item(view, "a"));
            using var ctrl = ModifierKeysState.Simulate(ModifierKeys.Control);

            Click(Item(view, "b"));
            Assert.Equal(["a"], Selected(view));
            Release(Item(view, "b"));
            Assert.Equal(["a", "b"], Selected(view));

            Click(Item(view, "a"));
            Release(Item(view, "a"));
            Assert.Equal(["b"], Selected(view));
        });
    });

    [Fact]
    public void Ctrl_click_on_an_element_of_a_chart_leaves_the_items_selection_to_the_cross_filtering() => Sta.Run(() =>
    {
        var view = View("a", "b");
        Shown(view, () =>
        {
            view.Select(Item(view, "a"));
            using var ctrl = ModifierKeysState.Simulate(ModifierKeys.Control);

            Click(Item(view, "b"));
            // The chart of b reports a picked bar during the click
            Item(view, "b").RaiseEvent(new RoutedEventArgs(VisualView.DataSelectedEvent));
            Release(Item(view, "b"));

            Assert.Equal(["a"], Selected(view));
        });
    });

    [Fact]
    public void Arrows_go_to_the_nearest_item_and_escape_unselects() => Sta.Run(() =>
    {
        var view = View("a", "b", "c");
        Shown(view, () =>
        {
            // a and b side by side, c below them
            view.Select(Item(view, "a"));

            Press(view, Key.Right);
            Assert.Equal(["b"], Selected(view));
            Press(view, Key.Down);
            Assert.Equal(["c"], Selected(view));
            Press(view, Key.Up); // a and b are as near: the first one
            Assert.Equal(["a"], Selected(view));
            // Nothing above: the selection stays, and the key does not move the focus to another control
            Assert.True(Press(view, Key.Up));
            Assert.Equal(["a"], Selected(view));

            Press(view, Key.Escape);
            Assert.Empty(view.SelectedItems);
        });
    });

    [Fact]
    public void An_arrow_goes_to_the_item_facing_it_before_a_better_aligned_one_further_away() => Sta.Run(() =>
    {
        // a | b
        // c (whole width)
        // f | e
        var view = new DashboardView();
        view.Load(new DashboardDocument
        {
            Report = new ReportDefinition { Visuals = new[] { "a", "b", "c", "f", "e" }.Select(Text).ToArray() },
            Layout = new DashboardLayoutDefinition
            {
                Bands =
                [
                    new BandLayout
                    {
                        Items =
                        [
                            LayoutNode.Row(LayoutNode.Item("a"), LayoutNode.Item("b")),
                            LayoutNode.Item("c"),
                            LayoutNode.Row(LayoutNode.Item("f"), LayoutNode.Item("e")),
                        ],
                    },
                ],
            },
        });
        Shown(view, () =>
        {
            view.Select(Item(view, "b"));

            Press(view, Key.Down);
            Assert.Equal(["c"], Selected(view)); // right below b, although e is straight below its centre
            Press(view, Key.Down);
            Assert.Equal(["f"], Selected(view)); // f and e face c as much: the first one
            Press(view, Key.Right);
            Assert.Equal(["e"], Selected(view));
        });
    });

    [Fact]
    public void The_selected_item_moves_one_step_as_one_change_and_stays_selected() => Sta.Run(() =>
    {
        var view = View("a", "b", "c"); // a | b, then c
        var changes = 0;
        view.Changed += (_, _) => changes++;
        view.Select(Item(view, "b"));

        Assert.True(view.MoveSelected(ItemSide.Below));

        var lines = view.Capture().Layout!.Bands[0].Items.Select(l => string.Join(",", l.Items().Select(i => i.VisualId))).ToArray();
        Assert.Equal(["a", "b", "c"], lines);
        Assert.Equal(["b"], Selected(view));
        Assert.Equal(1, changes);
        Assert.False(view.MoveSelected(ItemSide.Right)); // alone on its line: nothing to swap with

        view.SelectAll();
        Assert.False(view.MoveSelected(ItemSide.Below)); // one item at a time
    });

    [Fact]
    public void Selected_items_extend_over_the_free_room_as_one_change() => Sta.Run(() =>
    {
        var view = View("a", "b"); // a | b
        DashboardLayout.Resize(Item(view, "a"), 3, null);
        DashboardLayout.Resize(Item(view, "b"), 4, null);
        var changes = 0;
        view.Changed += (_, _) => changes++;
        view.Select(Item(view, "b"));

        Assert.True(view.ExtendSelected(width: true));
        Assert.False(DashboardLayout.IsConstrained(Item(view, "b")));
        Assert.Equal(1, changes);
        Assert.False(view.ExtendSelected(width: true)); // no free room left
    });

    [Fact]
    public void Layout_commands_take_a_direction_and_act_on_the_selection() => Sta.Run(() =>
    {
        var view = View("a", "b"); // a | b
        Assert.False(DashboardCommands.MoveItem.CanExecute(ItemSide.Below, view)); // nothing selected

        view.Select(Item(view, "b"));
        Assert.False(DashboardCommands.MoveItem.CanExecute("sideways", view));
        Assert.False(DashboardCommands.MoveItemToNewBand.CanExecute(ItemSide.Left, view)); // a new band is above or below

        DashboardCommands.MoveItem.Execute("Below", view); // by name, as in XAML
        Assert.Equal(2, view.Capture().Layout!.Bands[0].Items.Count);
        DashboardCommands.MoveItemToNewBand.Execute(ItemSide.Above, view);
        Assert.Equal(2, view.Capture().Layout!.Bands.Count);

        view.SelectAll();
        Assert.False(DashboardCommands.MoveItem.CanExecute(ItemSide.Below, view)); // one item at a time
        Assert.True(DashboardCommands.ExtendItem.CanExecute(ItemSide.Right, view)); // all of them

        view.IsReadOnly = true;
        Assert.False(DashboardCommands.ExtendItem.CanExecute(ItemSide.Right, view));
    });

    [Fact]
    public void Keyboard_layout_is_a_default_behavior_of_its_own() => Sta.Run(() =>
        Assert.Contains(DashboardView.CreateDefaultBehaviors(), b => b is Dashboard.Behaviors.KeyboardLayoutBehavior));
}
