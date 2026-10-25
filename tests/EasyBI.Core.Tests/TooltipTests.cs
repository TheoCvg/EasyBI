using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Chart tooltips: their default content, the visual's settings, extra measures and template texts.</summary>
public class TooltipTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount, int Quantity);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Web", 100m, 1),
        new("Nord", "Magasin", 300m, 2),
        new("Sud", "Web", 300m, 3),
        new("Sud", "Magasin", 100m, 4),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA")
            .Measure(s => s.Quantity, "Quantité");

    private static readonly VisualDefinition ByRegionAndChannel = new()
    {
        Model = "sales",
        Type = VisualType.Bar,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount")],
    };

    private static Task<RenderRequest> PrepareAsync(VisualDefinition visual) => VisualPipeline.PrepareAsync(Model(), visual);

    private static int Category(RenderRequest request, string name) => request.Chart.Categories.ToList().IndexOf(name);

    private static int Series(RenderRequest request, string legend) =>
        request.Chart.Series.ToList().FindIndex(s => InvariantValue.Format(s.LegendValue) == legend);

    private static string Format(double value) => ValueFormatter.Format(value, null);

    [Fact]
    public async Task By_default_a_tooltip_shows_the_category_then_each_hovered_series_with_its_value_and_color()
    {
        var request = await PrepareAsync(ByRegionAndChannel);
        var web = Series(request, "Web");

        var content = Tooltips.Build(request, Category(request, "Sud"), [web]);

        Assert.NotNull(content);
        Assert.Equal("Sud", content.Title);
        var item = Assert.Single(content.Items);
        Assert.Equal(request.Chart.Series[web].Name, item.Label);
        Assert.Equal(Format(300), item.Value);
        Assert.Equal(request.Theme.ColorAt(web), item.Color);
        Assert.Null(content.Text);
    }

    [Fact]
    public async Task The_settings_hide_the_category_and_the_series_names_and_add_the_share_of_the_series_total()
    {
        var request = await PrepareAsync(ByRegionAndChannel with { Tooltip = new() { HideCategory = true, HideSeries = true, ShowPercent = true } });
        var web = Series(request, "Web");

        var content = Tooltips.Build(request, Category(request, "Sud"), [web]);

        Assert.NotNull(content);
        Assert.Null(content.Title);
        var item = Assert.Single(content.Items);
        Assert.Equal("", item.Label);
        // Web: 100 in Nord, 300 in Sud
        Assert.Equal($"{Format(300)} ({ValueFormatter.Format(0.75, "P1")})", item.Value);
    }

    [Fact]
    public async Task A_visual_can_hide_its_tooltips()
    {
        var request = await PrepareAsync(ByRegionAndChannel with { Tooltip = new() { IsHidden = true } });

        Assert.Null(Tooltips.Build(request, 0, [0]));
        Assert.Null(request.TooltipFor(0, [0]));
    }

    [Fact]
    public async Task Tooltip_measures_are_queried_and_shown_for_the_hovered_category_and_legend()
    {
        var request = await PrepareAsync(ByRegionAndChannel with { Tooltip = new() { Values = [new("Quantity")] } });

        // Still one series per legend value: the tooltip measure is no series of the chart
        Assert.Equal(2, request.Chart.Series.Count);
        var content = Tooltips.Build(request, Category(request, "Sud"), [Series(request, "Magasin")]);

        Assert.NotNull(content);
        Assert.Equal(2, content.Items.Count);
        Assert.Equal(new TooltipItem("Quantité", Format(4)), content.Items[1]);
    }

    [Fact]
    public async Task A_template_text_fills_in_its_fields_and_keeps_unknown_ones_as_typed()
    {
        var request = await PrepareAsync(ByRegionAndChannel with
        {
            Tooltip = new() { Values = [new("Quantity")], Template = "{category}: {value} for {Quantité} sales ({unknown})" },
        });

        var content = Tooltips.Build(request, Category(request, "Nord"), [Series(request, "Web")]);

        Assert.NotNull(content);
        Assert.Equal($"Nord: {Format(100)} for {Format(1)} sales ({{unknown}})", content.Text);
    }

    [Fact]
    public async Task A_template_can_name_a_measure_by_its_name_or_its_caption()
    {
        var request = await PrepareAsync(ByRegionAndChannel with { Legend = null, Tooltip = new() { Template = "{Amount} = {CA}" } });

        var content = Tooltips.Build(request, Category(request, "Nord"), [0]);

        Assert.Equal($"{Format(400)} = {Format(400)}", content?.Text);
    }

    [Fact]
    public async Task A_host_replaces_the_default_content()
    {
        var request = await PrepareAsync(ByRegionAndChannel);
        var custom = new TooltipContent("Custom", []);

        Assert.Same(custom, (request with { Tooltip = (_, _) => custom }).TooltipFor(0, [0]));
        Assert.Equal(Tooltips.Build(request, 0, [0])?.Title, request.TooltipFor(0, [0])?.Title);
    }

    [Fact]
    public void Only_charts_query_the_tooltip_measures()
    {
        var tooltip = new TooltipSettings { Values = [new("Quantity")] };

        Assert.DoesNotContain(ByRegionAndChannel.ToQuery().Measures, m => m.Measure == "Quantity");
        Assert.DoesNotContain((ByRegionAndChannel with { Type = VisualType.Table, Tooltip = tooltip }).ToQuery().Measures, m => m.Measure == "Quantity");
        Assert.Contains((ByRegionAndChannel with { Tooltip = tooltip }).ToQuery().Measures, m => m.Measure == "Quantity");
    }

    [Fact]
    public void Tooltip_settings_are_saved_with_the_visual_and_omitted_when_there_are_none()
    {
        var visual = ByRegionAndChannel with
        {
            Tooltip = new() { HideSeries = true, ShowPercent = true, Values = [new("Quantity")], Template = "{category}" },
        };

        var json = JsonSerializer.Serialize(visual, ReportSerializer.Options);
        var read = JsonSerializer.Deserialize<VisualDefinition>(json, ReportSerializer.Options)!;

        Assert.True(read.Tooltip!.HideSeries);
        Assert.True(read.Tooltip.ShowPercent);
        Assert.False(read.Tooltip.IsHidden);
        Assert.Equal("Quantity", Assert.Single(read.Tooltip.Values).Measure);
        Assert.Equal("{category}", read.Tooltip.Template);
        Assert.DoesNotContain("tooltip", JsonSerializer.Serialize(ByRegionAndChannel, ReportSerializer.Options));
    }

    [Fact]
    public async Task The_total_of_a_waterfall_has_a_tooltip()
    {
        var request = await PrepareAsync(new VisualDefinition { Model = "sales", Type = VisualType.Waterfall, Axis = [new("Region")], Values = [new("Amount")] });

        var total = Tooltips.Build(request, request.Chart.Categories.Count, [0]);

        Assert.NotNull(total);
        Assert.Equal(Format(800), Assert.Single(total.Items).Value);
    }
}
