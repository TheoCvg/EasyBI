using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class RendererSettingsTests
{
    private static readonly VisualDefinition Visual = new() { Model = "sales" };

    [Fact]
    public void Options_are_stored_as_one_json_object_per_renderer()
    {
        var visual = Visual
            .WithRendererOption("LiveCharts2", "scrollable", true)
            .WithRendererOption("LiveCharts2", "style", "smooth")
            .WithRendererOption("LiveCharts2", "size", 12);

        Assert.Equal("""{"scrollable":true,"style":"smooth","size":12}""", visual.Extensions!["LiveCharts2"]);
        Assert.True(visual.GetRendererOption<bool>("LiveCharts2", "scrollable"));
        Assert.Equal("smooth", visual.GetRendererOption<string>("LiveCharts2", "style"));
        Assert.Equal(12d, visual.GetRendererOption<double>("LiveCharts2", "size"));
    }

    [Fact]
    public void An_option_that_is_not_set_reads_as_its_default()
    {
        var option = RendererOption.Number("size", () => "Size", 8);

        Assert.False(Visual.GetRendererOption<bool>("LiveCharts2", "scrollable"));
        Assert.Equal("none", Visual.GetRendererOption("LiveCharts2", "style", "none"));
        Assert.Equal(8d, Visual.GetRendererOption("LiveCharts2", option));
    }

    [Fact]
    public void A_value_of_another_type_reads_as_the_default()
    {
        var visual = Visual.WithRendererOption("LiveCharts2", "scrollable", "yes");

        Assert.True(visual.GetRendererOption("LiveCharts2", "scrollable", true));
    }

    [Fact]
    public void Removing_the_last_option_of_a_renderer_removes_its_entry()
    {
        var visual = Visual with { Extensions = new Dictionary<string, string> { ["OxyPlot"] = "smooth" } };

        var updated = visual.WithRendererOption("LiveCharts2", "scrollable", true).WithRendererOption("LiveCharts2", "scrollable", null);

        Assert.Equal(new Dictionary<string, string> { ["OxyPlot"] = "smooth" }, updated.Extensions);
        Assert.Null(Visual.WithRendererOption("LiveCharts2", "scrollable", true).WithRendererOption("LiveCharts2", "scrollable", null).Extensions);
    }

    [Fact]
    public void Options_of_other_renderers_are_kept()
    {
        var visual = Visual.WithRendererOption("OxyPlot", "smooth", true).WithRendererOption("LiveCharts2", "scrollable", true);

        Assert.True(visual.GetRendererOption<bool>("OxyPlot", "smooth"));
        Assert.True(visual.GetRendererOption<bool>("LiveCharts2", "scrollable"));
    }

    [Fact]
    public void An_entry_that_is_not_a_json_object_counts_as_no_settings()
    {
        var visual = Visual with { Extensions = new Dictionary<string, string> { ["LiveCharts2"] = "not json" } };

        Assert.False(visual.GetRendererOption<bool>("LiveCharts2", "scrollable"));
        Assert.True(visual.WithRendererOption("LiveCharts2", "scrollable", true).GetRendererOption<bool>("LiveCharts2", "scrollable"));
    }

    [Fact]
    public void Options_are_saved_with_the_report()
    {
        var report = new ReportDefinition { Visuals = [Visual.WithRendererOption("LiveCharts2", "scrollable", true)] };

        var loaded = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.True(loaded.Visuals[0].GetRendererOption<bool>("LiveCharts2", "scrollable"));
    }
}
