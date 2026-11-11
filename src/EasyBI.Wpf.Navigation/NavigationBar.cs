using System.Windows;
using System.Windows.Controls;
using EasyBI.Core;
using EasyBI.Wpf.Navigation.Resources;

namespace EasyBI.Wpf.Navigation;

/// <summary>
/// The path of a <see cref="DashboardNavigation"/>: a Back button, then the reports followed, each one a button going
/// back to it, the current one last. Collapsed while the user has not followed a link.
/// </summary>
public sealed class NavigationBar : ContentControl
{
    public static readonly DependencyProperty NavigationProperty = DependencyProperty.Register(
        nameof(Navigation), typeof(DashboardNavigation), typeof(NavigationBar), new PropertyMetadata(null, OnNavigationChanged));

    public NavigationBar()
    {
        Focusable = false;
        Loaded += (_, _) => EasyBILocalization.CultureChanged += OnCultureChanged;
        Unloaded += (_, _) => EasyBILocalization.CultureChanged -= OnCultureChanged;
        Build();
    }

    public DashboardNavigation? Navigation
    {
        get => (DashboardNavigation?)GetValue(NavigationProperty);
        set => SetValue(NavigationProperty, value);
    }

    private static void OnNavigationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (NavigationBar)d;
        if (e.OldValue is DashboardNavigation old) old.History.Changed -= bar.OnHistoryChanged;
        if (e.NewValue is DashboardNavigation navigation) navigation.History.Changed += bar.OnHistoryChanged;
        bar.Build();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => Build();

    private void OnCultureChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Build);

    private void Build()
    {
        var path = Navigation?.History.Path ?? [];
        Visibility = path.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (path.Count > 1)
        {
            var back = new Button { Content = "← " + Strings.Back, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 12, 0) };
            back.ToolTip = string.Format(EasyBILocalization.FormatCulture, Strings.BackTooltip, path[^2].Caption);
            back.Click += (_, _) => Navigation?.GoBack();
            panel.Children.Add(back);
            for (var i = 0; i < path.Count; i++)
            {
                if (i > 0) panel.Children.Add(Muted(" › "));
                if (i == path.Count - 1)
                {
                    panel.Children.Add(new TextBlock { Text = path[i].Caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                    continue;
                }
                var index = i;
                var step = new Button { Content = path[i].Caption, Padding = new Thickness(6, 1, 6, 1) };
                step.Click += (_, _) => Navigation?.GoBack(index);
                panel.Children.Add(step);
            }
        }
        Content = panel;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);
        return block;
    }
}
