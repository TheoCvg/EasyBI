using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;
using Microsoft.Win32;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Raised when the user asks to fill or edit a visual; set <see cref="Handled"/> to replace the built-in editor.</summary>
public sealed class EditVisualRequestedEventArgs(string itemId, VisualDefinition? visual, Action<VisualDefinition> complete) : EventArgs
{
    public string ItemId { get; } = itemId;

    /// <summary>The visual being edited, or null for a new one.</summary>
    public VisualDefinition? Visual { get; } = visual;

    /// <summary>True when the application handles the edit: the built-in designer window is not opened.</summary>
    public bool Handled { get; set; }

    /// <summary>Shows the edited visual in the item. Can be called later, once the application's editor closes.</summary>
    public void Complete(VisualDefinition visual) => complete(visual);
}

/// <summary>
/// Dashboard laid out on a grid that end users arrange: items show a visual, a text or an image and can cover
/// several rows and columns. Items move with their grip (the others make room live), resize with the corner
/// handle, and "+" buttons on their right and bottom edges add new items. The arrangement is part of
/// <see cref="Report"/> (<see cref="ReportDefinition.Layout"/>), so saving and restoring the report saves and
/// restores the dashboard.
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

    /// <summary>False shows the dashboard read-only: no grip, no "+", no resize handle, no toolbar.</summary>
    public static readonly DependencyProperty IsLayoutEditableProperty = DependencyProperty.Register(
        nameof(IsLayoutEditable), typeof(bool), typeof(DashboardPanel),
        new PropertyMetadata(true, (d, e) => ((DashboardPanel)d).AddAtEnd.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

    private const double MinFontSize = 8;
    private const double MaxFontSize = 96;

    private readonly DashboardViewModel _viewModel = new();
    private DashboardGridPanel? _grid;
    private bool _pushing;

    // Dragging
    private Point? _dragStart;
    private Point _grabOffset;
    private DashboardItemViewModel? _dragged;
    private (int Column, int Row)? _dropCell;

    // Resizing
    private (int ColumnSpan, int RowSpan) _resizeStart;
    private (int ColumnSpan, int RowSpan)? _resizeTarget;
    private Vector _resizeDelta;

    public DashboardPanel()
    {
        InitializeComponent();
        ItemList.DataContext = _viewModel;
        ItemList.ItemsSource = _viewModel.Items;
        // Keep the keyboard focus after a change, even when the clicked button disappeared, so Ctrl+Z works
        Focusable = true;
        FocusVisualStyle = null;
        _viewModel.ReportChanged += (_, _) =>
        {
            PushReport();
            // A removed button only loses the focus once its container is unloaded: check afterwards
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsKeyboardFocusWithin) Focus();
            }, DispatcherPriority.Input);
        };
        PushReport();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = CanUndo));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = CanRedo));
    }

    /// <summary>Raised before a visual is created or edited.</summary>
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

    /// <summary>Largest image that can be embedded in the report, in bytes (2 MB by default).</summary>
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>True when a change can be undone (Ctrl+Z).</summary>
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

    private void OnGridLoaded(object sender, RoutedEventArgs e) => _grid = (DashboardGridPanel)sender;

    private static DashboardItemViewModel Item(object sender) => (DashboardItemViewModel)((FrameworkElement)sender).DataContext;

    // --- Adding ---

    private void OnAddRight(object sender, RoutedEventArgs e) =>
        _viewModel.Apply(r => ReportLayout.AddItemNextTo(r, Item(sender).Id, ItemSide.Right));

    private void OnAddBelow(object sender, RoutedEventArgs e) =>
        _viewModel.Apply(r => ReportLayout.AddItemNextTo(r, Item(sender).Id, ItemSide.Below));

    private void OnAddAtEnd(object sender, RoutedEventArgs e) => _viewModel.Apply(r => ReportLayout.AddItemAtEnd(r));

    private void OnAddVisual(object sender, RoutedEventArgs e) => EditVisual(Item(sender));

    private void OnAddText(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        _viewModel.Apply(r => ReportLayout.SetText(r, item.Id, new TextContent()));
        BeginTextEdit(item);
    }

    private void OnAddImage(object sender, RoutedEventArgs e) => PickImage(Item(sender), ImageFit.Contain);

    // --- Editing and removing ---

    private void OnEditItem(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        switch (item.Kind)
        {
            case DashboardItemKind.Text: BeginTextEdit(item); break;
            case DashboardItemKind.Image: PickImage(item, item.Image!.Fit); break;
            default: EditVisual(item); break;
        }
    }

    private void EditVisual(DashboardItemViewModel item)
    {
        var itemId = item.Id;
        var args = new EditVisualRequestedEventArgs(itemId, item.Visual,
            visual => _viewModel.Apply(r => ReportLayout.SetVisual(r, itemId, visual)));
        EditVisualRequested?.Invoke(this, args);
        if (args.Handled || Context is not { } context) return;

        var editor = new VisualEditorWindow(context, item.Visual) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Visual is { } visual)
            args.Complete(visual);
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        if (VisualView.GetFilterContext(this) is { Selection: { } selection } filters && item.Visual?.Id == selection.SourceVisualId)
            filters.ClearSelection(); // a removed visual can no longer filter the others
        _viewModel.Apply(r => ReportLayout.RemoveItem(r, item.Id));
    }

    // --- Text ---

    private void OnTextMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && IsLayoutEditable) BeginTextEdit(Item(sender));
    }

    private void BeginTextEdit(DashboardItemViewModel item)
    {
        item.EditText = item.Text?.Text ?? "";
        item.IsEditingText = true;
    }

    private void OnTextEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        var editor = (TextBox)sender;
        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
    {
        var item = Item(sender);
        if (e.Key == Key.Escape)
        {
            item.IsEditingText = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CommitText(item);
            e.Handled = true;
        }
    }

    private void OnTextEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitText(Item(sender));

    private void CommitText(DashboardItemViewModel item)
    {
        if (!item.IsEditingText) return;
        item.IsEditingText = false;
        var text = item.Text ?? new TextContent();
        if (text.Text != item.EditText)
            _viewModel.Apply(r => ReportLayout.SetText(r, item.Id, text with { Text = item.EditText }));
    }

    private void OnTextSmaller(object sender, RoutedEventArgs e) =>
        ChangeText(Item(sender), t => t with { FontSize = Math.Max(MinFontSize, t.FontSize - 2) });

    private void OnTextLarger(object sender, RoutedEventArgs e) =>
        ChangeText(Item(sender), t => t with { FontSize = Math.Min(MaxFontSize, t.FontSize + 2) });

    private void OnTextBold(object sender, RoutedEventArgs e) => ChangeText(Item(sender), t => t with { IsBold = !t.IsBold });

    private void OnTextAlignment(object sender, RoutedEventArgs e) =>
        ChangeText(Item(sender), t => t with { Alignment = (TextAlign)(((int)t.Alignment + 1) % 3) });

    private void ChangeText(DashboardItemViewModel item, Func<TextContent, TextContent> change)
    {
        if (item.Text is { } text) _viewModel.Apply(r => ReportLayout.SetText(r, item.Id, change(text)));
    }

    // --- Image ---

    private void PickImage(DashboardItemViewModel item, ImageFit fit)
    {
        var dialog = new OpenFileDialog { Filter = Strings.ImageFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var bytes = File.ReadAllBytes(dialog.FileName);
        string? problem = null;
        if (bytes.Length > MaxImageBytes)
            problem = Strings.Format(Strings.ImageTooLarge, MaxImageBytes / (1024 * 1024));
        var image = new ImageContent { Data = Convert.ToBase64String(bytes), MediaType = MediaType(dialog.FileName), Fit = fit };
        if (problem is null && DashboardItemViewModel.Decode(image) is null)
            problem = Strings.ImageUnreadable;
        if (problem is not null)
        {
            MessageBox.Show(Window.GetWindow(this), problem, Strings.ChangeImage, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _viewModel.Apply(r => ReportLayout.SetImage(r, item.Id, image));
    }

    private void OnImageFit(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        if (item.Image is { } image)
            _viewModel.Apply(r => ReportLayout.SetImage(r, item.Id, image with { Fit = (ImageFit)(((int)image.Fit + 1) % 3) }));
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/png",
    };

    // --- Moving: the item follows the pointer cell by cell and the others make room live ---

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        // Where the item was grabbed, so that it does not jump under the pointer
        _grabOffset = e.GetPosition(ItemContainer(sender));
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

        _dragged = Item(sender);
        _dropCell = null;
        var result = DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(DashboardItemViewModel), _dragged), DragDropEffects.Move);
        if (result == DragDropEffects.None) _viewModel.EndPreview();
        _dragged = null;
    }

    private UIElement ItemContainer(object sender) => (UIElement)ItemList.ItemContainerGenerator.ContainerFromItem(Item(sender));

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!IsLayoutEditable || _grid is null || _dragged is not { } item
            || !e.Data.GetDataPresent(typeof(DashboardItemViewModel)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        var position = e.GetPosition(_grid);
        var column = (int)Math.Round((position.X - _grabOffset.X) / _grid.ColumnPitch(_grid.ActualWidth));
        var row = (int)Math.Round((position.Y - _grabOffset.Y) / _grid.RowPitch);
        var cell = (Math.Clamp(column, 0, Math.Max(0, _viewModel.Columns - item.ColumnSpan)), Math.Max(0, row));
        if (cell == _dropCell) return;
        _dropCell = cell;
        _viewModel.Preview(r => ReportLayout.MoveItem(r, item.Id, cell.Item1, cell.Item2));
    }

    private void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_dragged is not { } item || _dropCell is not { } cell) return;
        _viewModel.Apply(r => ReportLayout.MoveItem(r, item.Id, cell.Column, cell.Row));
    }

    // --- Resizing: the corner handle snaps to whole cells ---

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        var item = Item(sender);
        _resizeStart = (item.ColumnSpan, item.RowSpan);
        _resizeTarget = null;
        _resizeDelta = default;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_grid is null) return;
        var item = Item(sender);
        _resizeDelta += new Vector(e.HorizontalChange, e.VerticalChange);
        var columnSpan = Math.Max(1, _resizeStart.ColumnSpan + (int)Math.Round(_resizeDelta.X / _grid.ColumnPitch(_grid.ActualWidth)));
        var rowSpan = Math.Max(1, _resizeStart.RowSpan + (int)Math.Round(_resizeDelta.Y / _grid.RowPitch));
        if ((columnSpan, rowSpan) == (_resizeTarget ?? _resizeStart)) return;
        _resizeTarget = (columnSpan, rowSpan);
        _viewModel.Preview(r => ReportLayout.ResizeItem(r, item.Id, columnSpan, rowSpan));
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        var item = Item(sender);
        if (_resizeTarget is { } target && target != _resizeStart)
            _viewModel.Apply(r => ReportLayout.ResizeItem(r, item.Id, target.ColumnSpan, target.RowSpan));
        else
            _viewModel.EndPreview();
        _resizeTarget = null;
    }
}
