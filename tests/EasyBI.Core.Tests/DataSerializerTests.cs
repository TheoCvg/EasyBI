using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>A model reached through the neutral JSON (a server) draws and filters visuals as the model itself.</summary>
public class DataSerializerTests
{
    private sealed record Sale(DateTime Date, DateOnly Shipped, string Region, int Store, bool Online, decimal Amount, decimal Cost);

    private static readonly Sale[] Sales =
    [
        new(new(2023, 1, 10), new(2023, 1, 12), "Nord", 1, true, 100.25m, 60m),
        new(new(2023, 2, 10), new(2023, 2, 11), "Sud", 2, false, 90m, 50m),
        new(new(2024, 1, 10), new(2024, 1, 15), "Nord", 1, false, 150m, 80m),
        new(new(2024, 3, 10), new(2024, 3, 10), "Sud", 3, true, 85.5m, 40m),
        new(new(2024, 3, 20), new(2024, 3, 22), "Est", 3, true, 0m, 0m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales", "Sales")
            .Dimension(s => s.Date, "Date")
            .Dimension(s => s.Shipped, "Shipped")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Store, "Store")
            .Dimension(s => s.Online, "Online")
            .Measure(s => s.Amount, "CA", format: "N0")
            .Ratio(s => s.Cost, s => s.Amount, "Cost rate", "CostRate", format: "P1");

    /// <summary>The application's side of a server: every request, result and value goes through the JSON.</summary>
    private sealed class JsonModel(ISemanticModel server) : ISemanticModel
    {
        public List<string> Requests { get; } = [];

        public ModelMetadata Metadata { get; } = DataSerializer.DeserializeMetadata(DataSerializer.Serialize(server.Metadata));

        public async Task<VisualData> GetDataAsync(VisualDataRequest request, CancellationToken cancellationToken = default)
        {
            var json = DataSerializer.Serialize(request);
            Requests.Add(json);
            var data = await server.GetDataAsync(DataSerializer.DeserializeRequest(json), cancellationToken);
            return DataSerializer.DeserializeData(DataSerializer.Serialize(data));
        }

        public async Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default)
        {
            var values = await server.GetDistinctValuesAsync(field, cancellationToken);
            return DataSerializer.DeserializeValues(DataSerializer.SerializeValues(values), Metadata.GetField(field).DataType);
        }
    }

    private static VisualDefinition Visual(VisualType type, FieldRef[] axis, FieldRef? legend = null, params MeasureRef[] values) =>
        new() { Model = "sales", Type = type, Axis = axis, Legend = legend, Values = values };

    public static TheoryData<string, VisualDefinition> Visuals => new()
    {
        { "bars by month with the previous year", Visual(VisualType.Bar, [new("Date", DateGrain.Month)], null, new MeasureRef("Amount"), new MeasureRef("Amount") { Time = TimeCalculation.PreviousYear }) },
        { "lines by day", Visual(VisualType.Line, [new("Date", DateGrain.Day)], null, new MeasureRef("Amount")) },
        { "dates without grain and a ratio", Visual(VisualType.Bar, [new("Shipped")], null, new MeasureRef("CostRate")) },
        { "numbers and booleans", Visual(VisualType.Bar, [new("Store")], new FieldRef("Online"), new MeasureRef("Amount")) },
        { "quarters and regions", Visual(VisualType.Bar, [new("Date", DateGrain.Quarter)], new FieldRef("Region"), new MeasureRef("Amount")) },
        { "year to date", Visual(VisualType.Line, [new("Date", DateGrain.Month)], null, new MeasureRef("Amount") { Time = TimeCalculation.YearToDate }) },
    };

    [Theory]
    [MemberData(nameof(Visuals))]
    public async Task A_chart_drawn_from_json_is_the_chart_of_the_model(string name, VisualDefinition visual)
    {
        var local = await VisualPipeline.PrepareAsync(Model(), visual);
        var remote = await VisualPipeline.PrepareAsync(new JsonModel(Model()), visual);

        Assert.True(local.Chart.Categories.SequenceEqual(remote.Chart.Categories), name);
        Assert.Equal(local.Chart.Series.Select(s => s.Name), remote.Chart.Series.Select(s => s.Name));
        Assert.Equal(local.Chart.Series.Select(s => s.Values.ToArray()), remote.Chart.Series.Select(s => s.Values.ToArray()));
        Assert.Equal(local.Result.Columns, remote.Result.Columns);
    }

    [Fact]
    public async Task A_matrix_drawn_from_json_keeps_its_members_cells_and_totals()
    {
        var visual = Visual(VisualType.Matrix, [new("Region"), new("Date", DateGrain.Month)], new FieldRef("Date", DateGrain.Year), new MeasureRef("Amount"));

        var local = (await VisualPipeline.PrepareAsync(Model(), visual)).Matrix!;
        var remote = (await VisualPipeline.PrepareAsync(new JsonModel(Model()), visual)).Matrix!;

        Assert.Equal(local.RowCaptions, remote.RowCaptions);
        Assert.Equal(local.Rows.Select(r => r.Labels), remote.Rows.Select(r => r.Labels));
        Assert.Equal(local.Columns.Select(c => c.Labels), remote.Columns.Select(c => c.Labels));
        Assert.Equal(local.Rows.Select(r => r.Values.Select(InvariantValue.Format)), remote.Rows.Select(r => r.Values.Select(InvariantValue.Format)));
        Assert.Equal(local.Cells, remote.Cells);
        Assert.Equal(local.RowTotals, remote.RowTotals);
        Assert.Equal(local.ColumnTotals, remote.ColumnTotals);
        Assert.Equal(local.GrandTotals, remote.GrandTotals);
    }

    [Fact]
    public async Task Clicking_a_visual_drawn_from_json_filters_like_the_model()
    {
        var visual = Visual(VisualType.Bar, [new("Date", DateGrain.Month)], new FieldRef("Online"), new MeasureRef("Amount"));
        var total = Visual(VisualType.Kpi, [], null, new MeasureRef("Amount"));
        var (local, remote) = (new FilterContext(), new FilterContext());
        var model = new JsonModel(Model());

        local.Select(await VisualPipeline.PrepareAsync(Model(), visual), new CategorySelection(3, 0));
        remote.Select(await VisualPipeline.PrepareAsync(model, visual), new CategorySelection(3, 0));
        var filtered = await VisualPipeline.PrepareAsync(model, remote.Apply(total));

        Assert.NotEmpty(remote.Apply(total).Filters);
        Assert.Equal(local.Apply(total).Filters, remote.Apply(total).Filters, new FilterComparer());
        Assert.Equal(85.5d, Assert.Single(filtered.Result.Rows)[0]);
    }

    [Fact]
    public async Task Distinct_values_keep_their_type()
    {
        var model = new JsonModel(Model());

        Assert.Equal(Sales.Select(s => (object?)s.Shipped).Distinct(), await model.GetDistinctValuesAsync("Shipped"));
        Assert.Equal(Sales.Select(s => (object?)s.Date).Distinct(), await model.GetDistinctValuesAsync("Date"));
        Assert.Equal([1, 2, 3], await model.GetDistinctValuesAsync("Store"));
        Assert.Equal([false, true], await model.GetDistinctValuesAsync("Online"));
    }

    [Fact]
    public void A_response_written_as_an_object_is_written_as_its_own_type()
    {
        IReadOnlyList<object?> values = [new DateOnly(2024, 3, 10), "Nord", 3];
        var data = new VisualData(new ResultSet([new("Store", "Store", FieldKind.Dimension, DataType.Number)], [[1]]));

        // Web frameworks serialize their results as object (ASP.NET Core's Results.Json)
        var json = JsonSerializer.Serialize<object>(values, DataSerializer.Options);

        Assert.Equal("[\"2024-03-10\",\"Nord\",3]", json);
        Assert.Equal(DataSerializer.Serialize(data), JsonSerializer.Serialize<object>(data, DataSerializer.Options));
    }

    [Fact]
    public void Metadata_travels_with_its_fields_and_nothing_computed()
    {
        var json = DataSerializer.Serialize(Model().Metadata);
        var metadata = DataSerializer.DeserializeMetadata(json);

        Assert.Equal(Model().Metadata.Fields, metadata.Fields);
        Assert.Equal(("sales", "Sales"), (metadata.Name, metadata.Caption));
        Assert.DoesNotContain("dimensions", json);
    }

    [Fact]
    public void A_request_keeps_the_day_of_today_whatever_the_time_zone()
    {
        var visual = Visual(VisualType.Kpi, [], null, new MeasureRef("Amount"));
        var request = new VisualDataRequest(visual)
        {
            Today = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Local),
            Measures = [new CalculatedMeasure { Name = "Twice", Model = "sales", Formula = "[Amount] * 2" }],
            HasTooltips = true,
            UICulture = "fr-FR",
        };

        var json = DataSerializer.Serialize(request);
        var received = DataSerializer.DeserializeRequest(json);

        Assert.Contains("\"today\":\"2024-06-01\"", json);
        Assert.Equal(new DateTime(2024, 6, 1), received.Today);
        Assert.Equal(request.Measures, received.Measures);
        Assert.Equal((true, "fr-FR", null), (received.HasTooltips, received.UICulture, received.FormatCulture));
        Assert.Equal(visual.Values, received.Visual.Values);
    }

    [Fact]
    public void A_result_is_written_as_columns_and_rows_of_plain_values()
    {
        var result = new ResultSet(
            [
                new("Date.Month", "Date (mois)", FieldKind.Dimension, DataType.Date),
                new("Shipped", "Shipped", FieldKind.Dimension, DataType.Date),
                new("Amount", "CA", FieldKind.Measure, DataType.Number, "N0"),
            ],
            [[new DatePeriod(DateGrain.Month, 202403), new DateOnly(2024, 3, 10), 85.5m], [null, null, double.NaN]]);

        var json = DataSerializer.Serialize(new VisualData(result));
        var read = DataSerializer.DeserializeData(json).Result;

        Assert.Contains("\"rows\":[[{\"grain\":\"Month\",\"key\":202403},\"2024-03-10\",85.5],[null,null,\"NaN\"]]", json);
        Assert.Equal([new DatePeriod(DateGrain.Month, 202403), new DateOnly(2024, 3, 10), 85.5d], read.Rows[0]);
        Assert.Equal([null, null, double.NaN], read.Rows[1]);
    }

    [Fact]
    public void A_row_longer_than_the_columns_is_refused()
    {
        const string json = """{"result":{"columns":[{"key":"A","caption":"A","role":"Measure","dataType":"Number"}],"rows":[[1,2]]}}""";

        Assert.Throws<JsonException>(() => DataSerializer.DeserializeData(json));
    }

    private sealed class FilterComparer : IEqualityComparer<FilterSpec>
    {
        public bool Equals(FilterSpec? x, FilterSpec? y) => x is not null && y is not null
            && (x.Field, x.Operator, x.Grain) == (y.Field, y.Operator, y.Grain)
            && x.Values.SequenceEqual(y.Values)
            && (x.Alternatives ?? []).Count == (y.Alternatives ?? []).Count
            && (x.Alternatives ?? []).Zip(y.Alternatives ?? [], (a, b) => a.SequenceEqual(b, this)).All(same => same);

        public int GetHashCode(FilterSpec obj) => obj.Field.GetHashCode();
    }
}
