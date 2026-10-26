using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Renderers;

/// <summary>
/// Turns the pages of a chart's legend (◀ 2/5 ▶), shown over the chart in the corner next to its legend while the legend
/// has pages (see <see cref="LegendPager"/>); the mouse wheel over it turns them too. Lookless: its default template
/// is in Generic.xaml.
/// </summary>
public class ChartLegendPager : Control
{
    public static readonly DependencyProperty PagerProperty = DependencyProperty.Register(
        nameof(Pager), typeof(LegendPager), typeof(ChartLegendPager), new PropertyMetadata(null, (d, e) => ((ChartLegendPager)d).OnPagerChanged(e)));

    private static readonly DependencyPropertyKey PageTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PageText), typeof(string), typeof(ChartLegendPager), new PropertyMetadata(""));

    public static readonly DependencyProperty PageTextProperty = PageTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanGoBackPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanGoBack), typeof(bool), typeof(ChartLegendPager), new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoBackProperty = CanGoBackPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanGoForwardPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanGoForward), typeof(bool), typeof(ChartLegendPager), new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoForwardProperty = CanGoForwardPropertyKey.DependencyProperty;

    static ChartLegendPager()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ChartLegendPager), new FrameworkPropertyMetadata(typeof(ChartLegendPager)));
    }

    public ChartLegendPager()
    {
        PreviousCommand = new RelayCommand(() => Pager?.GoTo(Pager.Page - 1), () => CanGoBack);
        NextCommand = new RelayCommand(() => Pager?.GoTo(Pager.Page + 1), () => CanGoForward);
        Sync();
    }

    /// <summary>The pages of the legend, shared with the connector drawing the chart.</summary>
    public LegendPager? Pager
    {
        get => (LegendPager?)GetValue(PagerProperty);
        set => SetValue(PagerProperty, value);
    }

    /// <summary>The page shown and the number of pages ("2/5").</summary>
    public string PageText => (string)GetValue(PageTextProperty);

    public bool CanGoBack => (bool)GetValue(CanGoBackProperty);

    public bool CanGoForward => (bool)GetValue(CanGoForwardProperty);

    public ICommand PreviousCommand { get; }

    public ICommand NextCommand { get; }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Pager is not { } pager) return;
        pager.GoTo(pager.Page + (e.Delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    private void OnPagerChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LegendPager old) old.Changed -= OnPagesChanged;
        if (e.NewValue is LegendPager pager) pager.Changed += OnPagesChanged;
        Sync();
    }

    private void OnPagesChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Sync();
        else Dispatcher.BeginInvoke(Sync);
    }

    // Shown while the legend has pages, in the corner next to it: below a legend on a side, at the end of a row
    private void Sync()
    {
        var pager = Pager;
        var paged = pager is { PageCount: > 1 } && pager.Position != ChartLegend.Hidden;
        Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
        if (pager is null) return;
        SetValue(PageTextPropertyKey, $"{pager.Page + 1}/{pager.PageCount}");
        SetValue(CanGoBackPropertyKey, pager.Page > 0);
        SetValue(CanGoForwardPropertyKey, pager.Page < pager.PageCount - 1);
        HorizontalAlignment = pager.Position == ChartLegend.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        VerticalAlignment = pager.Position == ChartLegend.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        CommandManager.InvalidateRequerySuggested();
    }
}
