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
        var withTooltips = VisualType.BuiltIn.Where(t => Tooltips.Has(t)).ToHashSet();

        Assert.Equal(
            new HashSet<VisualType> { VisualType.Bar, VisualType.StackedBar, VisualType.Line, VisualType.Area, VisualType.Scatter, VisualType.Pie, VisualType.Donut, VisualType.Waterfall },
            withTooltips);
    }

    // --- Types declared by an application ---

    private static VisualTypeDefinition Treemap() => new(new VisualType("test.treemap"), () => "Treemap")
    {
        Axis = new() { MinFields = 1, MaxFields = 2, Caption = () => "Groups" },
        Legend = VisualWell.Unused,
        Values = new() { MinFields = 1, MaxFields = 1, Caption = () => "Size" },
        HasTooltips = true,
    };

    [Theory]
    [InlineData("treemap")]
    [InlineData("Bar")]
    [InlineData(".treemap")]
    [InlineData("test.")]
    public void A_declared_type_has_an_id_with_a_prefix(string id) =>
        Assert.Throws<ArgumentException>(() => new VisualTypeCatalog().Add(new(new VisualType(id), () => id)));

    [Fact]
    public void A_type_is_declared_once_and_the_default_catalog_does_not_change()
    {
        var catalog = new VisualTypeCatalog().Add(Treemap());

        Assert.Throws<InvalidOperationException>(() => catalog.Add(Treemap()));
        Assert.Throws<InvalidOperationException>(() => VisualTypeCatalog.Default.Add(Treemap()));
        Assert.Throws<InvalidOperationException>(() => VisualTypeCatalog.Default.Hide(VisualType.Bar));
        Assert.Equal("test.treemap", catalog.Definitions[^1].Type.Id);
        Assert.False(VisualTypeCatalog.Default.Contains(Treemap().Type));
    }

    [Fact]
    public void An_application_changes_or_hides_a_type_of_easybi()
    {
        var catalog = new VisualTypeCatalog();

        catalog.Replace(catalog.Get(VisualType.Bar) with { Caption = () => "Columns" });
        catalog.Hide(VisualType.Waterfall);

        Assert.Equal("Columns", catalog.Get(VisualType.Bar).Caption());
        Assert.Null(catalog.Get(VisualType.Waterfall).Group);
        Assert.NotNull(catalog.Get(VisualType.Waterfall).Validate); // its rules stay
        Assert.Throws<InvalidOperationException>(() => catalog.Replace(Treemap()));
    }

    [Fact]
    public void A_visual_of_a_declared_type_is_checked_against_its_wells()
    {
        var catalog = new VisualTypeCatalog().Add(Treemap());
        var visual = new VisualDefinition { Type = Treemap().Type, Model = "sales", Values = [new("Amount")] };
        string? Validate(VisualDefinition v) => VisualValidator.Validate(v, catalog);

        Assert.Equal("Add a field to Groups", Validate(visual));
        Assert.Null(Validate(visual with { Axis = [new("Region")] }));
        Assert.Equal("Groups holds at most 2 fields", Validate(visual with { Axis = [new("Region"), new("Product"), new("Channel")] }));
        Assert.Equal("Legend is not used by this type of visual", Validate(visual with { Axis = [new("Region")], Legend = new("Channel") }));
        Assert.Equal("Size holds a single field", Validate(visual with { Axis = [new("Region")], Values = [new("Amount"), new("Count")] }));
    }

    [Fact]
    public void A_rule_of_the_type_comes_before_the_check_of_its_wells()
    {
        var catalog = new VisualTypeCatalog().Add(Treemap() with { Validate = v => v.Title == "" ? "Name the treemap" : null });

        Assert.Equal("Name the treemap", VisualValidator.Validate(new VisualDefinition { Type = Treemap().Type, Values = [new("Amount")] }, catalog));
    }

    [Fact]
    public void The_tooltip_measures_of_a_declared_type_are_queried_with_it()
    {
        var catalog = new VisualTypeCatalog().Add(Treemap());
        var visual = new VisualDefinition
        {
            Type = Treemap().Type,
            Axis = [new("Region")],
            Values = [new("Amount")],
            Tooltip = new TooltipSettings { Values = [new("Count")] },
        };

        Assert.Equal(["Amount", "Count"], visual.ToQuery(types: catalog).Measures.Select(m => m.Measure));
        Assert.Equal(["Amount"], visual.ToQuery().Measures.Select(m => m.Measure)); // unknown: no tooltips
    }

    [Fact]
    public void The_settings_of_a_type_are_saved_under_its_id()
    {
        var visual = new VisualDefinition { Type = Treemap().Type }.WithRendererOption("test.treemap", "labels", false);

        Assert.False(visual.GetTypeOption("labels", true));
        Assert.True(new VisualDefinition { Type = VisualType.Bar }.GetTypeOption("labels", true));
    }
}
