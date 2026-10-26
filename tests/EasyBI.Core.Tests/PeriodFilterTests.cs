using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class PeriodFilterTests
{
    private static readonly DateOnly Today = new(2024, 5, 15);

    private static (DateTime?, DateTime?) Range(FilterSpec filter) => PeriodFilters.Range(filter, Today);

    private static DateTime Day(int year, int month, int day) => new(year, month, day);

    // --- The days each filter keeps

    [Fact]
    public void Relative_periods_are_counted_from_today()
    {
        Assert.Equal((Day(2024, 5, 1), Day(2024, 6, 1)), Range(PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Month)));
        Assert.Equal((Day(2023, 6, 1), Day(2024, 6, 1)), Range(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12)));
        Assert.Equal((Day(2024, 4, 1), Day(2024, 5, 1)), Range(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Month)));
        Assert.Equal((Day(2023, 10, 1), Day(2024, 4, 1)), Range(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Quarter, 2)));
        Assert.Equal((Day(2024, 1, 1), Day(2025, 1, 1)), Range(PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)));
        Assert.Equal((Day(2024, 5, 9), Day(2024, 5, 16)), Range(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Day, 7)));
    }

    [Fact]
    public void A_range_between_two_days_includes_both_and_may_be_open()
    {
        Assert.Equal((Day(2024, 1, 1), Day(2024, 4, 1)), Range(PeriodFilters.Between("Date", new(2024, 1, 1), new(2024, 3, 31))));
        Assert.Equal((Day(2024, 1, 1), null), Range(PeriodFilters.Between("Date", new(2024, 1, 1), null)));
        Assert.Equal((null, Day(2024, 4, 1)), Range(PeriodFilters.Between("Date", null, new(2024, 3, 31))));
    }

    [Fact]
    public void Period_filters_are_worded_for_the_user()
    {
        Assert.Equal("Last 12 months", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12)));
        Assert.Equal("This quarter", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Quarter)));
        Assert.Equal("This year", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Year, 1)));
        Assert.Equal("Last month", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Month)));
        Assert.Equal("Previous 3 years", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Year, 3)));
        Assert.Equal("Since 01/31/2024", PeriodFilters.Describe(PeriodFilters.Between("Date", new(2024, 1, 31), null)));
    }

    // --- Queries

    private sealed record Sale(DateTime Date, DateTime? Shipped, DateOnly Day, decimal Amount);

    private static Sale[] Sales()
    {
        var today = DateTime.Today;
        return
        [
            new(today, today, DateOnly.FromDateTime(today), 1m),
            new(today.AddMonths(-1), null, DateOnly.FromDateTime(today.AddMonths(-1)), 10m),
            new(today.AddYears(-2), today.AddYears(-2), DateOnly.FromDateTime(today.AddYears(-2)), 100m),
        ];
    }

    private static QueryableSemanticModel<Sale> Model(IQueryable<Sale> source) =>
        SemanticModel.For(source, "m")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Shipped, "Expédition")
            .Dimension(s => s.Day, "Jour")
            .Measure(s => s.Amount, "CA");

    private static async Task<object?> Total(FilterSpec filter, IQueryable<Sale>? source = null) =>
        Assert.Single((await Model(source ?? Sales().AsQueryable()).ExecuteAsync(new([], [new("Amount")], [filter]))).Rows)[0];

    [Fact]
    public async Task Queries_keep_the_rows_of_the_period_on_every_kind_of_date()
    {
        Assert.Equal(11m, await Total(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12)));
        Assert.Equal(11m, await Total(PeriodFilters.Relative("Day", FilterOperator.Last, DateGrain.Month, 12)));
        Assert.Equal(1m, await Total(PeriodFilters.Relative("Shipped", FilterOperator.Last, DateGrain.Month, 12))); // no date: left out
        Assert.Equal(110m, await Total(PeriodFilters.Between("Date", null, DateOnly.FromDateTime(DateTime.Today.AddDays(-1)))));
        Assert.Equal(1m, await Total(PeriodFilters.Relative("Day", FilterOperator.Current, DateGrain.Day)));
    }

    [Fact]
    public async Task Periods_are_compared_to_the_date_itself_by_the_provider()
    {
        var source = new RecordingQueryable<Sale>(Sales());

        await Total(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Year, 3), source);

        var expression = Assert.Single(source.Log.Executed);
        Assert.Contains("(x.Date >= ", expression);
        Assert.Contains("(x.Date < ", expression);
        Assert.DoesNotContain("Year", expression);
    }

    // --- Reports

    [Fact]
    public void Reports_save_period_filters_and_check_they_apply_to_dates()
    {
        var filter = PeriodFilters.Relative("Amount", FilterOperator.Last, DateGrain.Month, 12);
        var report = new ReportDefinition
        {
            Visuals = [new VisualDefinition { Title = "V", Model = "m", Type = VisualType.Kpi, Values = [new("Amount")], Filters = [filter with { Field = "Date" }] }],
            Filters = [new ReportFilter("m", PeriodFilters.Between("Amount", new(2024, 1, 1), null))],
        };

        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));
        Assert.Equal(FilterOperator.Last, restored.Visuals[0].Filters[0].Operator);
        Assert.Equal(["12"], restored.Visuals[0].Filters[0].Values);
        Assert.Contains("\"operator\": \"Between\"", ReportSerializer.Serialize(report));

        var problems = ReportValidator.Validate(report, [Model(Sales().AsQueryable()).Metadata]);
        Assert.Equal(["Report filter: 'Amount' is not a dimension."], problems);
    }
}

[Collection(CultureCollection.Name)]
public class PeriodFilterCultureTests
{
    [Fact]
    public void Period_filters_are_worded_in_french_with_agreement()
    {
        using (TestCulture.Use("fr-FR"))
        {
            Assert.Equal("12 derniers mois", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12)));
            Assert.Equal("3 dernières années", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Year, 3)));
            Assert.Equal("Année précédente", PeriodFilters.Describe(PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Year)));
            Assert.Equal("Depuis le 31/01/2024", PeriodFilters.Describe(PeriodFilters.Between("Date", new(2024, 1, 31), null)));
        }
    }
}
