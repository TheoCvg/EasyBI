using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class TimeCalculationTests
{
    private sealed record Sale(DateTime Date, string Region, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new(new(2023, 1, 10), "Nord", 100m),
        new(new(2023, 2, 10), "Nord", 50m),
        new(new(2023, 1, 20), "Sud", 40m),
        new(new(2024, 1, 10), "Nord", 150m),
        new(new(2024, 2, 10), "Nord", 25m),
        new(new(2024, 3, 10), "Sud", 60m),
    ];

    private static QueryableSemanticModel<Sale> Model(IQueryable<Sale>? source = null) =>
        SemanticModel.For(source ?? Sales.AsQueryable(), "m")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Région")
            .Measure(s => s.Amount, "CA", format: "N0");

    private static MeasureRef Amount(TimeCalculation time = TimeCalculation.None) => new("Amount") { Time = time };

    private static Task<ResultSet> Run(AnalyticQuery query, IQueryable<Sale>? source = null) => Model(source).ExecuteWithTimeAsync(query);

    private static double? Num(object? value) => value is null ? null : Convert.ToDouble(value);

    [Fact]
    public async Task Each_year_is_compared_with_the_previous_one()
    {
        var result = await Run(new([new("Date", DateGrain.Year)],
            [Amount(), Amount(TimeCalculation.PreviousYear), Amount(TimeCalculation.ChangeFromPreviousYear), Amount(TimeCalculation.PercentChangeFromPreviousYear)]));

        Assert.Equal(new DatePeriod(DateGrain.Year, 2023), result.Rows[0][0]);
        Assert.All(result.Rows[0].Skip(2), Assert.Null); // 2022 is not in the data
        Assert.Equal((235d, 190d, 45d), (Num(result.Rows[1][1])!.Value, Num(result.Rows[1][2])!.Value, Num(result.Rows[1][3])!.Value));
        Assert.Equal(45d / 190d, Num(result.Rows[1][4])!.Value, 10);
    }

    [Fact]
    public async Task Months_are_compared_with_the_same_month_a_year_earlier_for_each_series()
    {
        var result = await Run(new([new("Date", DateGrain.Month), new("Region")], [Amount(), Amount(TimeCalculation.PreviousYear)]));

        var byKey = result.Rows.ToDictionary(r => $"{((DatePeriod)r[0]!).Key} {r[1]}", r => Num(r[3]));
        Assert.Equal(100d, byKey["202401 Nord"]);
        Assert.Equal(50d, byKey["202402 Nord"]);
        Assert.Null(byKey["202403 Sud"]); // no sale in the South in March 2023
    }

    [Fact]
    public async Task A_kpi_filtered_on_a_period_is_compared_with_the_same_period_a_year_earlier()
    {
        var thisYear = await Run(new([], [Amount(), Amount(TimeCalculation.PercentChangeFromPreviousYear)],
            [PeriodFilters.Between("Date", new(2024, 1, 1), new(2024, 12, 31))]));
        var clickedYear = await Run(new([], [Amount(TimeCalculation.PreviousYear)], [new FilterSpec("Date", FilterOperator.In, ["2024"], DateGrain.Year)]));

        Assert.Equal(45d / 190d, Num(Assert.Single(thisYear.Rows)[1])!.Value, 10);
        Assert.Equal(190d, Num(Assert.Single(clickedYear.Rows)[0]));
    }

    // --- A period under way: compared with the same days a year earlier

    private static readonly DateOnly Today = new(2026, 2, 15);

    // Last year's sales before and after today's date a year earlier, and this year's up to today
    private static readonly Sale[] UnderWay =
    [
        new(new(2025, 1, 10), "Nord", 100m),
        new(new(2025, 2, 10), "Nord", 30m),
        new(new(2025, 2, 20), "Nord", 70m),
        new(new(2025, 6, 1), "Nord", 500m),
        new(new(2026, 1, 12), "Nord", 120m),
        new(new(2026, 2, 3), "Nord", 40m),
    ];

    private static Task<ResultSet> RunOn(DateOnly today, AnalyticQuery query) =>
        Model(UnderWay.AsQueryable()).ExecuteWithTimeAsync(query, today, CancellationToken.None);

    [Fact]
    public async Task A_range_ending_later_is_compared_up_to_the_same_day_and_a_past_range_whole()
    {
        var underWay = await RunOn(Today, new([], [Amount(), Amount(TimeCalculation.PreviousYear)], [PeriodFilters.Between("Date", new(2026, 1, 1), new(2026, 12, 31))]));
        var since = await RunOn(Today, new([], [Amount(TimeCalculation.PreviousYear)], [PeriodFilters.Between("Date", new(2026, 1, 1), null)]));
        var past = await RunOn(Today, new([], [Amount(TimeCalculation.PreviousYear)], [PeriodFilters.Between("Date", new(2025, 1, 1), new(2025, 12, 31))]));

        Assert.Equal((160d, 130d), (Num(underWay.Rows[0][0])!.Value, Num(underWay.Rows[0][1])!.Value));
        Assert.Equal(130d, Num(since.Rows[0][0])); // not this year's sales, a year earlier than an open end
        Assert.Null(Num(past.Rows[0][0])); // 2024: no sales
    }

    [Fact]
    public async Task The_month_under_way_is_compared_with_the_same_days_a_year_earlier_and_past_months_whole()
    {
        var result = await RunOn(Today, new([new("Date", DateGrain.Month)], [Amount(), Amount(TimeCalculation.PreviousYear)],
            [PeriodFilters.Between("Date", new(2026, 1, 1), new(2026, 12, 31))]));

        var byMonth = result.Rows.ToDictionary(r => ((DatePeriod)r[0]!).Key, r => Num(r[2]));
        Assert.Equal(100d, byMonth[202601]);
        Assert.Equal(30d, byMonth[202602]);
    }

    [Theory]
    [InlineData(DateGrain.Year, "2026", 130d)]
    [InlineData(DateGrain.Month, "202602", 30d)]
    [InlineData(DateGrain.Year, "2025", null)] // a past year: compared whole (2024)
    public async Task A_clicked_period_under_way_is_compared_up_to_the_same_day(DateGrain grain, string period, double? expected)
    {
        var result = await RunOn(Today, new([], [Amount(TimeCalculation.PreviousYear)], [new FilterSpec("Date", FilterOperator.In, [period], grain)]));

        Assert.Equal(expected, Num(result.Rows[0][0]));
    }

    [Fact]
    public async Task Dates_inside_alternative_filters_are_shifted_a_year_earlier()
    {
        // Selected elements: (2024, North) or (2024, South)
        var filter = FilterSpec.AnyOf([
            [new FilterSpec("Date", FilterOperator.In, ["2024"], DateGrain.Year), new FilterSpec("Region", FilterOperator.In, ["Nord"])],
            [new FilterSpec("Date", FilterOperator.In, ["2024"], DateGrain.Year), new FilterSpec("Region", FilterOperator.In, ["Sud"])],
        ]);

        var result = await Run(new([], [Amount(), Amount(TimeCalculation.PreviousYear)], [filter]));

        Assert.Equal((235d, 190d), (Num(result.Rows[0][0])!.Value, Num(result.Rows[0][1])!.Value));
    }

    [Fact]
    public async Task Without_any_date_the_previous_year_is_left_empty()
    {
        var result = await Run(new([new("Region")], [Amount(), Amount(TimeCalculation.PreviousYear)]));

        Assert.All(result.Rows, r => Assert.Null(r[2]));
    }

    [Fact]
    public async Task The_year_to_date_total_adds_up_along_the_date_axis_and_starts_again_each_year()
    {
        var result = await Run(new([new("Date", DateGrain.Month)], [Amount(TimeCalculation.YearToDate)]));

        Assert.Equal([140d, 190d, 150d, 175d, 235d], result.Rows.Select(r => Num(r[1])));
    }

    [Fact]
    public async Task Sorting_and_top_apply_to_time_columns()
    {
        var result = await Run(new([new("Region")], [Amount(TimeCalculation.YearToDate)], Sort: [new(Amount(TimeCalculation.YearToDate).Key, true)], Top: 1));

        Assert.Equal("Nord", Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task Time_columns_are_named_and_formatted_for_the_user()
    {
        var result = await Run(new([new("Date", DateGrain.Year)], [Amount(TimeCalculation.PreviousYear), Amount(TimeCalculation.PercentChangeFromPreviousYear)]));

        Assert.Equal(["CA, previous year", "CA, % change vs previous year"], result.Columns.Skip(1).Select(c => c.Caption));
        Assert.Equal(["N0", "P1"], result.Columns.Skip(1).Select(c => c.Format));
        Assert.Equal(["Amount.PreviousYear", "Amount.PercentChangeFromPreviousYear"], result.Columns.Skip(1).Select(c => c.Key));
    }

    [Fact]
    public async Task A_comparison_costs_one_more_query_and_plain_measures_none()
    {
        var plain = new RecordingQueryable<Sale>(Sales);
        await Run(new([new("Date", DateGrain.Year)], [Amount()]), plain);
        var compared = new RecordingQueryable<Sale>(Sales);
        await Run(new([new("Date", DateGrain.Year)], [Amount(), Amount(TimeCalculation.PreviousYear), Amount(TimeCalculation.ChangeFromPreviousYear)]), compared);

        Assert.Single(plain.Log.Executed);
        Assert.Equal(2, compared.Log.Executed.Count);
    }

    [Fact]
    public async Task Visuals_draw_time_columns_like_any_measure()
    {
        var visual = new VisualDefinition { Model = "m", Type = VisualType.Line, Axis = [new("Date", DateGrain.Year)], Values = [Amount(), Amount(TimeCalculation.PreviousYear)] };

        var chart = (await VisualPipeline.PrepareAsync(Model(), visual)).Chart;

        Assert.Equal(["CA", "CA, previous year"], chart.Series.Select(s => s.Name));
        Assert.Equal([null, 190d], chart.Series[1].Values);
    }

    [Fact]
    public void Reports_only_mention_a_time_calculation_when_there_is_one()
    {
        var report = new ReportDefinition { Visuals = [new VisualDefinition { Model = "m", Values = [Amount(), Amount(TimeCalculation.YearToDate)] }] };

        var json = ReportSerializer.Serialize(report);

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"time\""));
        Assert.Equal(TimeCalculation.YearToDate, ReportSerializer.Deserialize(json).Visuals[0].Values[1].Time);
    }
}
