using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Renderers;

/// <summary>Renderer intégré : affiche le ResultSet brut dans une DataGrid.</summary>
public sealed class TableRenderer : IVisualRenderer
{
    public string Name => "EasyBI Table";

    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Table });

    public FrameworkElement Render(RenderRequest request)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            Background = Brushes.Transparent,
            RowBackground = Brushes.Transparent,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB)),
            RowHeight = 26,
            BorderThickness = new Thickness(0),
            ItemsSource = request.Result.Rows,
        };

        for (var i = 0; i < request.Result.Columns.Count; i++)
        {
            var column = request.Result.Columns[i];
            var binding = new Binding($"[{i}]") { ConverterCulture = CultureInfo.CurrentCulture };
            if (column.Role == FieldKind.Measure)
                binding.StringFormat = $"{{0:{column.Format ?? "#,##0.##"}}}";
            else if (column.DataType == DataType.Date)
                binding.StringFormat = "{0:d}";

            var style = new Style(typeof(TextBlock));
            if (column.Role == FieldKind.Measure)
                style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Caption,
                Binding = binding,
                ElementStyle = style,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        }
        return grid;
    }
}
