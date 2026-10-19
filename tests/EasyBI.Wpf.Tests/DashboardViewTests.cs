using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Dashboard.Behaviors;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Tests;

public class DashboardViewTests
{
    private static VisualDefinition Chart(string id) => new() { Id = id, Title = id, Model = "sales", Values = [new("Amount")] };

    private static VisualDefinition Text(string id, string text = "") => new() { Id = id, Mark = MarkType.Text, Text = new TextContent { Text = text } };

    private static DashboardView View(params VisualDefinition[] visuals)
    {
        var view = new DashboardView();
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = visuals } });
        return view;
    }

    private static DashboardItem Item(DashboardView view, string visualId) => view.Items.Single(i => i.Visual?.Id == visualId);

    [Fact]
    public void Loading_a_report_without_layout_shows_every_visual() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"), Text("t", "Hello"));

        Assert.Equal(["a", "b", "t"], view.Items.Select(i => i.Visual!.Id));
        Assert.Equal(DashboardItemKind.Text, Item(view, "t").Kind);
        Assert.Equal(["a", "b", "t"], view.Capture().Report.Visuals.Select(v => v.Id));
    });

    [Fact]
    public void An_empty_dashboard_has_an_item_to_start_from() => Sta.Run(() =>
    {
        var item = Assert.Single(View().Items);

        Assert.Equal(DashboardItemKind.Empty, item.Kind);
    });

    [Fact]
    public void Reloading_keeps_items_showing_the_same_visual() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"));
        var a = Item(view, "a");

        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = [Chart("a") with { Title = "renamed" }] } });

        Assert.Same(a, Assert.Single(view.Items)); // no new rendering, no new query
        Assert.Equal("renamed", a.Visual!.Title);
    });

    [Fact]
    public void Each_change_is_reported_once_with_the_documents_before_and_after() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"));
        var changes = new List<DashboardChangedEventArgs>();
        view.Changed += (_, e) => changes.Add(e);

        DashboardLayout.Resize(Item(view, "a"), 4, null);

        var change = Assert.Single(changes);
        Assert.Null(change.Before.Layout!.Bands[0].Items[0].Children[0].ColumnSpan);
        Assert.Equal(4, change.After.Layout!.Bands[0].Items[0].Children[0].ColumnSpan);
        Assert.Same(change.After, view.Document);
    });

    [Fact]
    public void An_edit_groups_changes_and_cancelling_it_puts_the_dashboard_back() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"));
        var changes = 0;
        view.Changed += (_, _) => changes++;

        using (var edit = view.BeginEdit())
        {
            DashboardLayout.MoveTo(Item(view, "b"), 0, 0);
            DashboardLayout.Resize(Item(view, "b"), 12, 1);
            edit.Commit();
        }
        Assert.Equal(1, changes);

        using (view.BeginEdit())
        {
            DashboardLayout.Remove(Item(view, "a"));
        } // disposed without committing
        Assert.Equal(1, changes);
        Assert.Equal(["b", "a"], view.Capture().Report.Visuals.Select(v => v.Id)); // b now comes first
    });

    [Fact]
    public void Items_are_added_next_to_others_in_free_spaces_and_between_lines() => Sta.Run(() =>
    {
        var view = View(Chart("a"));
        var a = Item(view, "a");

        var right = view.AddNextTo(a, ItemSide.Right);
        Assert.Equal(DashboardItemKind.Empty, right.Kind);
        view.Remove(right);

        DashboardLayout.Resize(a, 6, null);
        view.Panel.Measure(new System.Windows.Size(1200, double.PositiveInfinity));
        view.Panel.Arrange(new System.Windows.Rect(view.Panel.DesiredSize));
        var filler = view.AddIn(Assert.Single(view.Panel.FreeSpaces));
        Assert.Equal([a, filler], view.Panel.ChildrenOf(0));

        var line = view.InsertLine(0, 0);
        Assert.Equal([line, a, filler], view.Panel.ChildrenOf(0));
    });

    [Fact]
    public void Resetting_the_size_makes_an_item_greedy_again() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"));
        var a = Item(view, "a");
        DashboardLayout.Resize(a, 4, 2);
        Assert.True(DashboardLayout.IsConstrained(a));

        DashboardCommands.ResetSize.Execute(a, view);

        Assert.False(DashboardLayout.IsConstrained(a));
    });

    [Fact]
    public void Removing_an_item_can_be_vetoed_and_never_leaves_the_dashboard_empty() => Sta.Run(() =>
    {
        var view = View(Chart("a"));
        var veto = true;
        view.ItemRemoving += (_, e) => e.Cancel = veto;

        Assert.False(view.Remove(Item(view, "a")));
        Assert.Single(view.Capture().Report.Visuals);

        veto = false;
        Assert.True(view.Remove(Item(view, "a")));
        Assert.Empty(view.Capture().Report.Visuals);
        Assert.Equal(DashboardItemKind.Empty, Assert.Single(view.Items).Kind);
    });

    [Fact]
    public void Editing_a_visual_goes_through_the_application_when_it_handles_it() => Sta.Run(() =>
    {
        var view = View();
        var item = Assert.Single(view.Items);
        view.EditVisualRequested += (_, e) =>
        {
            Assert.Null(e.Visual);
            e.Handled = true;
            e.Complete(Chart("new"));
        };

        DashboardCommands.AddVisual.Execute(item, view);

        Assert.Equal("new", item.Visual!.Id);
        Assert.Equal(["new"], view.Capture().Report.Visuals.Select(v => v.Id));
    });

    [Fact]
    public void Adding_a_text_asks_for_in_place_editing() => Sta.Run(() =>
    {
        var view = View();
        var item = Assert.Single(view.Items);
        DashboardItem? requested = null;
        view.TextEditRequested += (_, e) => requested = e.Item;

        DashboardCommands.AddText.Execute(item, view);

        Assert.Equal(DashboardItemKind.Text, item.Kind);
        Assert.Same(item, requested);
    });

    [Fact]
    public void A_picked_image_becomes_an_image_visual() => Sta.Run(() =>
    {
        var view = View();
        var item = Assert.Single(view.Items);
        view.ImageRequested += (_, e) =>
        {
            e.Handled = true;
            e.Complete(new ImageContent { Data = "AQID", Fit = ImageFit.Cover });
        };

        view.PickImage(item);

        Assert.Equal(DashboardItemKind.Image, item.Kind);
        Assert.Equal(("AQID", ImageFit.Cover), (item.Visual!.Image!.Data, item.Visual.Image.Fit));
    });

    [Fact]
    public void Read_only_dashboards_refuse_editing_commands() => Sta.Run(() =>
    {
        var view = View(Chart("a"));
        view.IsReadOnly = true;

        Assert.False(DashboardCommands.RemoveItem.CanExecute(Item(view, "a"), view));
        DashboardCommands.RemoveItem.Execute(Item(view, "a"), view);
        Assert.Single(view.Capture().Report.Visuals);
    });

    [Fact]
    public void Default_behaviors_skip_the_types_already_declared() => Sta.Run(() =>
    {
        var view = new DashboardView();
        var undo = new UndoRedoBehavior { MaxHistory = 3 };
        Interaction.GetBehaviors(view).Add(undo);

        view.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));

        var behaviors = Interaction.GetBehaviors(view);
        Assert.Equal(DashboardView.CreateDefaultBehaviors().Count, behaviors.Count);
        Assert.Same(undo, Assert.Single(behaviors.OfType<UndoRedoBehavior>()));
    });

    [Fact]
    public void Undo_and_redo_restore_previous_documents() => Sta.Run(() =>
    {
        var view = View(Chart("a"), Chart("b"));
        var undo = new UndoRedoBehavior();
        Interaction.GetBehaviors(view).Add(undo);

        DashboardLayout.Resize(Item(view, "a"), 4, null);
        view.Remove(Item(view, "b"));
        Assert.True(undo.CanUndo);

        ApplicationCommands.Undo.Execute(null, view);
        Assert.Equal(["a", "b"], view.Capture().Report.Visuals.Select(v => v.Id));
        undo.Undo();
        Assert.False(DashboardLayout.IsConstrained(Item(view, "a")));
        Assert.False(undo.CanUndo);

        undo.Redo();
        undo.Redo();
        Assert.True(DashboardLayout.IsConstrained(Item(view, "a")));
        Assert.Equal(["a"], view.Capture().Report.Visuals.Select(v => v.Id));
    });

    [Fact]
    public void Loading_another_document_starts_a_new_history() => Sta.Run(() =>
    {
        var view = View(Chart("a"));
        var undo = new UndoRedoBehavior();
        Interaction.GetBehaviors(view).Add(undo);
        DashboardLayout.Resize(Item(view, "a"), 4, null);

        view.Document = new DashboardDocument { Report = new ReportDefinition { Visuals = [Chart("b")] } };

        Assert.False(undo.CanUndo);
        Assert.Equal("b", Assert.Single(view.Items).Visual!.Id);
    });
}
