using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class VisualTransferTests
{
    private static readonly CalculatedMeasure Rate = new() { Name = "calc_rate", Caption = "Rate", Model = "sales", Formula = "[Margin] / [Amount]", Format = "P1" };

    private static VisualDefinition Chart(string id, string measure = "Amount", string model = "sales") =>
        new() { Id = id, Title = id, Model = model, Axis = [new("Region")], Values = [new(measure)] };

    private static MeasureContext Measures(params CalculatedMeasure[] measures)
    {
        var context = new MeasureContext();
        context.Load(measures);
        return context;
    }

    private static bool Sales(string model) => model == "sales";

    private static string Json(VisualDefinition visual) => System.Text.Json.JsonSerializer.Serialize(visual, ReportSerializer.Options);

    [Fact]
    public void Pasted_visuals_get_new_ids_and_keep_the_rest()
    {
        var json = VisualTransfer.Copy([Chart("a"), Chart("b")], Measures());

        var pasted = VisualTransfer.Paste(json, Measures(), Sales).Select(p => p.Visual).ToArray();

        Assert.Equal(["a", "b"], VisualTransfer.Paste(json, Measures(), Sales).Select(p => p.CopiedId));
        Assert.Equal(["a", "b"], pasted.Select(v => v.Title));
        Assert.DoesNotContain(pasted, v => v.Id is "a" or "b");
        // Records compare their lists by reference: compare them as saved
        Assert.Equal(Json(Chart("a") with { Id = pasted[0].Id }), Json(pasted[0]));
    }

    [Fact]
    public void The_calculated_measures_of_the_visuals_come_with_them()
    {
        var json = VisualTransfer.Copy([Chart("a", "calc_rate")], Measures(Rate));
        var target = Measures();

        VisualTransfer.Paste(json, target, Sales);

        Assert.Equal([Rate], target.Measures);
    }

    [Fact]
    public void A_measure_whose_name_is_taken_by_another_definition_is_renamed_and_its_visuals_follow()
    {
        var json = VisualTransfer.Copy([Chart("a", "calc_rate") with { Sort = new("calc_rate.Sum", Descending: true) }], Measures(Rate));
        var other = Rate with { Formula = "[Amount] * 2" };
        var target = Measures(other);

        var pasted = Assert.Single(VisualTransfer.Paste(json, target, Sales)).Visual;

        var added = Assert.Single(target.Measures, m => m != other);
        Assert.Equal(Rate with { Name = added.Name }, added);
        Assert.Equal(added.Name, pasted.Values.Single().Measure);
        Assert.Equal(new SortSpec(added.Name + ".Sum", Descending: true), pasted.Sort);
    }

    [Fact]
    public void Visuals_of_a_model_the_target_does_not_know_are_left_out_but_texts_are_kept()
    {
        var text = new VisualDefinition { Id = "t", Type = VisualType.Text, Text = new TextContent { Text = "Hello" } };
        var json = VisualTransfer.Copy([Chart("a", model: "stock"), text], Measures());

        var pasted = VisualTransfer.Paste(json, Measures(), Sales);

        Assert.Equal([VisualType.Text], pasted.Select(p => p.Visual.Type));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"schema\":\"other/v9\"}")]
    public void Content_that_is_not_copied_visuals_gives_nothing(string json) =>
        Assert.Empty(VisualTransfer.Paste(json, Measures(), Sales));
}
