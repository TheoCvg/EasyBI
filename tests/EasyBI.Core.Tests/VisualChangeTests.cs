using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class VisualChangeTests
{
    private static VisualDefinition Visual() => new()
    {
        Id = "v",
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Values = [new("Amount")],
        Filters = [new FilterSpec("Channel", FilterOperator.In, ["Web"])],
    };

    [Fact]
    public void Parts_of_the_visual_are_compared_by_value()
    {
        var change = new VisualChange(Visual(), Visual() with { Type = VisualType.Line });

        Assert.False(change.Changes(v => v.Axis)); // other lists, same fields
        Assert.True(change.Changes(v => v.Type));
    }

    [Fact]
    public void Settings_of_the_fields_are_not_a_change_of_the_fields()
    {
        var before = Visual();
        var settings = before with
        {
            Axis = [new("Region") { Labels = new Dictionary<string, string> { ["North"] = "N" } }],
            Values = [new("Amount", Aggregation.Average) { SecondaryAxis = true }],
            Filters = [new FilterSpec("Channel", FilterOperator.In, ["Shop"])],
            Title = "Renamed",
        };

        Assert.False(new VisualChange(before, settings).FieldsChanged);
    }

    [Fact]
    public void A_field_added_removed_or_moved_is_a_change_of_the_fields()
    {
        var before = Visual() with { Axis = [new("Region"), new("Channel")] };

        Assert.True(new VisualChange(before, before with { Values = [new("Amount"), new("Count")] }).FieldsChanged);
        Assert.True(new VisualChange(before, before with { Filters = [] }).FieldsChanged);
        Assert.True(new VisualChange(before, before with { Axis = [new("Channel"), new("Region")] }).FieldsChanged);
        Assert.True(new VisualChange(before, before with { Axis = [new("Region")], Legend = new("Channel") }).FieldsChanged);
    }
}
