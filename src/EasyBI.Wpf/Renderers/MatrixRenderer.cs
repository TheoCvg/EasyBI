using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// Built-in renderer of a <see cref="MarkType.Matrix"/>: the headers of its rows, a column per column and measure, a
/// total column per measure and a total row, in a grid styled like the table.
/// </summary>
public sealed class MatrixRenderer : IVisualRenderer
{
    // Above numbers, a header is aligned like them
    private static readonly Style NumberHeaderStyle = CreateNumberHeaderStyle();

    public string Name => "EasyBI Matrix";

    private static Style CreateNumberHeaderStyle()
    {
        var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), TableRenderer.HeaderStyle);
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
        // Shared by every grid, whatever its thread: a sealed style belongs to none
        style.Seal();
        return style;
    }

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Matrix });

    /// <summary>A line of the grid: a row of the matrix, or its total (no row).</summary>
    public sealed record Line(int? Row, object?[] Cells, double[] Opacities, ValueStyle?[] Styles);

    // A column of the grid: a row header, a column of the matrix (and a measure), or a total (no column)
    private sealed record GridColumn(string Header, int? Column, int Measure, bool IsRowHeader);

    public FrameworkElement Render(RenderRequest request)
    {
        var matrix = request.Matrix ?? throw new ArgumentException("The request has no matrix data.", nameof(request));
        var measures = matrix.Measures;
        var several = measures.Count > 1;

        // Without a row dimension, one header column holds the total row's caption
        List<GridColumn> columns = [.. (matrix.RowCaptions.Count > 0 ? matrix.RowCaptions : [""]).Select(c => new GridColumn(c, null, 0, true))];
        for (var c = 0; c < matrix.Columns.Count; c++)
            for (var m = 0; m < measures.Count; m++)
                columns.Add(new GridColumn(several ? $"{matrix.Columns[c].Labels[0]} · {measures[m].Caption}" : matrix.Columns[c].Labels[0], c, m, false));
        for (var m = 0; m < measures.Count; m++)
        {
            var header = matrix.Columns.Count == 0 ? measures[m].Caption : several ? $"{Strings.MatrixTotal} · {measures[m].Caption}" : Strings.MatrixTotal;
            columns.Add(new GridColumn(header, null, m, false));
        }

        // A measure's format compares its cells with each other, not with the totals
        var stylers = measures
            .Select((m, i) => ValueStyler.For(request.Visual.Values.FirstOrDefault(v => v.Key == m.Key),
                matrix.Cells.SelectMany(row => row.Select(cell => cell[i])), request.Theme))
            .ToArray();
        var lines = matrix.Rows.Select((row, r) => LineOf(request, columns, stylers, r, row.Labels, c => matrix.Cells[r][c], matrix.RowTotals[r])).ToList();
        lines.Add(LineOf(request, columns, stylers, null, [Strings.MatrixTotal], c => matrix.ColumnTotals[c], matrix.GrandTotals));

        var grid = TableRenderer.Grid(lines);
        for (var i = 0; i < columns.Count; i++)
            grid.Columns.Add(stylers[columns[i].Measure] is not null && columns[i] is { IsRowHeader: false, Column: not null }
                ? Formatted(columns[i], i, measures)
                : Column(columns[i], i, measures));
        grid.LoadingRow += (_, e) =>
            e.Row.FontWeight = e.Row.Item is Line { Row: null } ? FontWeights.SemiBold : FontWeights.Normal;

        if (request.Select is { } select)
        {
            grid.Cursor = System.Windows.Input.Cursors.Hand;
            grid.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (Ancestor<DataGridCell>(e.OriginalSource as DependencyObject) is not { DataContext: Line line } cell) return;
                var column = columns[cell.Column.DisplayIndex];
                // A header or a total: the row; a cell: the row and the column (the column's total: the column)
                if (column.Column is { } c) select(new MatrixSelection(line.Row, c));
                else if (line.Row is { } r) select(new MatrixSelection(r, null));
            };
        }
        return grid;
    }

    private static Line LineOf(
        RenderRequest request,
        List<GridColumn> columns,
        ValueStyler?[] stylers,
        int? row,
        IReadOnlyList<string> labels,
        Func<int, IReadOnlyList<double?>> column,
        IReadOnlyList<double?> totals)
    {
        var cells = new object?[columns.Count];
        var opacities = new double[columns.Count];
        var styles = new ValueStyle?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            cells[i] = c.IsRowHeader ? labels.ElementAtOrDefault(i) : c.Column is { } index ? column(index)[c.Measure] : totals[c.Measure];
            opacities[i] = request.IsMatrixEmphasized(row, c.IsRowHeader ? null : c.Column) ? 1 : SelectionFilters.DimmedOpacity;
            if (row is not null && c is { IsRowHeader: false, Column: not null }) styles[i] = stylers[c.Measure]?.Style(cells[i] as double?);
        }
        return new Line(row, cells, opacities, styles);
    }

    private static DataGridTemplateColumn Formatted(GridColumn column, int index, IReadOnlyList<MatrixMeasure> measures)
    {
        var formatted = TableRenderer.FormattedColumn(column.Header, $"{nameof(Line.Cells)}[{index}]", measures[column.Measure].Format,
            new Binding($"{nameof(Line.Styles)}[{index}]"), new Binding($"{nameof(Line.Opacities)}[{index}]"));
        formatted.HeaderStyle = NumberHeaderStyle;
        formatted.MinWidth = 60;
        return formatted;
    }

    private static DataGridTextColumn Column(GridColumn column, int index, IReadOnlyList<MatrixMeasure> measures)
    {
        var binding = new Binding($"{nameof(Line.Cells)}[{index}]") { ConverterCulture = EasyBILocalization.FormatCulture };
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(UIElement.OpacityProperty, new Binding($"{nameof(Line.Opacities)}[{index}]")));
        if (!column.IsRowHeader)
        {
            binding.StringFormat = $"{{0:{measures[column.Measure].Format ?? "#,##0.##"}}}";
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        }
        // Row headers and totals stand out from the cells
        if (column.IsRowHeader || column.Column is null)
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        var gridColumn = new DataGridTextColumn
        {
            Header = column.Header,
            Binding = binding,
            ElementStyle = style,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 60,
        };
        // Set only for numbers: a local value, even null, would hide the grid's header style
        if (!column.IsRowHeader) gridColumn.HeaderStyle = NumberHeaderStyle;
        return gridColumn;
    }

    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        return element as T;
    }
}
