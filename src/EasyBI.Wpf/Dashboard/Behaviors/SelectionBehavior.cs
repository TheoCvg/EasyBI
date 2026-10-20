using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Selects items (<see cref="DashboardView.SelectedItems"/>) with the mouse and the keyboard: a click selects an item
/// (Ctrl+click adds or removes it) without stopping the click, so that charts still cross-filter; a click on the
/// background unselects everything and a drag from it draws a selection box (Ctrl adds to the selection). Arrows go to
/// the nearest item in their direction (Shift adds it), Escape unselects. Delete, Ctrl+D and Ctrl+A are commands of the
/// view (<see cref="ApplicationCommands.Delete"/>, <see cref="DashboardCommands.Duplicate"/>, <see cref="ApplicationCommands.SelectAll"/>).
/// </summary>
public class SelectionBehavior : DashboardBehavior
{
    private readonly ContentControl _box = new() { IsHitTestVisible = false, Focusable = false };
    private DragGesture? _gesture;
    private Rect? _boxBounds;
    private IReadOnlyList<DashboardItem> _before = [];
    private bool _adding;

    protected override void OnAttach()
    {
        _box.SetResourceReference(FrameworkElement.StyleProperty, DashboardOverlay.SelectionBoxStyleKey);
        AddToOverlay(_box, (_, _) => _boxBounds);
        _gesture = new DragGesture(View, View, CanDrawBox, OnBoxStarted, OnBoxMoved, OnBoxEnded);
        View.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        View.PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnDetach()
    {
        _gesture?.Cancel();
        View.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
        View.PreviewKeyDown -= OnPreviewKeyDown;
    }

    private static bool Ctrl => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanEdit) return;
        if (DashboardView.ItemOf(e.OriginalSource as DependencyObject) is { } item && item.Parent == Panel)
        {
            View.Select(item, Ctrl ? DashboardSelectionMode.Toggle : DashboardSelectionMode.Replace);
            // The keys act on the selection; a control of the item (a text box) keeps its focus
            if (!View.IsKeyboardFocusWithin) View.Focus();
        }
        else if (IsBackground(e.OriginalSource))
        {
            if (!Ctrl) View.UnselectAll();
            if (!View.IsKeyboardFocusWithin) View.Focus();
        }
    }

    // The dashboard's empty room: the element under the pointer is the panel or one of its containers (the view's
    // background, its scroll viewer), not an item, a control of the overlay or a slot's content
    private bool IsBackground(object? source) => source is Visual element && (element == Panel || Panel.IsDescendantOf(element));

    private bool CanDrawBox() => CanEdit && IsBackground(Mouse.DirectlyOver);

    private void OnBoxStarted(Point start)
    {
        _adding = Ctrl;
        _before = View.SelectedItems.ToArray();
        _boxBounds = new Rect(start, start);
        Overlay.Refresh();
    }

    private void OnBoxMoved(Point position, Vector delta)
    {
        var box = new Rect(position - delta, position);
        _boxBounds = box;
        var touched = View.Items.Where(i => Panel.GetItemBounds(i) is { IsEmpty: false } bounds && bounds.IntersectsWith(box));
        View.Select(_adding ? [.. _before, .. touched] : touched);
        Overlay.Refresh();
    }

    private void OnBoxEnded(bool commit)
    {
        if (!commit) View.Select(_before);
        _boxBounds = null;
        Overlay.Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Typing in a text box (in-place text editing, a slot's content) is not for the selection
        if (!CanEdit || e.OriginalSource is TextBoxBase || View.SelectedItems.Count == 0) return;
        switch (e.Key)
        {
            case Key.Escape:
                View.UnselectAll();
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                if (Nearest(View.SelectedItems[^1], e.Key) is { } next)
                {
                    View.Select(next, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? DashboardSelectionMode.Add : DashboardSelectionMode.Replace);
                    next.BringIntoView();
                }
                // Taken even at the edge of the dashboard: WPF would otherwise move the focus to another control
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The next item in the direction of an arrow key, if any: among the items entirely on that side, those facing
    /// <paramref name="from"/> (overlapping it across the direction) first, then the nearest edge to edge, then the best aligned.
    /// </summary>
    protected DashboardItem? Nearest(DashboardItem from, Key direction)
    {
        var origin = Panel.GetItemBounds(from);
        var horizontal = direction is Key.Left or Key.Right;
        return View.Items
            .Where(item => item != from)
            .Select(item => (Item: item, Bounds: Panel.GetItemBounds(item)))
            .Where(c => !c.Bounds.IsEmpty)
            .Select(c => (c.Item, Gap: Gap(origin, c.Bounds, direction), Facing: Overlap(origin, c.Bounds, horizontal) > 1,
                Offset: horizontal ? Math.Abs(Center(c.Bounds).Y - Center(origin).Y) : Math.Abs(Center(c.Bounds).X - Center(origin).X)))
            // Entirely on that side (a pixel of tolerance for rounding)
            .Where(c => c.Gap > -1)
            .OrderBy(c => c.Facing ? 0 : 1).ThenBy(c => Math.Round(c.Gap)).ThenBy(c => Math.Round(c.Offset))
            .Select(c => c.Item)
            .FirstOrDefault();
    }

    // Space between the two items along the direction, negative when the candidate is not entirely on that side
    private static double Gap(Rect from, Rect to, Key direction) => direction switch
    {
        Key.Left => from.Left - to.Right,
        Key.Right => to.Left - from.Right,
        Key.Up => from.Top - to.Bottom,
        _ => to.Top - from.Bottom,
    };

    // How much the two items face each other across the direction
    private static double Overlap(Rect a, Rect b, bool horizontal) => horizontal
        ? Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top)
        : Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);

    private static Point Center(Rect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
}
