using System.ComponentModel;
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
    public void Sorting_on_a_chip_sorts_the_visual_and_unsorts_the_other_chips()
    {
        var designer = Designer();
        designer.Load(Saved with { Sort = null, Top = null });
        var region = designer.Axis.Single();
        var amount = designer.Values.Single();

        region.SortDirection = ListSortDirection.Ascending;
        Assert.Equal(new SortSpec("Region"), designer.Visual!.Sort);

        amount.SortCommand.Execute(ListSortDirection.Descending);

        Assert.Null(region.SortDirection);
        Assert.Equal(new SortSpec("Amount", Descending: true), designer.Visual!.Sort);
        Assert.Equal("CA, descending", designer.SortSummary);

        amount.SortCommand.Execute(null); // "No sort" in the menu of the chip
        Assert.Null(designer.Visual!.Sort);
        Assert.Null(designer.SortSummary);
    }

    [Fact]
    public void A_loaded_sort_shows_on_its_chip_and_follows_its_aggregation()
    {
        var designer = Designer();
        designer.Load(Saved);
        var amount = designer.Values.Single();
        Assert.Equal(ListSortDirection.Descending, amount.SortDirection);
        Assert.Null(designer.Axis.Single().SortDirection);

        amount.Aggregation = Aggregation.Average;

        Assert.Equal(new SortSpec("Amount.Average", Descending: true), designer.Visual!.Sort);
    }

    [Fact]
    public void A_sorted_dimension_moved_to_the_legend_stays_sorted()
    {
        var designer = Designer();
        designer.Load(Saved with { Sort = new("Region") });

        designer.Drop(designer.Axis.Single(), Shelf.Legend);

        Assert.Equal(ListSortDirection.Ascending, designer.Legend.Single().SortDirection);
        Assert.Equal(new SortSpec("Region"), designer.Visual!.Sort);
    }

    [Fact]
    public void Top_shows_the_first_rows_and_ignores_values_below_one()
    {
        var designer = Designer();
        designer.Load(Saved with { Top = null });

        designer.Top = 3;
        Assert.Equal(3, designer.Visual!.Top);

        designer.Top = 0;
        Assert.Null(designer.Top);
        Assert.Null(designer.Visual!.Top);
    }

    [Fact]
    public void Calculated_measures_offer_no_aggregation_and_save_none()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(
            SemanticModel.For(new[] { new Sale("Nord", "Web", new(2024, 1, 1), 1m) }, "sales")
                .Dimension(s => s.Region, "Région")
                .Measure(s => s.Amount, "CA")
                .Ratio(s => s.Amount, s => s.Amount, "Part", "Share")));

        designer.Load(new VisualDefinition { Model = "sales", Axis = [new("Region")], Values = [new("Share")] });

        Assert.False(designer.Values.Single().HasAggregation);
        Assert.Null(designer.Visual!.Values.Single().Aggregation);
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
