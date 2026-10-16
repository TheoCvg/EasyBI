using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Raised when the user asks to fill or edit a slot; set <see cref="Handled"/> to replace the built-in editor.</summary>
public sealed class EditVisualRequestedEventArgs(string slotId, VisualDefinition? visual, Action<VisualDefinition> complete) : EventArgs
{
    public string SlotId { get; } = slotId;

    /// <summary>The visual being edited, or null for an empty slot.</summary>
    public VisualDefinition? Visual { get; } = visual;

    /// <summary>True when the application handles the edit: the built-in designer window is not opened.</summary>
    public bool Handled { get; set; }

    /// <summary>Places the edited visual in the slot. Can be called later, once the application's editor closes.</summary>
    public void Complete(VisualDefinition visual) => complete(visual);
}

/// <summary>
/// Dashboard made of rows of visuals that the user arranges: "+" buttons appear near the edges of a slot
/// to add a slot or a row, slots move by their drag grip and resize by dragging the gaps, each row
/// fits, scrolls or wraps. The arrangement is part of <see cref="Report"/> (<see cref="ReportDefinition.Layout"/>),
/// so saving and restoring the report saves and restores the dashboard.
/// </summary>
public partial class DashboardPanel : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(DashboardPanel), new PropertyMetadata(null));

    /// <summary>The report shown and edited by the panel (two-way). Setting it loads its visuals and layout.</summary>
    public static readonly DependencyProperty ReportProperty = DependencyProperty.Register(
        nameof(Report), typeof(ReportDefinition), typeof(DashboardPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((DashboardPanel)d).OnReportChanged((ReportDefinition?)e.NewValue)));

    /// <summary>False shows the dashboard read-only: no grip, no "+", no splitter, no edit buttons.</summary>
    public static readonly DependencyProperty IsLayoutEditableProperty = DependencyProperty.Register(
        nameof(IsLayoutEditable), typeof(bool), typeof(DashboardPanel), new PropertyMetadata(true));

    private readonly DashboardViewModel _viewModel = new();
    private bool _pushing;
    private Point? _dragStart;
    private DashboardSlotViewModel? _hinted;
    private Dictionary<DashboardSlotViewModel, double>? _resizeStart;
    private double _resizeDelta;
    private double _rowResizeStart;

    public DashboardPanel()
    {
        InitializeComponent();
        RowList.ItemsSource = _viewModel.Rows;
        // Keep the keyboard focus after a change, even when the clicked button disappeared, so Ctrl+Z works
        Focusable = true;
        FocusVisualStyle = null;
        _viewModel.ReportChanged += (_, _) =>
        {
            PushReport();
            // The removed button only loses the focus once its container is unloaded: check afterwards
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsKeyboardFocusWithin) Focus();
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
        PushReport();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = CanRedo));
    }

    /// <summary>True when a layout or visual change can be undone (Ctrl+Z).</summary>
    public bool CanUndo => IsLayoutEditable && _viewModel.CanUndo;

    public bool CanRedo => IsLayoutEditable && _viewModel.CanRedo;

    /// <summary>Undoes the last change made through the dashboard (also Ctrl+Z).</summary>
    public void Undo()
    {
        if (CanUndo) _viewModel.Undo();
    }

    /// <summary>Redoes the last undone change (also Ctrl+Y).</summary>
    public void Redo()
    {
        if (CanRedo) _viewModel.Redo();
    }

    /// <summary>Raised before a slot is filled or edited.</summary>
    public event EventHandler<EditVisualRequestedEventArgs>? EditVisualRequested;

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public ReportDefinition? Report
    {
        get => (ReportDefinition?)GetValue(ReportProperty);
        set => SetValue(ReportProperty, value);
    }

    public bool IsLayoutEditable
    {
        get => (bool)GetValue(IsLayoutEditableProperty);
        set => SetValue(IsLayoutEditableProperty, value);
    }

    private void OnReportChanged(ReportDefinition? report)
    {
        if (_pushing) return;
        _viewModel.Load(report ?? new ReportDefinition());
        // Hand the normalized report (with its layout) back to the binding source
        PushReport();
    }

    private void PushReport()
    {
        _pushing = true;
        try
        {
            SetCurrentValue(ReportProperty, _viewModel.Report);
        }
        finally
        {
            _pushing = false;
        }
    }

    private static T Item<T>(object sender) => (T)((FrameworkElement)sender).DataContext;

    private int IndexOf(DashboardSlotViewModel slot) => slot.Row.Slots.IndexOf(slot);

    private int IndexOf(DashboardRowViewModel row) => _viewModel.Rows.IndexOf(row);

    // --- Adding ---

    private void OnInsertSlotBefore(object sender, RoutedEventArgs e)
    {
        var slot = Item<DashboardSlotViewModel>(sender);
        _viewModel.Apply(r => ReportLayout.InsertSlot(r, slot.Row.Id, IndexOf(slot)));
    }

    private void OnInsertSlotAfter(object sender, RoutedEventArgs e)
    {
        var slot = Item<DashboardSlotViewModel>(sender);
        _viewModel.Apply(r => ReportLayout.InsertSlot(r, slot.Row.Id, IndexOf(slot) + 1));
    }

    private void OnInsertRowBefore(object sender, RoutedEventArgs e)
    {
        var row = Item<DashboardRowViewModel>(sender);
        _viewModel.Apply(r => ReportLayout.InsertRow(r, IndexOf(row)));
    }

    private void OnInsertRowAfter(object sender, RoutedEventArgs e)
    {
        var row = Item<DashboardRowViewModel>(sender);
        _viewModel.Apply(r => ReportLayout.InsertRow(r, IndexOf(row) + 1));
    }

    // --- Editing and removing ---

    private void OnEditSlot(object sender, RoutedEventArgs e)
    {
        var slot = Item<DashboardSlotViewModel>(sender);
        var slotId = slot.Id;
        var args = new EditVisualRequestedEventArgs(slotId, slot.Visual,
            visual => _viewModel.Apply(r => ReportLayout.SetVisual(r, slotId, visual)));
        EditVisualRequested?.Invoke(this, args);
        if (args.Handled || Context is not { } context) return;

        var editor = new VisualEditorWindow(context, slot.Visual) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Visual is { } visual)
            args.Complete(visual);
    }

    private void OnRemoveSlot(object sender, RoutedEventArgs e)
    {
        var slot = Item<DashboardSlotViewModel>(sender);
        ClearSelectionOf([slot]);
        _viewModel.Apply(r => ReportLayout.RemoveSlot(r, slot.Id));
    }

    private void OnRowOptions(object sender, RoutedEventArgs e)
    {
        var row = Item<DashboardRowViewModel>(sender);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        foreach (var (overflow, label) in new[]
                 {
                     (RowOverflow.Fit, Strings.RowFit),
                     (RowOverflow.Scroll, Strings.RowScroll),
                     (RowOverflow.Wrap, Strings.RowWrap),
                 })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = row.Overflow == overflow };
            item.Click += (_, _) => _viewModel.Apply(r => ReportLayout.SetRowOverflow(r, row.Id, overflow));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = Strings.RemoveRow };
        remove.Click += (_, _) =>
        {
            ClearSelectionOf(row.Slots);
            _viewModel.Apply(r => ReportLayout.RemoveRow(r, row.Id));
        };
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    /// <summary>A removed visual can no longer filter the others.</summary>
    private void ClearSelectionOf(IEnumerable<DashboardSlotViewModel> slots)
    {
        if (VisualView.GetFilterContext(this) is { Selection: { } selection } filters
            && slots.Any(s => s.Visual?.Id == selection.SourceVisualId))
            filters.ClearSelection();
    }

    // --- Drag and drop ---

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        // The grip is small: keep receiving moves even when the pointer leaves it before the drag starts
        ((UIElement)sender).CaptureMouse();
    }

    private void OnGripMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void OnGripMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start) return;
        var delta = start - e.GetPosition(this);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        ((UIElement)sender).ReleaseMouseCapture();
        var slot = Item<DashboardSlotViewModel>(sender);
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(DashboardSlotViewModel), slot), DragDropEffects.Move);
        SetHint(null, DropHint.None);
    }

    private void OnSlotDragOver(object sender, DragEventArgs e)
    {
        var hint = HintAt(sender, e);
        e.Effects = hint == DropHint.None ? DragDropEffects.None : DragDropEffects.Move;
        SetHint(Item<DashboardSlotViewModel>(sender), hint);
        e.Handled = true;
    }

    private void OnSlotDragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(_hinted, Item<DashboardSlotViewModel>(sender))) SetHint(null, DropHint.None);
    }

    private void OnSlotDrop(object sender, DragEventArgs e)
    {
        var hint = HintAt(sender, e);
        SetHint(null, DropHint.None);
        if (hint == DropHint.None || e.Data.GetData(typeof(DashboardSlotViewModel)) is not DashboardSlotViewModel moved) return;

        var target = Item<DashboardSlotViewModel>(sender);
        var rowIndex = IndexOf(target.Row);
        _viewModel.Apply(hint switch
        {
            DropHint.Left => r => ReportLayout.MoveSlot(r, moved.Id, target.Row.Id, IndexOf(target)),
            DropHint.Right => r => ReportLayout.MoveSlot(r, moved.Id, target.Row.Id, IndexOf(target) + 1),
            DropHint.Top => r => ReportLayout.MoveSlotToNewRow(r, moved.Id, rowIndex),
            _ => r => ReportLayout.MoveSlotToNewRow(r, moved.Id, rowIndex + 1),
        });
        e.Handled = true;
    }

    /// <summary>Top and bottom quarters create a new row; the left and right halves insert into the row.</summary>
    private DropHint HintAt(object sender, DragEventArgs e)
    {
        if (!IsLayoutEditable || !e.Data.GetDataPresent(typeof(DashboardSlotViewModel))) return DropHint.None;
        var element = (FrameworkElement)sender;
        var position = e.GetPosition(element);
        if (position.Y < element.ActualHeight * 0.25) return DropHint.Top;
        if (position.Y > element.ActualHeight * 0.75) return DropHint.Bottom;
        return position.X < element.ActualWidth / 2 ? DropHint.Left : DropHint.Right;
    }

    private void SetHint(DashboardSlotViewModel? slot, DropHint hint)
    {
        if (_hinted is not null && !ReferenceEquals(_hinted, slot)) _hinted.DropHint = DropHint.None;
        _hinted = slot;
        if (slot is not null) slot.DropHint = hint;
    }

    // --- Resizing ---

    private void OnSlotSizeChanged(object sender, SizeChangedEventArgs e) =>
        Item<DashboardSlotViewModel>(sender).ActualWidth = e.NewSize.Width;

    private void OnSlotResizeStarted(object sender, DragStartedEventArgs e)
    {
        var row = Item<DashboardSlotViewModel>(sender).Row;
        _resizeStart = row.Slots.ToDictionary(s => s, s => s.ActualWidth > 0 ? s.ActualWidth : s.Width);
        _resizeDelta = 0;
    }

    private void OnSlotResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeStart is null) return;
        var slot = Item<DashboardSlotViewModel>(sender);
        _resizeDelta += e.HorizontalChange;
        var start = _resizeStart[slot];
        if (slot.Row.Overflow != RowOverflow.Fit)
        {
            slot.Width = Math.Max(ReportLayout.MinWidth, start + _resizeDelta);
            return;
        }

        // Fit: the gap moves between this slot and the next one; the others keep their current share
        var next = slot.Row.Slots[IndexOf(slot) + 1];
        var pair = start + _resizeStart[next];
        var width = Math.Clamp(start + _resizeDelta, ReportLayout.MinWidth, pair - ReportLayout.MinWidth);
        foreach (var (other, otherStart) in _resizeStart) other.Width = otherStart;
        slot.Width = width;
        next.Width = pair - width;
    }

    private void OnSlotResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizeStart is null) return;
        var row = Item<DashboardSlotViewModel>(sender).Row;
        var widths = row.Slots.ToDictionary(s => s.Id, s => s.Width);
        _resizeStart = null;
        _viewModel.Apply(r => ReportLayout.SetSlotWidths(r, row.Id, widths));
    }

    private void OnRowResizeStarted(object sender, DragStartedEventArgs e)
    {
        _rowResizeStart = Item<DashboardRowViewModel>(sender).Height;
        _resizeDelta = 0;
    }

    private void OnRowResizeDelta(object sender, DragDeltaEventArgs e)
    {
        _resizeDelta += e.VerticalChange;
        Item<DashboardRowViewModel>(sender).Height = Math.Max(ReportLayout.MinHeight, _rowResizeStart + _resizeDelta);
    }

    private void OnRowResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        var row = Item<DashboardRowViewModel>(sender);
        _viewModel.Apply(r => ReportLayout.SetRowHeight(r, row.Id, row.Height));
    }

    /// <summary>
    /// A horizontally scrolling row must not swallow the vertical wheel of the dashboard:
    /// the wheel scrolls the dashboard, Shift + wheel scrolls the row.
    /// </summary>
    private void OnRowMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var slots = (ItemsControl)sender;
        if (Item<DashboardRowViewModel>(sender).Overflow != RowOverflow.Scroll
            || VisualTreeHelper.GetChildrenCount(slots) == 0
            || VisualTreeHelper.GetChild(slots, 0) is not ScrollViewer row) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            row.ScrollToHorizontalOffset(row.HorizontalOffset - e.Delta);
        else
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
