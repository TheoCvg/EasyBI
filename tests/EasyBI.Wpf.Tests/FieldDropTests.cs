using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Tests;

/// <summary>Fields dragged from a field list: visuals made from them, the designer, and where they land on a dashboard.</summary>
public class FieldDropTests
{
    private sealed record Sale(string Region, string Channel, DateTime Date, decimal Amount, int Quantity);

    private static ISemanticModel Sales(string name = "sales") =>
        SemanticModel.For(new[] { new Sale("North", "Web", new(2024, 1, 1), 1m, 1) }, name)
            .Dimension(s => s.Region, "Region")
            .Dimension(s => s.Channel, "Channel")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "Revenue")
            .Measure(s => s.Quantity, "Units");

    private static readonly ISemanticModel Model = Sales();

    private static FieldDescriptor Field(string name) => Model.Metadata.GetField(name);

    // --- Visuals made from a field ---

    [Fact]
    public void A_measure_makes_a_kpi()
    {
        var visual = FieldVisuals.Create(Model, Field("Amount"));

        Assert.Equal((MarkType.Kpi, "sales", "Revenue"), (visual.Mark, visual.Model, visual.Title));
        Assert.Equal("Amount", Assert.Single(visual.Values).Measure);
    }

    [Fact]
    public void A_dimension_makes_bars_of_the_first_measure_and_a_date_a_line_by_month()
    {
        var bars = FieldVisuals.Create(Model, Field("Region"));
        var line = FieldVisuals.Create(Model, Field("Date"));

        Assert.Equal((MarkType.Bar, "Revenue by Region"), (bars.Mark, bars.Title));
        Assert.Equal(new FieldRef("Region"), Assert.Single(bars.Axis));
        Assert.Equal("Amount", Assert.Single(bars.Values).Measure);
        Assert.Equal(MarkType.Line, line.Mark);
        Assert.Equal(new FieldRef("Date", DateGrain.Month), Assert.Single(line.Axis));
    }

    [Fact]
    public void A_dimension_of_a_model_without_measures_makes_a_table()
    {
        var model = SemanticModel.For(new[] { new Sale("North", "Web", default, 0, 0) }, "lists").Dimension(s => s.Region, "Region");

        var visual = FieldVisuals.Create(model, model.Metadata.GetField("Region"));

        Assert.Equal(MarkType.Table, visual.Mark);
        Assert.Empty(visual.Values);
    }

    [Fact]
    public void Dimensions_dropped_on_a_chart_go_on_the_axis_then_in_the_legend()
    {
        var visual = new VisualDefinition { Model = "sales", Values = [new MeasureRef("Amount")] };

        visual = FieldVisuals.AddField(visual, Model, Field("Region"))!;
        Assert.Equal(["Region"], visual.Axis.Select(a => a.Field));
        visual = FieldVisuals.AddField(visual, Model, Field("Channel"))!;
        Assert.Equal("Channel", visual.Legend!.Field);
        visual = FieldVisuals.AddField(visual, Model, Field("Date"))!;
        Assert.Equal(["Region", "Date"], visual.Axis.Select(a => a.Field));

        Assert.Null(FieldVisuals.AddField(visual, Model, Field("Region"))); // already shown
    }

    [Fact]
    public void Measures_dropped_on_a_visual_join_its_values()
    {
        var kpi = FieldVisuals.Create(Model, Field("Amount"));

        var both = FieldVisuals.AddField(kpi, Model, Field("Quantity"))!;

        Assert.Equal(["Amount", "Quantity"], both.Values.Select(v => v.Measure));
        Assert.Null(FieldVisuals.AddField(both, Model, Field("Amount")));
        Assert.Null(FieldVisuals.AddField(kpi with { Mark = MarkType.Pie }, Model, Field("Quantity"))); // a pie shows one measure
    }

    [Fact]
    public void A_dimension_dropped_on_a_kpi_breaks_it_down()
    {
        var visual = FieldVisuals.AddField(FieldVisuals.Create(Model, Field("Amount")), Model, Field("Region"))!;

        Assert.Equal(MarkType.Bar, visual.Mark);
        Assert.Equal("Region", Assert.Single(visual.Axis).Field);
    }

    [Fact]
    public void Texts_images_and_visuals_of_another_model_refuse_fields()
    {
        var text = new VisualDefinition { Mark = MarkType.Text, Text = new TextContent() };
        var other = FieldVisuals.Create(Sales("other"), Field("Amount"));

        Assert.Null(FieldVisuals.AddField(text, Model, Field("Region")));
        Assert.Null(FieldVisuals.AddField(other, Model, Field("Region")));
    }

    // --- The designer ---

    [Fact]
    public void The_designer_takes_fields_of_its_model_from_a_field_list()
    {
        var context = EasyBIContext.Create().AddModel(Model).AddModel(Sales("other"));
        var designer = new DesignerViewModel(context);
        var handler = (IDropHandler)designer;
        var region = new ModelField(Model, Field("Region"));
        var foreign = new ModelField(context.GetModel("other"), context.GetModel("other").Metadata.GetField("Region"));

        Assert.Equal(DragDropEffects.Copy, handler.DragOver(new DropInfo { Item = region, Key = Shelf.Axis }));
        Assert.Equal(DragDropEffects.None, handler.DragOver(new DropInfo { Item = foreign, Key = Shelf.Axis }));
        Assert.Equal($"{foreign.Caption} belongs to the model “{foreign.Model.Metadata.Caption}”: choose this model first",
            handler.RefusalReason(new DropInfo { Item = foreign, Key = Shelf.Axis }));

        handler.Drop(new DropInfo { Item = region, Key = Shelf.Axis });
        designer.AddFieldCommand.Execute(new ModelField(Model, Field("Amount")));

        Assert.Equal("Region", Assert.Single(designer.Visual!.Axis).Field);
        Assert.Equal("Amount", Assert.Single(designer.Visual.Values).Measure);
    }

    // --- Where a dropped item lands ---

    private static LayoutNode I(string id, int? columns = null) => new() { Id = id, VisualId = id, ColumnSpan = columns };

    [Fact]
    public void Drop_targets_are_found_from_points() => Sta.Run(() =>
    {
        // Row(a, b) then Row(x:6) and its free space, then a wrapping band
        var panel = new DashboardPanel { Gap = 10, BandSpacing = 20 };
        var children = new Dictionary<string, Border>();
        DashboardLayout.SetLayout(panel, new DashboardLayoutDefinition
        {
            RowHeight = 100,
            Bands =
            [
                new BandLayout { Items = [LayoutNode.Row(I("a"), I("b")), LayoutNode.Row(I("x", columns: 6))] },
                new BandLayout { Mode = BandMode.Wrap, Items = [I("p"), I("q")] },
            ],
        }, node => children[node.VisualId!] = new Border());
        panel.Measure(new Size(1190, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 1190, panel.DesiredSize.Height));
        var geometry = panel.CaptureGeometry();
        DashboardDropTarget? At(double x, double y) => DashboardDropTarget.Find(geometry, panel, new Point(x, y), 40);

        Assert.Equal(new DashboardDropTarget(DashboardDropKind.NextTo, 0, 0, "a", ItemSide.Right), At(588, 100));
        Assert.Equal(new DashboardDropTarget(DashboardDropKind.Line, 0, 1), At(300, 325));
        Assert.Equal(new DashboardDropTarget(DashboardDropKind.NextTo, 0, 0, "x", ItemSide.Right), At(900, 400));
        Assert.Null(At(300, 160)); // middle of a: the dragged field may go into it
        var flow = At(700, panel.GetBandBounds(1).Top + 50)!;
        Assert.Equal((DashboardDropKind.Flow, 1, 1), (flow.Kind, flow.Band, flow.Index));

        var added = new Border();
        At(588, 100)!.Add(panel, added);
        Assert.Equal([children["a"], added, children["b"]], panel.ChildrenOf(0).Take(3));
    });

    [Fact]
    public void A_view_adds_an_empty_item_where_a_target_puts_it() => Sta.Run(() =>
    {
        var view = new DashboardView();
        view.Load(new DashboardDocument { Report = new ReportDefinition { Visuals = [new VisualDefinition { Id = "a", Model = "sales" }] } });

        var item = view.AddAt(new DashboardDropTarget(DashboardDropKind.Line, 0, 0));

        Assert.Equal(DashboardItemKind.Empty, item.Kind);
        Assert.Same(item, view.Panel.ChildrenOf(0)[0]);
    });
}
