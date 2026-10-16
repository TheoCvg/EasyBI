using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Lays children out horizontally, sharing the available width in proportion to their <c>Weight</c>.</summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight", typeof(double), typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetWeight(UIElement element) => (double)element.GetValue(WeightProperty);

    public static void SetWeight(UIElement element, double value) => element.SetValue(WeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var total = TotalWeight();
        var width = double.IsInfinity(availableSize.Width) ? InternalChildren.Count * 400d : availableSize.Width;
        var height = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(width * Weight(child) / total, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var total = TotalWeight();
        var x = 0d;
        foreach (UIElement child in InternalChildren)
        {
            var width = finalSize.Width * Weight(child) / total;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }

    private double TotalWeight() => Math.Max(1e-6, InternalChildren.Cast<UIElement>().Sum(Weight));

    private static double Weight(UIElement child) => Math.Max(1e-6, GetWeight(child));
}
