using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Places children on a grid: <see cref="Columns"/> columns sharing the width, rows of <see cref="RowHeight"/>,
/// separated by <see cref="Gap"/>. Each child covers the cells given by its attached Column, Row and spans.
/// </summary>
public sealed class DashboardGridPanel : Panel
{
    private const FrameworkPropertyMetadataOptions ParentLayout =
        FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange;

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight), typeof(double), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(110d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnProperty = DependencyProperty.RegisterAttached(
        "Column", typeof(int), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(0, ParentLayout));

    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached(
        "Row", typeof(int), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(0, ParentLayout));

    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(1, ParentLayout));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(DashboardGridPanel), new FrameworkPropertyMetadata(1, ParentLayout));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public static int GetColumn(UIElement e) => (int)e.GetValue(ColumnProperty);
    public static void SetColumn(UIElement e, int value) => e.SetValue(ColumnProperty, value);
    public static int GetRow(UIElement e) => (int)e.GetValue(RowProperty);
    public static void SetRow(UIElement e, int value) => e.SetValue(RowProperty, value);
    public static int GetColumnSpan(UIElement e) => (int)e.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(UIElement e, int value) => e.SetValue(ColumnSpanProperty, value);
    public static int GetRowSpan(UIElement e) => (int)e.GetValue(RowSpanProperty);
    public static void SetRowSpan(UIElement e, int value) => e.SetValue(RowSpanProperty, value);

    /// <summary>Distance between the starts of two neighbouring columns, for a given panel width.</summary>
    public double ColumnPitch(double width) => ColumnWidth(width) + Gap;

    /// <summary>Distance between the starts of two neighbouring rows.</summary>
    public double RowPitch => RowHeight + Gap;

    private double ColumnWidth(double width) => Math.Max(0, (width - Gap * (Math.Max(1, Columns) - 1)) / Math.Max(1, Columns));

    private Rect CellRect(UIElement child, double width)
    {
        var columnWidth = ColumnWidth(width);
        int column = GetColumn(child), row = GetRow(child);
        int columnSpan = Math.Max(1, GetColumnSpan(child)), rowSpan = Math.Max(1, GetRowSpan(child));
        return new Rect(
            column * (columnWidth + Gap),
            row * RowPitch,
            columnSpan * columnWidth + (columnSpan - 1) * Gap,
            rowSpan * RowHeight + (rowSpan - 1) * Gap);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 1200 : availableSize.Width;
        var bottom = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var rect = CellRect(child, width);
            child.Measure(rect.Size);
            bottom = Math.Max(bottom, rect.Bottom);
        }
        return new Size(width, bottom);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Arrange(CellRect(child, finalSize.Width));
        return finalSize;
    }
}

/// <summary>Width of a scrolling or wrapping band minus the gap it absorbs (for full-row items).</summary>
public sealed class MinusGapConverter : System.Windows.Data.IValueConverter
{
    public static MinusGapConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        Math.Max(0, (double)value - DashboardViewModel.Gap);

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
