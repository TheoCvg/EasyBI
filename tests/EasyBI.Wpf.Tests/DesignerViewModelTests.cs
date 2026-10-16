using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

public class DesignerViewModelTests
{
    private sealed record Sale(string Region, string Channel, DateTime Date, decimal Amount);

    private static DesignerViewModel Designer() =>
        new(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", "Web", new(2024, 1, 1), 1m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Dimension(s => s.Channel, "Canal")
                .Dimension(s => s.Date, "Date")
                .Measure(s => s.Amount, "CA")));

    private static readonly VisualDefinition Saved = new()
    {
        Title = "CA",
        Model = "sales",
        Axis = [new("Region")],
        Values = [new("Amount")],
        Filters =
        [
            new("Channel", FilterOperator.NotIn, ["Web"]),
            new("Date", FilterOperator.In, ["2024"], DateGrain.Year),
        ],
        Sort = new("Amount", Descending: true),
        Top = 5,
        Extensions = new Dictionary<string, string> { ["OxyPlot"] = "smooth" },
    };

    [Fact]
    public void Editing_a_visual_keeps_settings_the_designer_does_not_show()
    {
        var designer = Designer();
        designer.Load(Saved);
        designer.Title = "CA renommé";

        var visual = designer.Visual!;
        Assert.Equal(Saved.Id, visual.Id);
        Assert.Equal(Saved.Sort, visual.Sort);
        Assert.Equal(5, visual.Top);
        Assert.Equal("smooth", visual.Extensions!["OxyPlot"]);

        Assert.Collection(visual.Filters,
            f => Assert.Equal((FilterOperator.NotIn, "Web"), (f.Operator, f.Values.Single())),
            f => Assert.Equal((DateGrain.Year, "2024"), (f.Grain, f.Values.Single())));
    }

    [Fact]
    public void Sort_is_dropped_when_its_column_is_removed()
    {
        var designer = Designer();
        designer.Load(Saved);

        designer.RemoveCommand.Execute(designer.Values.Single());

        Assert.Null(designer.Visual!.Sort);
        Assert.Equal(5, designer.Visual.Top);
    }

    [Fact]
    public void New_visual_starts_without_previous_settings()
    {
        var designer = Designer();
        designer.Load(Saved);

        designer.Clear();

        Assert.Null(designer.Visual!.Sort);
        Assert.Null(designer.Visual.Top);
        Assert.Null(designer.Visual.Extensions);
        Assert.Empty(designer.Visual.Filters);
    }
}
