using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
/// Raised when the user adds an item next to a full-row item, which requires giving up the full row.
/// Set <see cref="Handled"/> to ask the user yourself, then call <see cref="Proceed"/> if they agree;
/// otherwise the panel shows a banner on the item.
/// </summary>
public sealed class FullRowSplitRequestedEventArgs(string itemId, Action proceed) : EventArgs
{
    public string ItemId { get; } = itemId;

    public bool Handled { get; set; }

    /// <summary>Removes the full-row setting and adds the new item next to it.</summary>
    public void Proceed() => proceed();
}

/// <summary>
/// Dashboard made of bands that end users arrange. A <see cref="BandMode.Fit"/> band is a 12-column grid where
/// items can span several rows and columns; a <see cref="BandMode.Scroll"/> or <see cref="BandMode.Wrap"/> band
/// lines items of a fixed width up, scrolling or wrapping. Items show a visual, a text or an image, move with
/// their grip (the others make room live, across bands too), resize with the corner handle, and can take the
/// full row. The arrangement is part of <see cref="Report"/> (<see cref="ReportDefinition.Layout"/>), so saving
/// and restoring the report saves and restores the dashboard.
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
    private readonly Dictionary<DashboardBandViewModel, DashboardGridPanel> _grids = [];
    private bool _pushing;

    // Dragging
    private Point? _dragStart;
    private Point _grabOffset;
    private DashboardItemViewModel? _dragged;
    private Func<ReportDefinition, ReportDefinition>? _drop;
    private string? _dropKey;

    // Resizing
    private (int ColumnSpan, int RowSpan, double Width) _resizeStart;
    private string? _resizeKey;
    private Func<ReportDefinition, ReportDefinition>? _resize;
    private Vector _resizeDelta;

    public DashboardPanel()
    {
        InitializeComponent();
        BandList.ItemsSource = _viewModel.Bands;
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

    /// <summary>Raised when "+" is used next to a full-row item.</summary>
    public event EventHandler<FullRowSplitRequestedEventArgs>? FullRowSplitRequested;

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

    private void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        var grid = (DashboardGridPanel)sender;
        if (grid.DataContext is DashboardBandViewModel band) _grids[band] = grid;
    }

    private static DashboardItemViewModel Item(object sender) => (DashboardItemViewModel)((FrameworkElement)sender).DataContext;

    private static DashboardBandViewModel Band(object sender) => (DashboardBandViewModel)((FrameworkElement)sender).DataContext;

    // --- Adding ---

    private void OnAddRight(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        var itemId = item.Id;
        if (!item.IsFullRow)
        {
            _viewModel.Apply(r => ReportLayout.AddItemNextTo(r, itemId, ItemSide.Right));
            return;
        }

        // Adding next to a full-row item gives up the full row: the application or a banner asks first
        var args = new FullRowSplitRequestedEventArgs(itemId, () => _viewModel.Apply(r => ReportLayout.SplitFullRow(r, itemId)));
        FullRowSplitRequested?.Invoke(this, args);
        if (!args.Handled) item.ShowFullRowWarning = true;
    }

    private void OnFullRowProceed(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        item.ShowFullRowWarning = false;
        _viewModel.Apply(r => ReportLayout.SplitFullRow(r, item.Id));
    }

    private void OnFullRowCancel(object sender, RoutedEventArgs e) => Item(sender).ShowFullRowWarning = false;

    private void OnAddBelow(object sender, RoutedEventArgs e) =>
        _viewModel.Apply(r => ReportLayout.AddItemNextTo(r, Item(sender).Id, ItemSide.Below));

    private void OnAddAtEnd(object sender, RoutedEventArgs e) => _viewModel.Apply(r => ReportLayout.AddItemAtEnd(r));

    private void OnInsertBand(object sender, RoutedEventArgs e)
    {
        var band = Band(sender);
        _viewModel.Apply(r => ReportLayout.InsertBand(r, _viewModel.Bands.IndexOf(band) + 1));
    }

    private void OnAddVisual(object sender, RoutedEventArgs e) => EditVisual(Item(sender));

    private void OnAddText(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        _viewModel.Apply(r => ReportLayout.SetText(r, item.Id, new TextContent()));
        BeginTextEdit(item);
    }

    private void OnAddImage(object sender, RoutedEventArgs e) => PickImage(Item(sender), ImageFit.Contain);

    // --- Bands ---

    private void OnBandOptions(object sender, RoutedEventArgs e)
    {
        var band = Band(sender);
        // Converting between grid spans and pixel widths uses the band's real width (left gutter excluded)
        var bandWidth = ((FrameworkElement)((FrameworkElement)sender).Parent).ActualWidth - 24;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        foreach (var (mode, label) in new[] { (BandMode.Fit, Strings.BandFit), (BandMode.Scroll, Strings.BandScroll), (BandMode.Wrap, Strings.BandWrap) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = band.Mode == mode };
            item.Click += (_, _) => _viewModel.Apply(r => ReportLayout.SetBandMode(r, band.Id, mode, bandWidth, DashboardViewModel.Gap));
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var insert = new MenuItem { Header = Strings.InsertBand };
        insert.Click += (_, _) => _viewModel.Apply(r => ReportLayout.InsertBand(r, _viewModel.Bands.IndexOf(band) + 1));
        menu.Items.Add(insert);
        var remove = new MenuItem { Header = Strings.RemoveBand };
        remove.Click += (_, _) =>
        {
            ClearSelectionOf(band.Items);
            _viewModel.Apply(r => ReportLayout.RemoveBand(r, band.Id));
        };
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

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

    private void OnToggleFullRow(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        _viewModel.Apply(r => ReportLayout.SetFullRow(r, item.Id, !item.IsFullRow));
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        ClearSelectionOf([item]);
        _viewModel.Apply(r => ReportLayout.RemoveItem(r, item.Id));
    }

    /// <summary>A removed visual can no longer filter the others.</summary>
    private void ClearSelectionOf(IEnumerable<DashboardItemViewModel> items)
    {
        if (VisualView.GetFilterContext(this) is { Selection: { } selection } filters
            && items.Any(i => i.Visual?.Id == selection.SourceVisualId))
            filters.ClearSelection();
    }

    // --- Text ---

    private void OnTextMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && IsLayoutEditable) BeginTextEdit(Item(sender));
    }

    private static void BeginTextEdit(DashboardItemViewModel item)
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

    // --- Moving: the item follows the pointer and the others make room live ---

    private void OnGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        // Where the item was grabbed, so that it does not jump under the pointer
        _grabOffset = e.GetPosition(Container(sender));
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
        _drop = null;
        _dropKey = null;
        var result = DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(DashboardItemViewModel), _dragged), DragDropEffects.Move);
        if (result == DragDropEffects.None) _viewModel.EndPreview();
        _dragged = null;
    }

    /// <summary>The element that hosts an item in its band (grid cell or strip slot).</summary>
    private static FrameworkElement Container(object sender)
    {
        DependencyObject current = (DependencyObject)sender;
        while (current is not ContentPresenter { Content: DashboardItemViewModel })
            current = VisualTreeHelper.GetParent(current);
        return (FrameworkElement)current;
    }

    private void OnBandDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!IsLayoutEditable || _dragged is not { } item || !e.Data.GetDataPresent(typeof(DashboardItemViewModel)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        var band = Band(sender);
        string key;
        Func<ReportDefinition, ReportDefinition> drop;
        if (band.Mode == BandMode.Fit)
        {
            if (!_grids.TryGetValue(band, out var grid)) return;
            var position = e.GetPosition(grid);
            var span = item.IsFullRow ? band.Columns : Math.Min(item.ColumnSpan, band.Columns);
            var column = Math.Clamp((int)Math.Round((position.X - _grabOffset.X) / grid.ColumnPitch(grid.ActualWidth)), 0, band.Columns - span);
            var row = Math.Max(0, (int)Math.Round((position.Y - _grabOffset.Y) / grid.RowPitch));
            key = $"{band.Id}/{column}/{row}";
            drop = r => ReportLayout.MoveItem(r, item.Id, band.Id, column, row);
        }
        else
        {
            var index = StripIndex((ItemsControl)sender, band, item, e.GetPosition((IInputElement)sender));
            key = $"{band.Id}#{index}";
            drop = r => ReportLayout.MoveItemTo(r, item.Id, band.Id, index);
        }

        if (key == _dropKey) return;
        _dropKey = key;
        _drop = drop;
        _viewModel.Preview(drop);
    }

    /// <summary>
    /// Index (in the band as saved) before which the dragged item goes: before the first other item whose
    /// centre is after the pointer, reading line by line.
    /// </summary>
    private int StripIndex(ItemsControl strip, DashboardBandViewModel band, DashboardItemViewModel dragged, Point pointer)
    {
        var saved = _viewModel.Report.Layout!.Bands.FirstOrDefault(b => b.Id == band.Id)?.Items.Select(i => i.Id).ToList() ?? [];
        foreach (var other in band.Items.Where(i => i != dragged))
        {
            if (strip.ItemContainerGenerator.ContainerFromItem(other) is not FrameworkElement container) continue;
            var bounds = container.TransformToAncestor(strip).TransformBounds(new Rect(container.RenderSize));
            var sameLine = band.Mode == BandMode.Scroll || (pointer.Y >= bounds.Top && pointer.Y <= bounds.Bottom);
            var before = band.Mode == BandMode.Scroll || sameLine ? pointer.X < bounds.Left + bounds.Width / 2 : pointer.Y < bounds.Top;
            if (before)
                return saved.IndexOf(other.Id) is var index and >= 0 ? index : saved.Count;
        }
        return saved.Count;
    }

    private void OnBandDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_drop is { } drop) _viewModel.Apply(drop);
        _drop = null;
        _dropKey = null;
    }

    // --- Resizing: whole cells in a grid band, pixels (width) and rows (height) in other bands ---

    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        var item = Item(sender);
        _resizeStart = (item.ColumnSpan, item.RowSpan, item.Width);
        _resizeKey = null;
        _resize = null;
        _resizeDelta = default;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        var item = Item(sender);
        _resizeDelta += new Vector(e.HorizontalChange, e.VerticalChange);
        var rowPitch = item.Band.RowHeight + DashboardViewModel.Gap;
        var rowSpan = Math.Max(1, _resizeStart.RowSpan + (int)Math.Round(_resizeDelta.Y / rowPitch));

        string key;
        Func<ReportDefinition, ReportDefinition> resize;
        if (item.Band.Mode == BandMode.Fit)
        {
            if (!_grids.TryGetValue(item.Band, out var grid)) return;
            var columnSpan = Math.Max(1, _resizeStart.ColumnSpan + (int)Math.Round(_resizeDelta.X / grid.ColumnPitch(grid.ActualWidth)));
            key = $"{columnSpan}x{rowSpan}";
            resize = r => ReportLayout.ResizeItem(r, item.Id, columnSpan, rowSpan);
        }
        else
        {
            // A full-row item keeps the band width: only its height changes
            var width = item.IsFullRow ? _resizeStart.Width : Math.Max(ReportLayout.MinWidth, Math.Round(_resizeStart.Width + _resizeDelta.X));
            key = $"{width}x{rowSpan}";
            resize = r => ReportLayout.ResizeItemWidth(r, item.Id, width, rowSpan);
        }

        if (key == _resizeKey) return;
        _resizeKey = key;
        _resize = resize;
        _viewModel.Preview(resize);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resize is { } resize) _viewModel.Apply(resize);
        else _viewModel.EndPreview();
        _resize = null;
        _resizeKey = null;
    }
}
