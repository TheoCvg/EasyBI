using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

/// <summary>The built-in matrix renderer and the matrix in the designer.</summary>
public class MatrixUiTests
{
    private sealed record Sale(string Region, string Channel, decimal Amount);

    private static QueryableSemanticModel<Sale> Model() =>
        SemanticModel.For(new[] { new Sale("Nord", "Web", 10m), new Sale("Nord", "Shop", 20m), new Sale("Sud", "Web", 5m) }, "sales")
            .Dimension(s => s.Region, "Région")
            .Dimension(s => s.Channel, "Canal")
            .Measure(s => s.Amount, "CA", format: "N0");

    private static readonly VisualDefinition Matrix = new()
    {
        Model = "sales",
        Mark = MarkType.Matrix,
        Axis = [new("Region")],
        Legend = new("Channel"),
        Values = [new("Amount")],
    };

    private static RenderRequest Request(VisualDefinition? visual = null) =>
        Task.Run(() => VisualPipeline.PrepareAsync(Model(), visual ?? Matrix)).Result;

    // The grid shown in a window away from the screen, so that its rows and cells exist
    private static DataGrid Shown(RenderRequest request)
    {
        var grid = (DataGrid)new MatrixRenderer().Render(request);
        var window = new Window { Content = grid, Width = 800, Height = 400, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        return grid;
    }

    private static TextBlock Cell(DataGrid grid, int line, int column) =>
        (TextBlock)grid.Columns[column].GetCellContent((DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(line))!;

    [Fact]
    public void A_matrix_shows_its_rows_columns_totals_and_a_total_row() => Sta.Run(() =>
    {
        var grid = Shown(Request());

        Assert.Equal(["Région", "Shop", "Web", "Total"], grid.Columns.Select(c => (string)c.Header));
        Assert.Equal(3, grid.Items.Count); // Nord, Sud, Total
        Assert.Equal(["Nord", "20", "10", "30"], Enumerable.Range(0, 4).Select(c => Cell(grid, 0, c).Text));
        Assert.Equal(["Sud", "", "5", "5"], Enumerable.Range(0, 4).Select(c => Cell(grid, 1, c).Text));
        Assert.Equal(["Total", "20", "15", "35"], Enumerable.Range(0, 4).Select(c => Cell(grid, 2, c).Text));
        // Row headers keep the grid's header style, numbers have theirs (right-aligned)
        Assert.Same(grid.ColumnHeaderStyle, grid.Columns[0].HeaderStyle);
        Assert.Same(grid.ColumnHeaderStyle, grid.Columns[1].HeaderStyle.BasedOn);
    });

    [Fact]
    public void Several_measures_get_a_column_each_under_every_column() => Sta.Run(() =>
    {
        var grid = Shown(Request(Matrix with { Values = [new("Amount"), new("Amount", Aggregation.Count)] }));

        Assert.Equal(7, grid.Columns.Count); // Région, 2 × 2 cells, 2 totals
        Assert.StartsWith("Shop · ", (string)grid.Columns[1].Header);
        Assert.StartsWith("Total · ", (string)grid.Columns[5].Header);
    });

    [Fact]
    public void The_cells_left_out_of_the_selection_are_dimmed() => Sta.Run(() =>
    {
        var request = Request();
        var grid = Shown(request with { Highlight = [new MatrixSelection(0, 1)] }); // Nord · Web

        Assert.Equal(1, Cell(grid, 0, 2).Opacity);
        Assert.Equal(SelectionFilters.DimmedOpacity, Cell(grid, 0, 1).Opacity);
        Assert.Equal(1, Cell(grid, 0, 0).Opacity); // the row's header
        Assert.Equal(SelectionFilters.DimmedOpacity, Cell(grid, 1, 2).Opacity);
    });

    [Fact]
    public void Clicking_a_cell_or_a_column_total_selects_it() => Sta.Run(() =>
    {
        var picked = new List<DataSelection>();
        var grid = Shown(Request() with { Select = picked.Add });
        void Click(int line, int column) => Cell(grid, line, column).RaiseEvent(
            new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent });

        Click(0, 2); // Nord · Web
        Click(1, 0); // Sud's header
        Click(2, 1); // the total of Shop
        Click(2, 3); // the grand total: nothing

        Assert.Equal<DataSelection>([new MatrixSelection(0, 1), new MatrixSelection(1, null), new MatrixSelection(null, 0)], picked);
    });

    [Fact]
    public void The_designer_offers_the_matrix_with_rows_and_columns()
    {
        var designer = new DesignerViewModel(EasyBIContext.Create().AddModel(Model()));

        designer.SelectedMark = MarkType.Matrix;

        Assert.Contains(designer.MarkOptions, o => o.Value == MarkType.Matrix);
        Assert.Equal(("Rows", "Columns"), (designer.AxisWell.Label, designer.LegendWell.Label));
        Assert.True(designer.LegendWell.IsUsed);
        Assert.IsType<MatrixRenderer>(EasyBIContext.Create().ResolveRenderer(MarkType.Matrix));
    }
}
