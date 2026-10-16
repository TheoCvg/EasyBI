using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

public class QueryEngineTests
{
    private sealed record Product(string Category);

    private sealed record Sale(string Region, Product Product, DateTime Date, decimal Amount, int Quantity);

    private static readonly Sale[] Sales =
    [
        new("Nord", new("Vélos"), new(2024, 1, 15), 100m, 1),
        new("Nord", new("Vélos"), new(2024, 2, 10), 200m, 2),
        new("Nord", new("Casques"), new(2024, 4, 5), 50m, 5),
        new("Sud", new("Vélos"), new(2024, 1, 20), 300m, 3),
        new("Sud", new("Casques"), new(2025, 7, 1), 40m, 4),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales", "Ventes")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Product.Category, "Catégorie")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "CA", format: "C0")
            .Measure(s => s.Quantity, "Quantité")
            .Count("Nb ventes");

    [Fact]
    public void Metadata_infers_names_and_types()
    {
        var meta = Model().Metadata;
        Assert.Equal(["Region", "Product.Category", "Date", "Amount", "Quantity", "Count"], meta.Fields.Select(f => f.Name));
        Assert.Equal(DataType.Date, meta.GetField("Date").DataType);
        Assert.Equal(FieldKind.Measure, meta.GetField("Amount").Kind);
    }

    [Fact]
    public async Task Groups_by_dimension_and_sums()
    {
        var result = await Model().ExecuteAsync(new([new("Region")], [new("Amount"), new("Count")]));

        Assert.Equal(["Region", "Amount", "Count"], result.Columns.Select(c => c.Key));
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["Nord", 350m, 3], result.Rows[0]);
        Assert.Equal(["Sud", 340m, 2], result.Rows[1]);
    }

    [Fact]
    public async Task No_group_by_returns_grand_total()
    {
        var result = await Model().ExecuteAsync(new([], [new("Amount")]));
        Assert.Equal(690m, Assert.Single(result.Rows)[0]);
    }

    [Fact]
    public async Task Applies_date_grain_and_aggregation_override()
    {
        var result = await Model().ExecuteAsync(new(
            [new("Date", DateGrain.Quarter)],
            [new("Quantity", Aggregation.Max)]));

        Assert.Equal("Date.Quarter", result.Columns[0].Key);
        Assert.Equal("Date (Quarter)", result.Columns[0].Caption);
        Assert.Equal(["2024 Q1", "2024 Q2", "2025 Q3"], result.Rows.Select(r => r[0]!.ToString()));
        Assert.Equal(new DatePeriod(DateGrain.Quarter, 20241), result.Rows[0][0]);
        Assert.Equal([3, 5, 4], result.Rows.Select(r => r[1]));
    }

    [Fact]
    public async Task Filters_sorts_and_limits()
    {
        var query = new AnalyticQuery(
            [new("Product.Category")],
            [new("Amount")],
            Filters: [new("Region", FilterOperator.In, ["Nord"])],
            Sort: [new("Amount", Descending: true)],
            Top: 1);

        var row = Assert.Single((await Model().ExecuteAsync(query)).Rows);
        Assert.Equal(["Vélos", 300m], row);
    }

    [Fact]
    public async Task Filters_on_dates_and_not_in()
    {
        var query = new AnalyticQuery(
            [new("Region")],
            [new("Count")],
            Filters: [new("Date", FilterOperator.NotIn, [InvariantValue.Format(new DateTime(2025, 7, 1))])]);

        var result = await Model().ExecuteAsync(query);
        Assert.Equal([3, 1], result.Rows.Select(r => r[1]));
    }

    [Fact]
    public async Task Distinct_values_are_sorted()
    {
        var values = await Model().GetDistinctValuesAsync("Product.Category");
        Assert.Equal(["Casques", "Vélos"], values);
    }

    [Fact]
    public async Task Chart_data_pivots_legend_into_series()
    {
        var visual = new VisualDefinition
        {
            Model = "sales",
            Axis = [new("Date", DateGrain.Year)],
            Legend = new("Region"),
            Values = [new("Amount")],
        };

        var request = await VisualPipeline.PrepareAsync(Model(), visual);

        Assert.Equal(["2024", "2025"], request.Chart.Categories);
        Assert.Equal(["Nord", "Sud"], request.Chart.Series.Select(s => s.Name));
        Assert.Equal([350d, null], request.Chart.Series[0].Values);
        Assert.Equal([300d, 40d], request.Chart.Series[1].Values);
    }

    [Fact]
    public void Report_round_trips_through_json()
    {
        var report = new ReportDefinition
        {
            Title = "Test",
            Visuals =
            [
                new VisualDefinition
                {
                    Model = "sales",
                    Mark = MarkType.Line,
                    Axis = [new("Date", DateGrain.Month)],
                    Values = [new("Amount", Aggregation.Average)],
                    Filters = [new("Region", FilterOperator.In, ["Nord"])],
                },
            ],
        };

        var json = ReportSerializer.Serialize(report);
        var back = ReportSerializer.Deserialize(json);

        Assert.Contains("\"mark\": \"Line\"", json);
        var visual = Assert.Single(back.Visuals);
        Assert.Equal(report.Visuals[0].Axis, visual.Axis);
        Assert.Equal(report.Visuals[0].Values, visual.Values);
        Assert.Equal(["Nord"], visual.Filters[0].Values);
    }
}
