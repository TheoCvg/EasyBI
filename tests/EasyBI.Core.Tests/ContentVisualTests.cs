using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class ContentVisualTests
{
    private static readonly ModelMetadata Sales = new("sales", "Sales", [new FieldDescriptor("Amount", "Amount", FieldKind.Measure, DataType.Number)]);

    [Fact]
    public void Text_and_image_visuals_need_no_model_nor_measure()
    {
        Assert.Null(VisualValidator.Validate(new VisualDefinition { Mark = MarkType.Text, Text = new TextContent { Text = "Hi" } }));
        Assert.Null(VisualValidator.Validate(new VisualDefinition { Mark = MarkType.Image, Image = new ImageContent { Data = "AQID" } }));
    }

    [Fact]
    public void An_image_visual_without_an_image_is_reported()
    {
        Assert.NotNull(VisualValidator.Validate(new VisualDefinition { Mark = MarkType.Image }));
    }

    [Fact]
    public void Report_validation_ignores_the_model_of_text_and_image_visuals()
    {
        var report = new ReportDefinition
        {
            Visuals =
            [
                new VisualDefinition { Mark = MarkType.Text, Text = new TextContent() },
                new VisualDefinition { Mark = MarkType.Image, Image = new ImageContent { Data = "AQID" } },
            ],
        };

        Assert.Empty(ReportValidator.Validate(report, [Sales]));
    }

    [Fact]
    public void Content_visuals_render_without_querying()
    {
        var visual = new VisualDefinition { Mark = MarkType.Text, Text = new TextContent { Text = "Hi" } };

        var request = VisualPipeline.PrepareContent(visual);

        Assert.True(visual.Mark.IsContent());
        Assert.False(MarkType.Bar.IsContent());
        Assert.Empty(request.Result.Rows);
        Assert.Same(visual, request.Visual);
    }

    [Fact]
    public void Text_and_image_visuals_round_trip_through_json()
    {
        var report = new ReportDefinition
        {
            Visuals =
            [
                new VisualDefinition { Mark = MarkType.Text, Text = new TextContent { Text = "Hi", FontSize = 24, IsBold = true, Alignment = TextAlign.Center } },
                new VisualDefinition { Mark = MarkType.Image, Image = new ImageContent { Data = "AQID", MediaType = "image/jpeg", Fit = ImageFit.Cover } },
            ],
        };

        var json = ReportSerializer.Serialize(report);
        var back = ReportSerializer.Deserialize(json);

        Assert.Contains("\"mark\": \"Text\"", json);
        Assert.Equal(report.Visuals[0].Text, back.Visuals[0].Text);
        Assert.Equal(report.Visuals[1].Image, back.Visuals[1].Image);
    }

    [Fact]
    public void Reports_saved_before_content_visuals_still_load()
    {
        const string json = """
            { "schema": "easybi/v1", "title": "Old", "visuals": [ { "id": "v", "model": "sales", "mark": "Kpi", "values": [ { "measure": "Amount" } ] } ] }
            """;

        var visual = Assert.Single(ReportSerializer.Deserialize(json).Visuals);

        Assert.Equal(("sales", MarkType.Kpi), (visual.Model, visual.Mark));
        Assert.Null(visual.Text);
        Assert.Null(visual.Image);
    }
}
