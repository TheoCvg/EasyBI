using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Dashboard;

namespace EasyBI.Wpf.Tests;

public class DashboardSerializerTests
{
    private static readonly VisualDefinition Title = new() { Id = "t", Mark = MarkType.Text, Text = new TextContent { Text = "Sales", IsBold = true } };
    private static readonly VisualDefinition Logo = new() { Id = "i", Mark = MarkType.Image, Image = new ImageContent { Data = "AQID", MediaType = "image/jpeg" } };

    [Fact]
    public void Dashboard_round_trips_with_its_layout_next_to_the_report() => Sta.Run(() =>
    {
        var document = new DashboardDocument
        {
            Report = new ReportDefinition { Visuals = [Title, Logo] },
            Layout = new DashboardLayoutDefinition
            {
                Bands =
                [
                    new BandLayout { Items = [new ItemLayout { VisualId = "t", ColumnSpan = 8, IsFullRow = true }] },
                    new BandLayout { Mode = BandMode.Wrap, Items = [new ItemLayout { VisualId = "i", Width = 420 }] },
                ],
            },
        };

        var json = DashboardSerializer.Serialize(document);
        var back = DashboardSerializer.Deserialize(json);

        Assert.Contains("\"schema\": \"easybi-dashboard/v1\"", json);
        Assert.DoesNotContain("\"id\": \"" + document.Layout.Bands[0].Id, json); // operation ids are not saved
        Assert.Equal("Sales", back.Report.Visuals[0].Text!.Text);
        Assert.Equal("AQID", back.Report.Visuals[1].Image!.Data);
        Assert.True(back.Layout!.Bands[0].Items[0].IsFullRow);
        Assert.Equal((BandMode.Wrap, 420d), (back.Layout.Bands[1].Mode, back.Layout.Bands[1].Items[0].Width));
    });

    [Fact]
    public void The_report_inside_a_dashboard_is_a_plain_easybi_report() => Sta.Run(() =>
    {
        var json = DashboardSerializer.Serialize(new DashboardDocument { Report = new ReportDefinition { Visuals = [Title] } });
        var report = System.Text.Json.Nodes.JsonNode.Parse(json)!["report"]!.ToJsonString();

        Assert.Equal("Sales", ReportSerializer.Deserialize(report).Visuals[0].Text!.Text);
        Assert.DoesNotContain("layout", report, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public void A_plain_report_opens_as_a_dashboard_without_layout() => Sta.Run(() =>
    {
        var document = DashboardSerializer.Deserialize(ReportSerializer.Serialize(new ReportDefinition { Visuals = [Title] }));

        Assert.Null(document.Layout);
        Assert.Equal("t", Assert.Single(document.Report.Visuals).Id);
    });

    [Fact]
    public void An_unknown_dashboard_schema_is_refused() => Sta.Run(() =>
    {
        Assert.Throws<NotSupportedException>(() => DashboardSerializer.Deserialize("""{ "schema": "easybi-dashboard/v9", "report": {} }"""));
    });
}
