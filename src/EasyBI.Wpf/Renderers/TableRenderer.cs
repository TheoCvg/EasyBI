using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Renderers;

/// <summary>Built-in renderer: shows the raw ResultSet in a DataGrid.</summary>
public sealed class TableRenderer : IVisualRenderer
{
    // Headers follow the theme (the system's stay light whatever the theme)
    private static readonly Style HeaderStyle = CreateHeaderStyle();

    public string Name => "EasyBI Table";

    private static Style CreateHeaderStyle()
    {
        var style = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(EasyBITheme.MutedForegroundBrushKey)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(EasyBITheme.GridLineBrushKey)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        return style;
    }

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
            Background = Brushes.Transparent,
            RowBackground = Brushes.Transparent,
            // A minimum, not a fixed height: control libraries give cells larger paddings
            MinRowHeight = 26,
            BorderThickness = new Thickness(0),
            ItemsSource = request.Result.Rows,
            ColumnHeaderStyle = HeaderStyle,
        }
            .Themed(DataGrid.HorizontalGridLinesBrushProperty, EasyBITheme.GridLineBrushKey)
            .Themed(DataGrid.AlternatingRowBackgroundProperty, EasyBITheme.SurfaceAltBrushKey)
            .Themed(Control.ForegroundProperty, EasyBITheme.ForegroundBrushKey);

        for (var i = 0; i < request.Result.Columns.Count; i++)
        {
            var column = request.Result.Columns[i];
            var binding = new Binding($"[{i}]") { ConverterCulture = EasyBILocalization.FormatCulture };
            if (column.Role == FieldKind.Measure)
                binding.StringFormat = $"{{0:{column.Format ?? "#,##0.##"}}}";
            else
                binding.Converter = DisplayConverter.Instance;

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
        if (request.Highlight is { Count: > 0 })
        {
            // Rows are recycled while scrolling: their opacity is set each time one shows a row
            grid.LoadingRow += (_, e) =>
                e.Row.Opacity = request.IsRowEmphasized(e.Row.GetIndex()) ? 1 : SelectionFilters.DimmedOpacity;
        }
        if (request.Select is { } select)
        {
            grid.Cursor = System.Windows.Input.Cursors.Hand;
            grid.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (ItemsControl.ContainerFromElement(grid, (DependencyObject)e.OriginalSource) is DataGridRow row)
                    select(new RowSelection(row.GetIndex()));
            };
        }
        return grid;
    }
}

/// <summary>Displays dimension values (dates, periods, booleans, blanks) as the charts do.</summary>
internal sealed class DisplayConverter : IValueConverter
{
    public static DisplayConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => ValueFormatter.Display(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
