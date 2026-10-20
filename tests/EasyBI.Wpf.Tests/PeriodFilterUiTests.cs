using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Tests;

/// <summary>Date dimensions are filtered on a period, in the designer's Filters shelf and in the filter bar.</summary>
public class PeriodFilterUiTests
{
    private sealed record Sale(string Region, DateTime Date, decimal Amount);

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(new[] { new Sale("Nord", DateTime.Today, 1m) }, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "CA");

    private static FilterItemViewModel DateFilter(Action? changed = null, FilterSpec? loaded = null)
    {
        var model = Model();
        return new FilterItemViewModel(model.Metadata.GetField("Date"), model, changed ?? (() => { }),
            loaded?.Values, loaded?.Operator ?? FilterOperator.In, loaded?.Grain ?? DateGrain.None);
    }

    // FilterSpec holds a list: records compare it by reference
    private static void AssertSame(FilterSpec expected, FilterSpec? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Field, expected.Operator, expected.Grain), (actual.Field, actual.Operator, actual.Grain));
        Assert.Equal(expected.Values, actual.Values);
    }

    [Fact]
    public void A_date_filter_chooses_a_period_instead_of_values()
    {
        var changes = 0;
        var filter = DateFilter(() => changes++);
        Assert.True(filter.IsPeriod);
        Assert.Null(filter.ToFilterSpec());
        Assert.Equal("(all)", filter.Summary);

        filter.PeriodKind = filter.PeriodKinds.Single(k => k.Value == FilterOperator.Last);
        filter.Count = 6;

        AssertSame(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 6), filter.ToFilterSpec());
        Assert.Equal("Last 6 months", filter.Summary);
        Assert.True(filter.ShowCount && filter.ShowUnit && !filter.ShowDates);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_date_filter_can_keep_the_days_between_two_dates()
    {
        var filter = DateFilter();
        filter.PeriodKind = filter.PeriodKinds.Single(k => k.Value == FilterOperator.Between);
        Assert.Null(filter.ToFilterSpec()); // no date yet: no filter

        filter.From = new DateTime(2024, 1, 1);

        AssertSame(PeriodFilters.Between("Date", new(2024, 1, 1), null), filter.ToFilterSpec());
    }

    [Fact]
    public void A_saved_period_filter_is_shown_as_it_was_chosen()
    {
        var filter = DateFilter(loaded: PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Quarter, 2));

        Assert.Equal((FilterOperator?)FilterOperator.Previous, filter.PeriodKind.Value);
        Assert.Equal((2, DateGrain.Quarter), (filter.Count, filter.Unit.Value));
        Assert.Equal("Previous 2 quarters", filter.Summary);
    }

    [Fact]
    public void The_designer_takes_date_filters_and_keeps_their_period()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(Model()));
        var period = PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year);
        designer.Load(new VisualDefinition
        {
            Model = "sales",
            Mark = MarkType.Kpi,
            Values = [new("Amount")],
            Filters = [period, new FilterSpec("Date", FilterOperator.In, ["2024"], DateGrain.Year)],
        });

        // The period is a chip; the clicked year (a period key) stays hidden, as before
        Assert.Equal("This year", Assert.Single(designer.Filters).Summary);
        Assert.Equal(2, designer.Visual!.Filters.Count);
        AssertSame(period, designer.Visual.Filters[0]);
        AssertSame(new FilterSpec("Date", FilterOperator.In, ["2024"], DateGrain.Year), designer.Visual.Filters[1]);

        designer.RemoveCommand.Execute(designer.Filters.Single());
        Assert.True(designer.CanDrop(Model().Metadata.GetField("Date"), Shelf.Filters));
    }

    [Fact]
    public void The_filter_bar_shows_period_filters() => Sta.Run(() =>
    {
        var filters = new FilterContext();
        filters.SetFilter("sales", PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12));
        var bar = new FilterBar { Context = EasyBIContext.Create().AddModel(Model()), FilterContext = filters };

        var slicer = Assert.Single(bar.Slicers);
        Assert.True(slicer.Filter.IsPeriod);
        Assert.Equal("Last 12 months", slicer.Filter.Summary);
    });
}
