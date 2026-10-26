using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>What happened to a failed visual: what failed, the exception's message, and the details to copy for a bug report.</summary>
internal sealed class VisualFailureWindow : Window
{
    public VisualFailureWindow(VisualFailedEventArgs failure)
    {
        Title = Strings.VisualFailureTitle;
        Width = 720;
        Height = 480;
        MinWidth = 400;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, EasyBITheme.BackgroundBrushKey);
        SetResourceReference(ForegroundProperty, EasyBITheme.ForegroundBrushKey);

        Summary = failure.Stage == VisualFailureStage.Query
            ? Strings.VisualFailureQuery
            : Strings.Format(Strings.VisualFailureRender, failure.Renderer);
        Details = failure.Details;

        var copy = new Button { Content = Strings.VisualFailureCopy, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4) };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(Details);
                copy.Content = Strings.VisualFailureCopied;
            }
            catch (COMException)
            {
                // The clipboard is held by another application: the text can still be selected and copied by hand
            }
        };
        var close = new Button { Content = Strings.Close, IsCancel = true, IsDefault = true, MinWidth = 90, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        var footer = new Border { Child = buttons, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 8, 12, 8) }
            .Themed(Border.BorderBrushProperty, EasyBITheme.BorderBrushKey);
        DockPanel.SetDock(footer, Dock.Bottom);

        var header = new StackPanel { Margin = new Thickness(16, 16, 16, 8) };
        header.Children.Add(new TextBlock { Text = Summary, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = failure.Exception.Message, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 120 }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.ErrorBrushKey));
        header.Children.Add(new TextBlock { Text = Strings.VisualFailureTechnicalDetails, Margin = new Thickness(0, 12, 0, 0) }
            .Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey));
        DockPanel.SetDock(header, Dock.Top);

        var details = new TextBox
        {
            Text = Details,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(16, 0, 16, 16),
        };

        var root = new DockPanel();
        root.Children.Add(footer);
        root.Children.Add(header);
        root.Children.Add(details);
        Content = root;
    }

    /// <summary>What failed, in the end user's words.</summary>
    public string Summary { get; }

    /// <summary>The text shown and copied (<see cref="VisualFailedEventArgs.Details"/>).</summary>
    public string Details { get; }
}
