using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// The look of chart tooltips. An application replaces the content's look by defining a <see cref="DataTemplate"/>
/// for <see cref="TooltipContent"/> under <see cref="TemplateKey"/> in its resources; the frame is a standard
/// <see cref="ToolTip"/>, so it takes the application's tooltip style.
/// </summary>
public static class ChartTooltips
{
    /// <summary>The <see cref="DataTemplate"/> drawing a <see cref="TooltipContent"/> (EasyBI's default is in Generic.xaml).</summary>
    public static ComponentResourceKey TemplateKey { get; } = new(typeof(ChartTooltips), "Template");

    /// <summary>True for a non-empty text (the default template shows a template's text instead of the items).</summary>
    public static System.Windows.Data.IValueConverter HasText { get; } = new HasTextConverter();

    private sealed class HasTextConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is string { Length: > 0 };

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

/// <summary>
/// Shows tooltips over a chart, near the pointer: what a connector uses to show the content EasyBI gives it
/// (<c>request.TooltipFor(category, series)</c>). The tooltip belongs to the chart, so it finds the chart's
/// resources (theme, templates).
/// </summary>
public sealed class ChartTooltipHost
{
    private readonly FrameworkElement _owner;
    private readonly ToolTip _tooltip;

    public ChartTooltipHost(FrameworkElement owner)
    {
        _owner = owner;
        // Custom placement: a relative one is mirrored (opens left of the pointer) where menus drop right-aligned
        _tooltip = new ToolTip { PlacementTarget = owner, Placement = PlacementMode.Custom, CustomPopupPlacementCallback = Place, IsHitTestVisible = false };
        // Owned by the chart for its resources, but opened by this host only, not by WPF's tooltip service
        owner.ToolTip = _tooltip;
        ToolTipService.SetIsEnabled(owner, false);
        owner.MouseLeave += (_, _) => Hide();
    }

    /// <summary>The tooltip shown (its content is the last <see cref="TooltipContent"/> shown).</summary>
    public ToolTip ToolTip => _tooltip;

    /// <summary>Shows <paramref name="content"/> next to the pointer; null hides the tooltip.</summary>
    public void Show(TooltipContent? content)
    {
        if (!_owner.Dispatcher.CheckAccess())
        {
            _owner.Dispatcher.BeginInvoke(() => Show(content));
            return;
        }
        if (content is null)
        {
            Hide();
            return;
        }
        var pointer = Mouse.GetPosition(_owner);
        // Looked up from the chart each time: the application's template, else EasyBI's (a tooltip is not in the tree)
        _tooltip.ContentTemplate = _owner.TryFindResource(ChartTooltips.TemplateKey) as DataTemplate;
        _tooltip.Content = content;
        _tooltip.HorizontalOffset = pointer.X;
        _tooltip.VerticalOffset = pointer.Y;
        _tooltip.IsOpen = true;
    }

    // Below right of the pointer (the offsets, which WPF adds), else below left of it when there is no room; in device pixels
    private CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        var dpi = VisualTreeHelper.GetDpi(_owner);
        var gap = new Vector(16 * dpi.DpiScaleX, 16 * dpi.DpiScaleY);
        return
        [
            new(new Point(gap.X, gap.Y), PopupPrimaryAxis.Horizontal),
            new(new Point(-popup.Width - gap.X / 2, gap.Y), PopupPrimaryAxis.Horizontal),
        ];
    }

    public void Hide()
    {
        if (!_owner.Dispatcher.CheckAccess())
        {
            _owner.Dispatcher.BeginInvoke(Hide);
            return;
        }
        _tooltip.IsOpen = false;
    }
}
