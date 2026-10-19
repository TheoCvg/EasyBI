using System.Windows;
using System.Windows.Controls;
using EasyBI.Abstractions;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf.Tests;

/// <summary>A control library styles standard controls through implicit styles: EasyBI must not hide them.</summary>
public class ControlLibraryStyleTests
{
    [Fact]
    public void Field_lists_take_the_applications_list_box_and_item_styles() => Sta.Run(() =>
    {
        var listStyle = new Style(typeof(ListBox));
        var host = new Border { Resources = { [typeof(ListBox)] = listStyle }, Child = new FieldList() };
        host.Measure(new Size(300, 600));

        foreach (var name in new[] { "DimensionList", "MeasureList" })
        {
            var list = (ListBox)LogicalTreeHelper.FindLogicalNode(host, name);
            Assert.Same(listStyle, list.Style);
            Assert.Null(list.ItemContainerStyle); // items take the implicit ListBoxItem style, selection included
        }
    });

    [Fact]
    public void Table_rows_grow_with_the_cell_padding_of_a_control_library() => Sta.Run(() =>
    {
        var result = new ResultSet([new ResultColumn("Region", "Region", FieldKind.Dimension, DataType.Text)], [["North"], ["South"]]);
        var request = new RenderRequest(new VisualDefinition { Mark = MarkType.Table }, result, new ChartData(null, [], []), new BiTheme([]));
        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        cellStyle.Setters.Add(new Setter(Control.TemplateProperty, CellTemplate()));
        var host = new Border { Resources = { [typeof(DataGridCell)] = cellStyle }, Child = new TableRenderer().Render(request) };

        host.Measure(new Size(400, 400));
        host.Arrange(new Rect(0, 0, 400, 400));

        var row = (DataGridRow)((DataGrid)host.Child).ItemContainerGenerator.ContainerFromIndex(0);
        Assert.True(row.ActualHeight > 40, $"row height {row.ActualHeight}");
    });

    // The system's cell template ignores Padding; libraries' templates honor it
    private static ControlTemplate CellTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        return new ControlTemplate(typeof(DataGridCell)) { VisualTree = presenter };
    }
}
