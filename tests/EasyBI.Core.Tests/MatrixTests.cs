using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Core.Tests;

/// <summary>Matrix: rows (axis) × columns (legend) × values, with totals computed at their own level.</summary>
public class MatrixTests
{
    private sealed record Sale(string Region, string Channel, string Category, DateTime Date, decimal Amount);

    private static readonly Sale[] Sales =
    [
        new("Nord", "Web", "A", new(2024, 1, 10), 100m),
        new("Nord", "Web", "B", new(2024, 4, 10), 350m),
        new("Nord", "Shop", "A", new(2024, 2, 10), 200m),
        new("Sud", "Web", "A", new(2024, 5, 10), 50m),
        new("Sud", "Shop", "B", new(2024, 1, 20), 10m),
        new("Est", "Shop", "A", new(2024, 7, 1), 1000m),
    ];

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(Sales, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Dimension(s => s.Category, "Catégorie")
            .Dimension(s => s.Date, "Date")
            .Measure(s => s.Amount, "CA")
            .Measure(s => s.Amount, "CA moyen", Aggregation.Average, name: "AverageAmount");

    private static readonly VisualDefinition ByRegionAndChannel = new()
    {
        Id = "matrix",
        Model = "sales",
        Mark = MarkType.Matrix,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount")],
    };

    private static async Task<RenderRequest> PrepareAsync(VisualDefinition visual) => await VisualPipeline.PrepareAsync(Model(), visual);

    private static double? Cell(MatrixData matrix, string row, string column, int measure = 0) =>
        matrix.Cells[Row(matrix, row)][Column(matrix, column)][measure];

    private static int Row(MatrixData matrix, string label) => matrix.Rows.ToList().FindIndex(r => r.Labels[0] == label);

    private static int Column(MatrixData matrix, string label) => matrix.Columns.ToList().FindIndex(c => c.Labels[0] == label);

    [Fact]
    public async Task Rows_cross_columns_with_the_totals_of_each_row_each_column_and_the_whole()
    {
        var matrix = (await PrepareAsync(ByRegionAndChannel)).Matrix!;

        Assert.Equal(["Région"], matrix.RowCaptions);
        Assert.Equal("Canal", matrix.ColumnCaption);
        Assert.Equal(["Est", "Nord", "Sud"], matrix.Rows.Select(r => r.Labels[0]));
        Assert.Equal(["Shop", "Web"], matrix.Columns.Select(c => c.Labels[0]));
        Assert.Equal(450d, Cell(matrix, "Nord", "Web"));
        Assert.Null(Cell(matrix, "Est", "Web")); // no sale
        Assert.Equal([1000d, 650d, 60d], matrix.RowTotals.Select(t => t[0]));
        Assert.Equal([1210d, 500d], matrix.ColumnTotals.Select(t => t[0]));
        Assert.Equal(1710d, matrix.GrandTotals[0]);
    }

    [Fact]
    public async Task Totals_compute_the_measure_on_their_rows_instead_of_adding_cells()
    {
        var matrix = (await PrepareAsync(ByRegionAndChannel with { Values = [new("AverageAmount")] })).Matrix!;

        Assert.Equal(225d, Cell(matrix, "Nord", "Web"));
        Assert.Equal(650d / 3, matrix.RowTotals[Row(matrix, "Nord")][0]!.Value, 10); // (100 + 350 + 200) / 3
        Assert.Equal(1710d / 6, matrix.GrandTotals[0]!.Value, 10);
    }

    [Fact]
    public async Task Each_cell_holds_every_measure()
    {
        var matrix = (await PrepareAsync(ByRegionAndChannel with { Values = [new("Amount"), new("AverageAmount")] })).Matrix!;

        Assert.Equal(["CA", "CA moyen"], matrix.Measures.Select(m => m.Caption));
        Assert.Equal((450d, 225d), (Cell(matrix, "Nord", "Web", 0)!.Value, Cell(matrix, "Nord", "Web", 1)!.Value));
    }

    [Fact]
    public async Task Sort_and_top_choose_the_rows_and_the_column_totals_only_count_them()
    {
        var visual = ByRegionAndChannel with { Sort = new SortSpec("Amount", Descending: true), Top = 2 };

        var matrix = (await PrepareAsync(visual)).Matrix!;

        Assert.Equal(["Est", "Nord"], matrix.Rows.Select(r => r.Labels[0]));
        Assert.Equal([1200d, 450d], matrix.ColumnTotals.Select(t => t[0]));
        Assert.Equal(1650d, matrix.GrandTotals[0]);
    }

    [Fact]
    public async Task A_measure_filter_keeps_the_rows_whose_total_matches()
    {
        var visual = ByRegionAndChannel with { Filters = [NumericFilters.Compare("Amount", FilterOperator.LessThan, 700)] };

        var matrix = (await PrepareAsync(visual)).Matrix!;

        Assert.Equal(["Nord", "Sud"], matrix.Rows.Select(r => r.Labels[0]));
        Assert.Equal(10d, Cell(matrix, "Sud", "Shop")); // cells are not filtered themselves
        Assert.Equal(710d, matrix.GrandTotals[0]);
    }

    [Fact]
    public async Task Rows_of_several_dimensions_are_kept_exactly()
    {
        var visual = ByRegionAndChannel with
        {
            Axis = [new("Region"), new("Category")],
            Legend = new("Date", DateGrain.Quarter),
            Sort = new SortSpec("Amount", Descending: true),
            Top = 2,
        };

        var matrix = (await PrepareAsync(visual)).Matrix!;

        // Est·A 1000 and Nord·B 350: Region in (Est, Nord) and Category in (A, B) would also count Nord·A (300)
        Assert.Equal([["Est", "A"], ["Nord", "B"]], matrix.Rows.Select(r => r.Labels));
        Assert.Equal(1350d, matrix.GrandTotals[0]);
        Assert.Equal(["2024 Q2", "2024 Q3"], matrix.Columns.Select(c => c.Labels[0]));
    }

    [Fact]
    public async Task Without_columns_a_matrix_shows_its_rows_and_their_total()
    {
        var matrix = (await PrepareAsync(ByRegionAndChannel with { Legend = null })).Matrix!;

        Assert.Empty(matrix.Columns);
        Assert.Null(matrix.ColumnCaption);
        Assert.Equal([1000d, 650d, 60d], matrix.RowTotals.Select(t => t[0]));
        Assert.Equal(1710d, matrix.GrandTotals[0]);
    }

    [Fact]
    public async Task Without_rows_a_matrix_shows_the_totals_of_its_columns()
    {
        var matrix = (await PrepareAsync(ByRegionAndChannel with { Axis = [] })).Matrix!;

        Assert.Empty(matrix.Rows);
        Assert.Equal([1210d, 500d], matrix.ColumnTotals.Select(t => t[0]));
        Assert.Equal(1710d, matrix.GrandTotals[0]);
    }

    [Fact]
    public async Task Clicking_a_cell_a_row_or_a_column_filters_the_other_visuals_on_it()
    {
        var request = await PrepareAsync(ByRegionAndChannel);
        var matrix = request.Matrix!;
        var total = new VisualDefinition { Model = "sales", Mark = MarkType.Kpi, Values = [new("Amount")] };
        async Task<decimal> TotalAsync(FilterContext filters) => (decimal)(await Model().ExecuteAsync(filters.Apply(total).ToQuery())).Rows[0][0]!;

        var filters = new FilterContext();
        filters.Select(request, new MatrixSelection(Row(matrix, "Nord"), Column(matrix, "Web")));
        Assert.Equal(450m, await TotalAsync(filters));

        filters.Select(request, new MatrixSelection(Row(matrix, "Sud"), null));
        Assert.Equal(60m, await TotalAsync(filters));

        filters.Select(request, new MatrixSelection(null, Column(matrix, "Shop")));
        Assert.Equal(1210m, await TotalAsync(filters));
        Assert.Equal("Canal = Shop", filters.Selections[0].Describe(Model().Metadata));
    }

    [Fact]
    public async Task The_matrix_emphasizes_the_cells_rows_and_columns_of_its_selection()
    {
        var request = await PrepareAsync(ByRegionAndChannel);
        var matrix = request.Matrix!;
        var (nord, sud, web, shop) = (Row(matrix, "Nord"), Row(matrix, "Sud"), Column(matrix, "Web"), Column(matrix, "Shop"));
        var filters = new FilterContext();
        filters.Select(request, new MatrixSelection(nord, web));
        filters.Select(request, new MatrixSelection(null, shop), extend: true);

        var source = request with { Highlight = filters.HighlightFor(request) };

        Assert.True(source.IsMatrixEmphasized(nord, web));
        Assert.False(source.IsMatrixEmphasized(sud, web));
        Assert.True(source.IsMatrixEmphasized(sud, shop)); // the whole column
        Assert.True(source.IsMatrixEmphasized(nord, null)); // the row of a selected cell
        Assert.False(source.IsMatrixEmphasized(sud, null));
        Assert.True(source.IsMatrixEmphasized(null, shop)); // the column's total
        Assert.False(source.IsMatrixEmphasized(null, null));
    }
}
