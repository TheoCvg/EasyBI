using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class VisualTypeTests
{
    private static VisualType ReadType(string json) =>
        ReportSerializer.Deserialize($$"""{ "schema": "easybi/v1", "visuals": [ { "id": "v", "type": {{json}} } ] }""").Visuals[0].Type;

    [Fact]
    public void Built_in_types_are_read_whatever_their_case()
    {
        Assert.Equal(VisualType.StackedBar, ReadType("\"stackedbar\""));
        Assert.True(ReadType("\"KPI\"").IsBuiltIn);
    }

    [Fact]
    public void A_type_declared_by_an_application_round_trips_through_json()
    {
        var report = new ReportDefinition { Visuals = [new VisualDefinition { Type = new("barista.treemap") }] };

        var json = ReportSerializer.Serialize(report);

        Assert.Contains("\"type\": \"barista.treemap\"", json);
        var type = ReportSerializer.Deserialize(json).Visuals[0].Type;
        Assert.Equal(new VisualType("barista.treemap"), type);
        Assert.False(type.IsBuiltIn);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("3")]
    public void A_type_is_written_as_a_non_empty_string(string json) =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ReadType(json));

    [Fact]
    public void A_type_needs_an_id() => Assert.ThrowsAny<ArgumentException>(() => new VisualType(" "));

    [Fact]
    public void The_catalog_defines_every_built_in_type_once()
    {
        var types = VisualTypeCatalog.Default.Definitions.Select(d => d.Type).ToArray();

        Assert.Equal(VisualType.BuiltIn.Count, types.Distinct().Count());
        Assert.Equal(VisualType.BuiltIn.ToHashSet(), types.ToHashSet());
    }

    [Fact]
    public void The_types_built_from_fields_are_offered_by_the_gallery_but_not_text_and_image()
    {
        var offered = VisualTypeCatalog.Default.Definitions.Where(d => d.Group is not null).Select(d => d.Type).ToHashSet();

        Assert.Equal(VisualType.BuiltIn.Where(t => !t.IsContent()).ToHashSet(), offered);
    }

    [Fact]
    public void A_type_the_catalog_does_not_know_gets_the_default_rules()
    {
        var definition = VisualTypeCatalog.Default.Get(new("other.app"));

        Assert.Equal("other.app", definition.Caption());
        Assert.Null(definition.Group);
        Assert.Equal((true, 3), (definition.Axis.IsUsed, definition.Axis.MaxFields));
        Assert.Equal(1, definition.Values.MinFields);
        Assert.False(definition.HasTooltips);
        Assert.False(Tooltips.Has(definition.Type));
    }

    [Fact]
    public void A_visual_of_an_unknown_type_needs_a_measure_like_any_chart()
    {
        var visual = new VisualDefinition { Type = new("other.app"), Model = "sales", Axis = [new("Region")] };

        Assert.NotNull(VisualValidator.Validate(visual));
        Assert.Null(VisualValidator.Validate(visual with { Values = [new("Amount")] }));
    }

    [Fact]
    public void The_types_drawn_as_charts_have_tooltips()
    {
        var withTooltips = VisualType.BuiltIn.Where(Tooltips.Has).ToHashSet();

        Assert.Equal(
            new HashSet<VisualType> { VisualType.Bar, VisualType.StackedBar, VisualType.Line, VisualType.Area, VisualType.Scatter, VisualType.Pie, VisualType.Donut, VisualType.Waterfall },
            withTooltips);
    }
}
