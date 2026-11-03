using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Periods, presentations and accumulations taken from a catalog an application extends.</summary>
public class TimeCatalogTests
{
    private sealed record Sale(DateTime Date, string Region, decimal Amount, decimal Cost);

    private static readonly Sale[] Sales =
    [
        new(new(2023, 1, 10), "Nord", 100m, 10m),
        new(new(2023, 2, 10), "Nord", 50m, 30m),
        new(new(2023, 1, 20), "Sud", 40m, 4m),
        new(new(2024, 1, 10), "Nord", 150m, 20m),
        new(new(2024, 2, 10), "Nord", 25m, 25m),
        new(new(2024, 3, 10), "Sud", 60m, 6m),
    ];

    private static QueryableSemanticModel<Sale> Model(IQueryable<Sale>? source = null) =>
        SemanticModel.For(source ?? Sales.AsQueryable(), "m")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Région")
            .Measure(s => s.Amount, "CA", format: "N0")
            .Measure(s => s.Cost, "Coûts", format: "N0", favorable: Favorable.Lower);

    private static MeasureRef Amount(MeasureTime? time = null) => new("Amount") { Time = time };

    private static readonly MeasureTime PreviousPeriod = new() { Period = TimeIds.PreviousPeriod };

    private static double? Num(object? value) => value is null ? null : Convert.ToDouble(value);

    private static Dictionary<int, double?> ByMonth(ResultSet result, int column = 2) =>
        result.Rows.ToDictionary(r => ((DatePeriod)r[0]!).Key, r => Num(r[column]));

    [Fact]
    public async Task The_previous_period_of_each_month_of_the_axis_is_the_month_before()
    {
        var result = await Model().ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)], [Amount(), Amount(PreviousPeriod)]));

        var previous = ByMonth(result);
        Assert.Equal(140d, previous[202302]); // January 2023
        Assert.Equal(150d, previous[202402]);
        Assert.Equal(25d, previous[202403]);
        Assert.Equal("CA M-1", result.Columns[2].Caption);
    }

    [Theory]
    [InlineData("2024-02-01", "2024-02-29", 150d)] // a whole month: the month before
    [InlineData("2024-01-15", "2024-02-14", 150d)] // 31 days: the 31 days before (2023-12-15 to 2024-01-14)
    [InlineData("2024-01-01", "2024-12-31", 190d)] // a whole year: the year before
    public async Task Without_a_date_on_the_axis_the_previous_period_is_as_long_as_the_filtered_one(string from, string to, double expected)
    {
        var filter = PeriodFilters.Between("Date", DateOnly.Parse(from), DateOnly.Parse(to));

        var result = await Model().ExecuteWithTimeAsync(new([], [Amount(PreviousPeriod)], [filter]), new DateOnly(2026, 1, 1), CancellationToken.None);

        Assert.Equal(expected, Num(Assert.Single(result.Rows)[0]));
    }

    [Fact]
    public async Task A_measure_is_compared_with_any_number_of_units_back()
    {
        var twoMonths = new MeasureTime { Period = TimeIds.Offset, Count = 2, Unit = TimeUnit.Month };

        var result = await Model().ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)], [Amount(), Amount(twoMonths)]));

        Assert.Equal(150d, ByMonth(result)[202403]);
        Assert.Equal("CA M-2", result.Columns[2].Caption);
        Assert.Equal("Amount.offset(2Month).value", result.Columns[2].Key);
    }

    [Fact]
    public async Task Each_period_compared_with_costs_one_more_query()
    {
        var log = new RecordingQueryable<Sale>(Sales);

        await Model(log).ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)],
            [Amount(), Amount(MeasureTime.PreviousYear), Amount(MeasureTime.PercentChangeFromPreviousYear), Amount(PreviousPeriod)]));

        Assert.Equal(3, log.Log.Executed.Count);
    }

    [Fact]
    public async Task Totals_since_the_start_of_the_year_are_compared_with_those_of_the_year_before()
    {
        var ytdVsLastYear = new MeasureTime { Accumulation = TimeIds.YearToDate, Period = TimeIds.LastYear };

        var result = await Model().ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)], [Amount(MeasureTime.YearToDate), Amount(ytdVsLastYear)]));

        var months = result.Rows.ToDictionary(r => ((DatePeriod)r[0]!).Key, r => (Num(r[1]), Num(r[2])));
        Assert.Equal((175d, 190d), months[202402]);
        Assert.Equal((150d, 140d), months[202401]);
        Assert.Equal("CA YTD Y-1", result.Columns[2].Caption);
    }

    [Fact]
    public async Task A_running_total_never_starts_again_and_quarters_start_again_each_quarter()
    {
        var result = await Model().ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)],
            [Amount(new MeasureTime { Accumulation = TimeIds.Running }), Amount(new MeasureTime { Accumulation = TimeIds.QuarterToDate })]));

        Assert.Equal([140d, 190d, 340d, 365d, 425d], result.Rows.Select(r => Num(r[1])));
        Assert.Equal([140d, 190d, 150d, 175d, 235d], result.Rows.Select(r => Num(r[2])));
    }

    [Fact]
    public async Task An_application_adds_its_own_periods_presentations_and_accumulations()
    {
        var catalog = new TimeCatalog()
            .Add(new ComparisonPeriod("retail.week364", () => "Same weekday last year", _ => new TimeShift(0, -364)) { ShortCaption = _ => "W-52" })
            .Add(new ComparisonOutput("acme.index", () => "Index", (value, compared) => compared is { } c && c != 0 ? value / c * 100 : null)
            {
                ColumnCaption = () => "{0} index vs {1}",
                Format = "N0",
            })
            .Add(new Accumulation("acme.fiscal", () => "Fiscal year to date", d => new DateTime(d.Month >= 2 ? d.Year : d.Year - 1, 2, 1)));
        var model = Model();
        model.Time = catalog;
        var index = new MeasureTime { Period = "retail.week364", Show = "acme.index" };

        var result = await model.ExecuteWithTimeAsync(new([new("Date", DateGrain.Year)], [Amount(), Amount(index)]));
        var fiscal = await model.ExecuteWithTimeAsync(new([new("Date", DateGrain.Month)], [Amount(new MeasureTime { Accumulation = "acme.fiscal" })]));

        Assert.Equal(235d / 190d * 100, Num(result.Rows[1][2])!.Value, 6);
        Assert.Equal(("CA index vs W-52", "N0"), (result.Columns[2].Caption, result.Columns[2].Format));
        // February starts the fiscal year: January 2024 adds up to February 2023's
        Assert.Equal([140d, 50d, 200d, 25d, 85d], fiscal.Rows.Select(r => Num(r[1])));
    }

    [Fact]
    public async Task A_calculation_the_catalog_no_longer_has_is_empty()
    {
        var catalog = new TimeCatalog();
        catalog.RemovePeriod(TimeIds.LastYear);
        catalog.RemoveAccumulation(TimeIds.YearToDate);
        var model = Model();
        model.Time = catalog;

        var result = await model.ExecuteWithTimeAsync(new([new("Date", DateGrain.Year)], [Amount(), Amount(MeasureTime.PreviousYear), Amount(MeasureTime.YearToDate)]));

        Assert.All(result.Rows, r => Assert.Equal([null, null], r.Skip(2)));
        Assert.DoesNotContain(catalog.Periods, p => p.Id == TimeIds.LastYear);
    }

    [Fact]
    public void Entries_of_an_application_have_a_prefix_and_a_unique_id()
    {
        var catalog = new TimeCatalog();
        var mine = new ComparisonOutput("acme.ratio", () => "Ratio", (v, c) => v / c);

        Assert.Throws<ArgumentException>(() => catalog.Add(mine with { Id = "ratio" }));
        catalog.Add(mine);
        Assert.Throws<InvalidOperationException>(() => catalog.Add(mine));
        Assert.Throws<InvalidOperationException>(() => catalog.Replace(mine with { Id = "acme.other" }));

        catalog.Replace(catalog.Output(TimeIds.Change)! with { Caption = () => "Difference" });
        Assert.Equal("Difference", catalog.Output(TimeIds.Change)!.Caption());
        Assert.Equal(TimeIds.Value, catalog.Outputs[0].Id);
        Assert.Equal("acme.ratio", catalog.Outputs[^1].Id);
    }

    [Fact]
    public async Task Changes_tell_which_way_is_good_from_the_model_or_the_visual()
    {
        var cost = new MeasureRef("Cost") { Time = MeasureTime.ChangeFromPreviousYear };

        var result = await Model().ExecuteWithTimeAsync(new([new("Date", DateGrain.Year)],
            [cost, Amount(MeasureTime.ChangeFromPreviousYear), Amount(MeasureTime.PreviousYear), cost with { Favorable = Favorable.Higher }]));

        Assert.Equal([Favorable.Lower, Favorable.Higher, null, Favorable.Higher], result.Columns.Skip(1).Select(c => c.Change));
    }

    [Fact]
    public void A_chip_sums_a_calculation_up_in_a_few_characters()
    {
        var catalog = new TimeCatalog();

        Assert.Equal("Y-1", catalog.Badge(MeasureTime.PreviousYear));
        Assert.Equal("Δ% Y-1", catalog.Badge(MeasureTime.PercentChangeFromPreviousYear));
        Assert.Equal("Cum.", catalog.Badge(MeasureTime.YearToDate));
        Assert.Equal("Cum. Δ M-3", catalog.Badge(new MeasureTime { Accumulation = TimeIds.YearToDate, Period = TimeIds.Offset, Count = 3, Unit = TimeUnit.Month, Show = TimeIds.Change }));
    }

    [Fact]
    public void Reports_written_before_the_catalogs_open_with_the_same_calculations()
    {
        const string json = """
            {"schema":"easybi/v1","visuals":[{"model":"m","values":[
              {"measure":"Amount","time":"PreviousYear"},{"measure":"Amount","time":"ChangeFromPreviousYear"},
              {"measure":"Amount","time":"PercentChangeFromPreviousYear"},{"measure":"Amount","time":"YearToDate"}]}]}
            """;

        var values = ReportSerializer.Deserialize(json).Visuals[0].Values;

        Assert.Equal([MeasureTime.PreviousYear, MeasureTime.ChangeFromPreviousYear, MeasureTime.PercentChangeFromPreviousYear, MeasureTime.YearToDate],
            values.Select(v => v.Time));
    }

    [Fact]
    public void A_sort_saved_on_a_time_column_of_an_older_report_refers_to_its_column_of_today()
    {
        const string json = """
            {"schema":"easybi/v1","visuals":[{"model":"m","values":[{"measure":"Amount","time":"PercentChangeFromPreviousYear"}],
              "sort":{"key":"Amount.PercentChangeFromPreviousYear","descending":true}}]}
            """;

        var visual = ReportSerializer.Deserialize(json).Visuals[0];

        Assert.Equal(visual.Values[0].Key, visual.Sort!.Key);
    }

    [Fact]
    public void A_report_writes_only_the_parts_a_calculation_uses()
    {
        var report = new ReportDefinition
        {
            Visuals = [new VisualDefinition
            {
                Model = "m",
                Values =
                [
                    Amount(MeasureTime.YearToDate),
                    Amount(MeasureTime.PercentChangeFromPreviousYear),
                    Amount(new MeasureTime { Period = TimeIds.Offset, Count = 2, Unit = TimeUnit.Month }) with { Favorable = Favorable.Lower },
                ],
            }],
        };

        var json = ReportSerializer.Serialize(report);

        Assert.Contains("\"time\":{\"accumulation\":\"yearToDate\"}", json.Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        Assert.Contains("\"time\":{\"period\":\"lastYear\",\"show\":\"percentChange\"}", json.Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        Assert.Contains("\"time\":{\"period\":\"offset\",\"count\":2,\"unit\":\"Month\"},\"favorable\":\"Lower\"", json.Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        Assert.Equal(report.Visuals[0].Values, ReportSerializer.Deserialize(json).Visuals[0].Values);
    }
}
