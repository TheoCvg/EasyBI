using System.Windows;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

/// <summary>Copy and paste go through data objects here: the tests never touch the real clipboard.</summary>
public class DashboardCopyPasteTests
{
    private sealed record Sale(string Region, decimal Amount, decimal Margin);

    private static readonly CalculatedMeasure Rate = new() { Name = "calc_rate", Caption = "Rate", Model = "sales", Formula = "[Margin] / [Amount]" };

    private static EasyBIContext Context() => EasyBIContext.Create().AddModel(
        SemanticModel.For(new[] { new Sale("Nord", 2m, 1m) }, "sales").Dimension(s => s.Region, "Region").Measure(s => s.Amount, "Amount").Measure(s => s.Margin, "Margin"));

    private static VisualDefinition Text(string id) => new() { Id = id, Title = id, Type = VisualType.Text, Text = new TextContent { Text = id } };

    private static VisualDefinition Chart(string id, string measure = "Amount") => new() { Id = id, Title = id, Model = "sales", Axis = [new("Region")], Values = [new(measure)] };

    private static DashboardView View(EasyBIContext? context, params VisualDefinition[] visuals)
    {
        var view = new DashboardView { Context = context };
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = visuals } });
        return view;
    }

    private static DashboardItem Item(DashboardView view, string id) => view.Items.Single(i => i.Visual?.Id == id);

    private static string?[] Titles(DashboardView view) => view.Capture().Report.Visuals.Select(v => v.Title).ToArray();

    private static string Shape(LayoutNode node) => node.IsItem
        ? node.VisualId + (node.ColumnSpan is { } c ? $":{c}" : "")
        : (node.Split == SplitDirection.Row ? "Row(" : "Col(") + string.Join(",", node.Children.Select(Shape)) + ")";

    // The lines of the first band, items named by their titles
    private static string Lines(DashboardView view)
    {
        var document = view.Capture();
        var titles = document.Report.Visuals.ToDictionary(v => v.Id, v => v.Title);
        LayoutNode Named(LayoutNode node) => node with { VisualId = node.VisualId is { } id ? titles[id] : null, Children = node.Children.Select(Named).ToArray() };
        return string.Join(" | ", document.Layout!.Bands[0].Items.Select(l => Shape(Named(l))));
    }

    private static void Resize(DashboardView view, string id, int columns) => DashboardLayout.Resize(Item(view, id), columns, null);

    [Fact]
    public void Pasted_items_keep_their_arrangement_and_sizes_after_the_line_of_the_selection() => Sta.Run(() =>
    {
        var view = View(null, Text("a"), Text("b"), Text("c"));
        Resize(view, "a", 4);
        view.Select([Item(view, "a"), Item(view, "b")]);
        var copy = view.CreateCopy()!;
        view.Select(Item(view, "a"));
        var changes = 0;
        view.Changed += (_, _) => changes++;

        var pasted = view.Paste(copy);

        Assert.Equal("Row(a:4,b) | Row(a:4,b) | c", Lines(view));
        Assert.Equal(2, pasted.Count);
        Assert.DoesNotContain(pasted, p => p.Visual!.Id is "a" or "b");
        Assert.Equal(pasted, view.SelectedItems);
        Assert.Equal(1, changes); // and one undo step
    });

    [Fact]
    public void Copied_visuals_without_their_layout_go_to_the_right_of_the_selection() => Sta.Run(() =>
    {
        var view = View(null, Text("a"), Text("b"));
        view.Select(Item(view, "a"));
        var copy = view.CreateCopy()!;
        var withoutLayout = new DataObject(Core.VisualTransfer.ClipboardFormat, copy.GetData(Core.VisualTransfer.ClipboardFormat));

        var pasted = Assert.Single(view.Paste(withoutLayout));

        Assert.Equal("Row(a,a,b)", Lines(view));
        Assert.Equal([pasted], view.SelectedItems);
    });

    [Fact]
    public void Without_a_selection_the_items_go_on_a_new_line_at_the_end() => Sta.Run(() =>
    {
        var source = View(null, Text("x"), Text("y"));
        source.SelectAll();
        var target = View(null, Text("a"));

        target.Paste(source.CreateCopy()!);

        Assert.Equal(["a", "x", "y"], Titles(target));
        Assert.Equal("a | Row(x,y)", Lines(target));
    });

    [Fact]
    public void Visuals_pasted_into_another_dashboard_bring_their_measures_and_skip_unknown_models() => Sta.Run(() =>
    {
        var sourceContext = Context();
        var source = View(sourceContext, Chart("rate", "calc_rate"), Chart("other") with { Model = "stock" });
        sourceContext.Measures.Set(Rate); // created in the dashboard (loading a report replaces the measures)
        source.SelectAll();
        var targetContext = Context();
        var target = View(targetContext, Text("a"));

        var pasted = Assert.Single(target.Paste(source.CreateCopy()!));

        Assert.Equal("rate", pasted.Visual!.Title);
        Assert.Equal([Rate], targetContext.Measures.Measures);
    });

    [Fact]
    public void Paste_needs_copied_visuals_and_an_editable_dashboard() => Sta.Run(() =>
    {
        var view = View(null, Text("a"));
        view.Select(Item(view, "a"));
        var copy = view.CreateCopy()!;
        var text = new DataObject(DataFormats.UnicodeText, "plain text");

        Assert.False(ApplicationCommands.Paste.CanExecute(text, view));
        Assert.True(ApplicationCommands.Paste.CanExecute(copy, view));
        ApplicationCommands.Paste.Execute(copy, view);
        Assert.Equal(2, view.Items.Count());

        view.IsReadOnly = true;
        Assert.False(ApplicationCommands.Paste.CanExecute(copy, view));
        Assert.Empty(view.Paste(copy));
    });

    [Fact]
    public void Empty_items_have_nothing_to_copy() => Sta.Run(() =>
    {
        var view = View(null);
        view.SelectAll();

        Assert.Null(view.CreateCopy());
    });
}
