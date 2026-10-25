using System.Windows;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.DragAndDrop;

namespace EasyBI.Wpf.Tests;

public class ShelfDragDropTests
{
    private sealed record Sale(string Region, string Channel, string Product, string Store, DateTime Date, decimal Amount, int Quantity);

    private static DesignerViewModel Designer() =>
        new(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("North", "Web", "A", "S1", new(2024, 1, 1), 1m, 1) }, "sales")
                .Dimension(s => s.Region, "Region")
                .Dimension(s => s.Channel, "Channel")
                .Dimension(s => s.Product, "Product")
                .Dimension(s => s.Store, "Store")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "Revenue")
                .Measure(s => s.Quantity, "Units")));

    private static FieldDescriptor Field(DesignerViewModel designer, string name) =>
        designer.Dimensions.Concat(designer.Measures).Single(f => f.Name == name);

    private static string[] Names(IEnumerable<ShelfItemViewModel> items) => items.Select(i => i.Field.Name).ToArray();

    private static DragDropEffects DragOver(DesignerViewModel designer, object item, Shelf shelf, int index = 0) =>
        ((IDropHandler)designer).DragOver(new DropInfo { Item = item, Key = shelf, Index = index });

    [Fact]
    public void A_field_dropped_between_two_chips_is_inserted_there()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);
        designer.Drop(Field(designer, "Product"), Shelf.Axis);

        ((IDropHandler)designer).Drop(new DropInfo { Item = Field(designer, "Channel"), Key = Shelf.Axis, Index = 1 });

        Assert.Equal(["Region", "Channel", "Product"], Names(designer.Axis));
        Assert.Equal(["Region", "Channel", "Product"], designer.Visual!.Axis.Select(a => a.Field));
    }

    [Theory]
    [InlineData(0, new[] { "Product", "Region", "Channel" })]
    [InlineData(1, new[] { "Region", "Product", "Channel" })]
    [InlineData(3, new[] { "Region", "Channel", "Product" })] // on its own place: unchanged
    public void A_chip_is_reordered_within_its_shelf(int index, string[] expected)
    {
        var designer = Designer();
        foreach (var name in new[] { "Region", "Channel", "Product" }) designer.Drop(Field(designer, name), Shelf.Axis);

        designer.Drop(designer.Axis[2], Shelf.Axis, index);

        Assert.Equal(expected, Names(designer.Axis));
    }

    [Fact]
    public void A_full_axis_still_reorders_its_chips()
    {
        var designer = Designer();
        foreach (var name in new[] { "Region", "Channel", "Product" }) designer.Drop(Field(designer, name), Shelf.Axis);

        Assert.Equal(DragDropEffects.None, DragOver(designer, Field(designer, "Store"), Shelf.Axis));
        Assert.Equal(DragDropEffects.Move, DragOver(designer, designer.Axis[0], Shelf.Axis, 3));
    }

    [Fact]
    public void A_dimension_moves_from_axis_to_legend_with_its_granularity()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);
        designer.Drop(Field(designer, "Date"), Shelf.Axis);
        designer.Drop(Field(designer, "Channel"), Shelf.Legend);
        designer.Axis[1].Grain = DateGrain.Quarter;

        designer.Drop(designer.Axis[1], Shelf.Legend);

        Assert.Equal(["Region"], Names(designer.Axis));
        var legend = Assert.Single(designer.Legend); // replaces the previous legend
        Assert.Equal(("Date", Shelf.Legend, DateGrain.Quarter), (legend.Field.Name, legend.Shelf, legend.Grain));
        Assert.Equal(new FieldRef("Date", DateGrain.Quarter), designer.Visual!.Legend);
    }

    [Fact]
    public void The_legend_moves_to_the_axis_only_when_there_is_room()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);
        designer.Drop(Field(designer, "Channel"), Shelf.Legend);

        designer.Drop(designer.Legend[0], Shelf.Axis, 0);

        Assert.Equal(["Channel", "Region"], Names(designer.Axis));
        Assert.Empty(designer.Legend);

        designer.Drop(Field(designer, "Product"), Shelf.Axis);
        designer.Drop(Field(designer, "Store"), Shelf.Legend);
        Assert.Equal(DragDropEffects.None, DragOver(designer, designer.Legend[0], Shelf.Axis));
    }

    [Fact]
    public void Measures_and_filters_stay_on_their_shelf()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Amount"), Shelf.Values);
        designer.Drop(Field(designer, "Quantity"), Shelf.Values);
        designer.Drop(Field(designer, "Channel"), Shelf.Filters);
        designer.Drop(Field(designer, "Region"), Shelf.Filters);

        Assert.Equal(DragDropEffects.None, DragOver(designer, designer.Values[0], Shelf.Axis));
        Assert.Equal(DragDropEffects.None, DragOver(designer, designer.Filters[0], Shelf.Legend));

        designer.Drop(designer.Values[1], Shelf.Values, 0);
        designer.Drop(designer.Filters[1], Shelf.Filters, 0);

        Assert.Equal(["Quantity", "Amount"], Names(designer.Values));
        Assert.Equal(["Region", "Channel"], designer.Filters.Select(f => f.Field.Name));
    }

    [Fact]
    public void Fields_are_copied_and_chips_are_moved()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);

        Assert.Equal(DragDropEffects.Copy, DragOver(designer, Field(designer, "Channel"), Shelf.Legend));
        Assert.Equal(DragDropEffects.Move, DragOver(designer, designer.Axis[0], Shelf.Legend));
        Assert.Equal(DragDropEffects.None, DragOver(designer, "not a field", Shelf.Axis));
        Assert.Equal(DragDropEffects.None, ((IDropHandler)designer).DragOver(new DropInfo { Item = Field(designer, "Channel") }));
    }

    [Fact]
    public void A_chip_from_another_designer_is_refused()
    {
        var designer = Designer();
        var other = Designer();
        other.Drop(Field(other, "Region"), Shelf.Axis);

        Assert.Equal(DragDropEffects.None, DragOver(designer, other.Axis[0], Shelf.Axis));
    }

    private static string? Refusal(DesignerViewModel designer, object item, Shelf shelf) =>
        ((IDropHandler)designer).RefusalReason(new DropInfo { Item = item, Key = shelf });

    [Fact]
    public void A_refused_field_says_why()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);
        designer.Drop(Field(designer, "Channel"), Shelf.Filters);

        Assert.Equal("Only dimensions go in Axis", Refusal(designer, Field(designer, "Amount"), Shelf.Axis));
        Assert.Equal("Only measures go in Values", Refusal(designer, Field(designer, "Store"), Shelf.Values));
        Assert.Equal("Region is already in Axis", Refusal(designer, Field(designer, "Region"), Shelf.Axis));
        Assert.Equal("Channel is already in Filters", Refusal(designer, Field(designer, "Channel"), Shelf.Filters));
        Assert.Null(Refusal(designer, Field(designer, "Store"), Shelf.Axis)); // accepted
    }

    [Fact]
    public void A_full_axis_says_how_many_fields_it_holds()
    {
        var designer = Designer();
        foreach (var name in new[] { "Region", "Channel", "Product" }) designer.Drop(Field(designer, name), Shelf.Axis);

        Assert.Equal("Axis holds at most 3 fields", Refusal(designer, Field(designer, "Store"), Shelf.Axis));
    }

    [Fact]
    public void A_refused_chip_says_why()
    {
        var designer = Designer();
        designer.Drop(Field(designer, "Region"), Shelf.Axis);
        designer.Drop(Field(designer, "Amount"), Shelf.Values);
        designer.Drop(Field(designer, "Channel"), Shelf.Filters);

        Assert.Equal("Only dimensions go in Axis", Refusal(designer, designer.Values[0], Shelf.Axis));
        Assert.Equal("To filter on Region, drag it from the field list", Refusal(designer, designer.Axis[0], Shelf.Filters));
        Assert.Equal("A filter stays in Filters", Refusal(designer, designer.Filters[0], Shelf.Legend));
    }

    [Fact]
    public void Nothing_is_said_about_a_chip_of_another_designer()
    {
        var designer = Designer();
        var other = Designer();
        other.Drop(Field(other, "Region"), Shelf.Axis);

        Assert.Null(Refusal(designer, other.Axis[0], Shelf.Axis));
    }
}
