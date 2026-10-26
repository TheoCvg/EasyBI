using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Visuals ask their <see cref="ISemanticModel"/> for their data, whatever the source behind it.</summary>
public class VisualDataSourceTests
{
    private sealed record Sale(DateTime Date, string Region, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new(new(2023, 1, 10), "Nord", 100m),
        new(new(2023, 2, 10), "Sud", 90m),
        new(new(2024, 1, 10), "Nord", 150m),
        new(new(2024, 3, 10), "Sud", 85m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Region, "Région")
            .Measure(s => s.Amount, "CA", format: "N0");

    private static readonly VisualDefinition ByRegion =
        new() { Model = "sales", Type = VisualType.Bar, Axis = [new("Region")], Values = [new("Amount")] };

    private static readonly ResultColumn[] Columns =
    [
        new("Region", "Région", FieldKind.Dimension, DataType.Text),
        new("Amount", "CA", FieldKind.Measure, DataType.Number),
    ];

    /// <summary>A source that answers visuals itself, as a server would: it records what it is asked.</summary>
    private sealed class AnsweringModel(VisualData data) : ISemanticModel
    {
        public List<VisualDataRequest> Requests { get; } = [];

        public ModelMetadata Metadata { get; } = Model().Metadata;

        public Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(data);
        }

        public Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<object?>>([]);
    }

    private static CalculatedMeasure Measure(string name, string formula) =>
        new() { Name = name, Caption = name, Model = "sales", Formula = formula };

    [Fact]
    public async Task A_source_that_answers_visuals_is_drawn_without_running_queries()
    {
        var matrix = new MatrixData(["Région"], null, [], [], []);
        var model = new AnsweringModel(new VisualData(new ResultSet(Columns, [["Nord", (object?)250m], ["Sud", 175m]])) { Matrix = matrix });

        var request = await VisualPipeline.PrepareAsync(model, ByRegion);

        Assert.Equal(["Nord", "Sud"], request.Chart.Categories);
        Assert.Equal([250d, 175d], Assert.Single(request.Chart.Series).Values);
        Assert.Same(matrix, request.Matrix);
    }

    [Fact]
    public async Task A_visual_asks_for_its_data_with_the_measures_it_uses_and_whether_it_has_tooltips()
    {
        var model = new AnsweringModel(new VisualData(new ResultSet([Columns[0], Columns[1] with { Key = "Quadrupled" }], [])));
        var (doubled, quadrupled, unused) = (Measure("Doubled", "[Amount] * 2"), Measure("Quadrupled", "[Doubled] * 2"), Measure("Unused", "[Amount]"));
        var visual = ByRegion with { Values = [new("Quadrupled")] };

        await VisualPipeline.PrepareAsync(model, visual, measures: [unused, doubled, quadrupled]);
        await VisualPipeline.PrepareAsync(model, visual with { Type = VisualType.Table }, measures: [unused, doubled, quadrupled]);

        var (chart, table) = (model.Requests[0], model.Requests[1]);
        Assert.Same(visual, chart.Visual);
        Assert.Equal([doubled, quadrupled], chart.Measures.OrderBy(m => m.Name));
        Assert.True(chart.HasTooltips);
        Assert.False(table.HasTooltips);
        Assert.Equal((EasyBILocalization.UICulture.Name, EasyBILocalization.FormatCulture.Name), (chart.UICulture, chart.FormatCulture));
        Assert.Equal(DateTime.Today, chart.Today);
    }

    [Fact]
    public async Task A_query_source_answers_in_the_language_of_the_request()
    {
        var visual = ByRegion with { Values = [new("Amount") { Time = TimeCalculation.PreviousYear }], Axis = [new("Date", DateGrain.Year)] };

        var french = await Model().GetDataAsync(new VisualDataRequest(visual) { UICulture = "fr-FR", FormatCulture = "fr-FR" });
        var english = await Model().GetDataAsync(new VisualDataRequest(visual) { UICulture = "en-US", FormatCulture = "en-US" });

        Assert.Equal("CA N-1", french.Result.Columns[1].Caption);
        Assert.Equal("CA, previous year", english.Result.Columns[1].Caption);
        Assert.Equal(CultureInfo.InvariantCulture, EasyBILocalization.UICulture); // the application's culture is left as it was
    }

    [Fact]
    public async Task A_query_source_computes_relative_periods_on_the_date_of_the_request()
    {
        var thisYear = new VisualDefinition
        {
            Model = "sales",
            Type = VisualType.Kpi,
            Values = [new("Amount")],
            Filters = [PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)],
        };

        var in2023 = await Model().GetDataAsync(new VisualDataRequest(thisYear) { Today = new(2023, 6, 1) });
        var in2024 = await Model().GetDataAsync(new VisualDataRequest(thisYear) { Today = new(2024, 6, 1, 18, 30, 0) });

        Assert.Equal(190m, Assert.Single(in2023.Result.Rows)[0]);
        Assert.Equal(235m, Assert.Single(in2024.Result.Rows)[0]);
    }

    [Fact]
    public async Task A_query_source_pivots_a_matrix()
    {
        var matrix = ByRegion with { Type = VisualType.Matrix, Legend = new("Date", DateGrain.Year) };

        var data = await Model().GetDataAsync(new VisualDataRequest(matrix));

        Assert.NotNull(data.Matrix);
        Assert.Equal(425d, Assert.Single(data.Matrix.GrandTotals));
    }
}
