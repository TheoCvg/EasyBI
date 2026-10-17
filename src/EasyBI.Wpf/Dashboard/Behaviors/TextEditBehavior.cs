using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Edits text items in place: double-click a text (or use <see cref="DashboardCommands.EditItem"/>) to type over it.
/// Ctrl+Enter or leaving the editor keeps the text, Escape cancels.
/// </summary>
public sealed class TextEditBehavior : DashboardBehavior
{
    private readonly TextBox _editor = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BorderThickness = new Thickness(0),
    }
        .Themed(Control.BackgroundProperty, EasyBITheme.SurfaceAltBrushKey)
        .Themed(Control.ForegroundProperty, EasyBITheme.ForegroundBrushKey);

    private DashboardItem? _item;

    protected override void OnAttach()
    {
        _editor.PreviewKeyDown += OnKeyDown;
        _editor.LostKeyboardFocus += (_, _) => End(commit: true);
        AddToOverlay(_editor, Place);
        View.TextEditRequested += OnTextEditRequested;
        View.PreviewMouseLeftButtonDown += OnMouseDown;
        View.PreviewMouseDown += OnAnyMouseDown;
    }

    protected override void OnDetach()
    {
        View.TextEditRequested -= OnTextEditRequested;
        View.PreviewMouseLeftButtonDown -= OnMouseDown;
        View.PreviewMouseDown -= OnAnyMouseDown;
        _item = null;
    }

    /// <summary>Starts editing the text of an item.</summary>
    public void Begin(DashboardItem item)
    {
        if (!CanEdit || item.Visual is not { Mark: MarkType.Text } visual) return;
        End(commit: true);
        var text = visual.Text ?? new TextContent();
        _item = item;
        _editor.Text = text.Text;
        _editor.FontSize = text.FontSize;
        _editor.FontWeight = text.IsBold ? FontWeights.Bold : FontWeights.Normal;
        _editor.TextAlignment = text.Alignment switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
        Overlay.Refresh();
        // The editor is placed on the next layout pass: focus it afterwards
        Dispatcher.BeginInvoke(() =>
        {
            _editor.Focus();
            _editor.CaretIndex = _editor.Text.Length;
        }, DispatcherPriority.Input);
    }

    private Rect? Place(DashboardOverlay overlay, Size size)
    {
        if (_item is not { } item || item.Parent != Panel) return null;
        var bounds = Panel.GetItemBounds(item);
        var padding = item.Padding;
        return new Rect(
            bounds.Left + padding.Left,
            bounds.Top + padding.Top,
            Math.Max(0, bounds.Width - padding.Left - padding.Right),
            Math.Max(0, bounds.Height - padding.Top - padding.Bottom));
    }

    private void OnTextEditRequested(object? sender, TextEditRequestedEventArgs e)
    {
        Begin(e.Item);
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !CanEdit || DashboardView.ItemOf(e.OriginalSource as DependencyObject) is not { Kind: DashboardItemKind.Text } item) return;
        Begin(item);
        e.Handled = true;
    }

    // Clicking a chart does not take the keyboard focus: a click outside the editor ends the edit too
    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_item is not null && !_editor.IsMouseOver) End(commit: true);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            End(commit: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            End(commit: true);
            e.Handled = true;
        }
    }

    private void End(bool commit)
    {
        if (_item is not { } item) return;
        _item = null;
        Overlay.Refresh();
        if (commit && item.Visual is { Mark: MarkType.Text } visual && (visual.Text?.Text ?? "") != _editor.Text)
            View.SetVisual(item, visual with { Text = (visual.Text ?? new TextContent()) with { Text = _editor.Text } });
        // Give the keyboard back to the view so that its shortcuts keep working
        if (_editor.IsKeyboardFocused) View.Focus();
    }
}
