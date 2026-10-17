using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Wpf.Dashboard.Behaviors;
using EasyBI.Wpf.Renderers;
using EasyBI.Wpf.Resources;
using Microsoft.Win32;
using Microsoft.Xaml.Behaviors;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Dashboard that end users arrange: a <see cref="DashboardPanel"/> of <see cref="DashboardItem"/>s showing visuals,
/// texts and images. The view owns the content and the save format (<see cref="Document"/>); every interaction (the
/// "+" buttons, moving, resizing, toolbars, band menus, in-place text editing, undo/redo...) is a behavior from
/// <see cref="Behaviors"/>, attached by default and replaceable one by one.
/// </summary>
/// <remarks>
/// Hooks for applications: the <c>...Requested</c> events replace the built-in dialogs, <see cref="ItemRemoving"/>
/// can veto a removal, <see cref="Changed"/> reports every change, <see cref="DashboardCommands"/> trigger the actions
/// from custom templates, and the public methods (<see cref="AddNextTo"/>, <see cref="SetVisual"/>...) drive the view
/// from code. Set <see cref="UseDefaultBehaviors"/> to false to pick the behaviors yourself.
/// </remarks>
public partial class DashboardView : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(DashboardView),
        new PropertyMetadata(null, (d, e) => ((DashboardView)d).OnContextChanged((EasyBIContext?)e.NewValue)));

    /// <summary>The report and its layout (two-way). Setting it shows another dashboard.</summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(DashboardDocument), typeof(DashboardView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((DashboardView)d).OnDocumentChanged((DashboardDocument?)e.NewValue)));

    /// <summary>Read-only dashboard: no editing chrome, no editing command. Inherited, so item templates can use it.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.RegisterAttached(
        "IsReadOnly", typeof(bool), typeof(DashboardView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, (d, _) => CommandManager.InvalidateRequerySuggested()));

    public static readonly DependencyProperty UseDefaultBehaviorsProperty = DependencyProperty.Register(
        nameof(UseDefaultBehaviors), typeof(bool), typeof(DashboardView), new PropertyMetadata(true));

    private ReportDefinition _report = new();
    private DashboardDocument _committed = new();
    private int _editDepth;
    private bool _pushing;
    private bool _behaviorsAttached;

    public DashboardView()
    {
        InitializeComponent();
        // Keyboard shortcuts (Ctrl+Z...) need the focus inside the view
        Focusable = true;
        FocusVisualStyle = null;
        Panel.ArrangementChanged += (_, _) => Commit();
        BindCommands();
        Show(new DashboardDocument());
        Loaded += (_, _) => AttachDefaultBehaviors();
    }

    /// <summary>Raised after every change made through the dashboard (not after <see cref="Load"/>).</summary>
    public event EventHandler<DashboardChangedEventArgs>? Changed;

    /// <summary>Raised after <see cref="Load"/> showed another document (undo histories start over).</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised before a visual is created or edited.</summary>
    public event EventHandler<EditVisualRequestedEventArgs>? EditVisualRequested;

    /// <summary>Raised before an image is picked.</summary>
    public event EventHandler<ImageRequestedEventArgs>? ImageRequested;

    /// <summary>Raised when a text should be edited.</summary>
    public event EventHandler<TextEditRequestedEventArgs>? TextEditRequested;

    /// <summary>Raised before an item is removed.</summary>
    public event EventHandler<ItemRemovingEventArgs>? ItemRemoving;

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public DashboardDocument? Document
    {
        get => (DashboardDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static bool GetIsReadOnly(DependencyObject element) => (bool)element.GetValue(IsReadOnlyProperty);

    public static void SetIsReadOnly(DependencyObject element, bool value) => element.SetValue(IsReadOnlyProperty, value);

    /// <summary>
    /// True (default) attaches <see cref="CreateDefaultBehaviors"/> when the view loads, except the types already in
    /// <c>Interaction.Behaviors</c>: declaring one there replaces its default.
    /// </summary>
    public bool UseDefaultBehaviors
    {
        get => (bool)GetValue(UseDefaultBehaviorsProperty);
        set => SetValue(UseDefaultBehaviorsProperty, value);
    }

    /// <summary>The panel arranging the items.</summary>
    public DashboardPanel Panel => PART_Panel;

    /// <summary>Items in the panel.</summary>
    public IEnumerable<DashboardItem> Items => Panel.Children.OfType<DashboardItem>();

    /// <summary>Largest image the built-in picker embeds, in bytes (2 MB by default).</summary>
    public int MaxImageBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>The behaviors attached by default, in a new instance each time.</summary>
    public static IReadOnlyList<Behavior> CreateDefaultBehaviors() =>
    [
        new AddButtonsBehavior(),
        new FreeSpaceBehavior(),
        new MoveBehavior(),
        new ResizeBehavior(),
        new ItemToolbarBehavior(),
        new BandMenuBehavior(),
        new BandScrollBehavior(),
        new TextEditBehavior(),
        new UndoRedoBehavior(),
    ];

    /// <summary>The item containing an element (or the element itself), if any.</summary>
    public static DashboardItem? ItemOf(DependencyObject? element)
    {
        while (element is not null and not DashboardItem)
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        return element as DashboardItem;
    }

    // --- Document ---

    /// <summary>
    /// Shows a report and its layout. Visuals the layout does not place are added at the end; layout items referencing
    /// a missing visual become empty. Items showing the same visual as before are kept, so they do not query again.
    /// </summary>
    public void Load(DashboardDocument document)
    {
        Show(document);
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the dashboard back to a previous state (undo/redo): like <see cref="Load"/>, without starting a new history.</summary>
    public void Restore(DashboardDocument document) => Show(document);

    private void Show(DashboardDocument document)
    {
        _report = document.Report;
        var visuals = document.Report.Visuals.GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First());
        var layout = DashboardLayoutEngine.Place(document.Layout, document.Report.Visuals.Select(v => v.Id));
        var existing = Items.Where(i => i.Visual is not null).GroupBy(i => i.Visual!.Id).ToDictionary(g => g.Key, g => g.First());

        _editDepth++;
        try
        {
            DashboardLayout.SetLayout(Panel, layout, item =>
            {
                var visual = item.VisualId is { } id ? visuals.GetValueOrDefault(id) : null;
                if (visual is not null && existing.Remove(visual.Id, out var kept))
                {
                    kept.Visual = visual;
                    return kept;
                }
                return NewItem(visual);
            });
        }
        finally
        {
            _editDepth--;
        }
        _committed = Capture();
        PushDocument();
    }

    /// <summary>The current report and layout.</summary>
    public DashboardDocument Capture()
    {
        var layout = DashboardLayout.GetLayout(Panel, e => (e as DashboardItem)?.Visual?.Id);
        // Visuals are saved in reading order
        var visuals = Enumerable.Range(0, Panel.BandCount).SelectMany(Panel.ChildrenOf)
            .OfType<DashboardItem>().Select(i => i.Visual).OfType<VisualDefinition>().ToArray();
        return new DashboardDocument { Report = _report with { Visuals = visuals }, Layout = layout };
    }

    /// <summary>
    /// Groups several changes into one (one <see cref="Changed"/>, one undo step). Changes made while an edit is open,
    /// such as the live preview of a drag, are only reported when it is committed; cancelling puts the dashboard back.
    /// </summary>
    public DashboardEdit BeginEdit()
    {
        _editDepth++;
        return new DashboardEdit(this);
    }

    internal void EndEdit(bool commit)
    {
        _editDepth--;
        if (_editDepth > 0) return;
        if (commit) Commit();
        else Show(_committed);
    }

    /// <summary>Reports the current state as a change if it differs from the last one.</summary>
    public void Commit()
    {
        if (_editDepth > 0) return;
        var after = Capture();
        var before = _committed;
        if (DashboardSerializer.Serialize(after) == DashboardSerializer.Serialize(before)) return;
        _committed = after;
        PushDocument();
        Changed?.Invoke(this, new DashboardChangedEventArgs(before, after));
    }

    private void OnDocumentChanged(DashboardDocument? document)
    {
        if (!_pushing) Load(document ?? new DashboardDocument());
    }

    private void PushDocument()
    {
        _pushing = true;
        try
        {
            SetCurrentValue(DocumentProperty, _committed);
        }
        finally
        {
            _pushing = false;
        }
    }

    private void OnContextChanged(EasyBIContext? context)
    {
        foreach (var item in Items) item.Context = context;
    }

    private DashboardItem NewItem(VisualDefinition? visual) => new() { Visual = visual, Context = Context };

    private void AttachDefaultBehaviors()
    {
        if (_behaviorsAttached || !UseDefaultBehaviors) return;
        _behaviorsAttached = true;
        var behaviors = Interaction.GetBehaviors(this);
        foreach (var behavior in CreateDefaultBehaviors())
            if (behaviors.All(b => b.GetType() != behavior.GetType())) behaviors.Add(behavior);
    }

    // --- Items ---

    /// <summary>Adds an empty item next to another one (see <see cref="DashboardLayoutEngine.AddNextTo"/>).</summary>
    public DashboardItem AddNextTo(DashboardItem anchor, ItemSide side)
    {
        var item = NewItem(null);
        DashboardLayout.AddNextTo(anchor, item, side);
        return item;
    }

    /// <summary>Adds an empty item in a free space: it takes the room left by its constrained neighbours.</summary>
    public DashboardItem AddIn(DashboardFreeSpace space)
    {
        var item = NewItem(null);
        DashboardLayout.AddNextTo(Panel, space.AnchorId, item, space.Side);
        return item;
    }

    /// <summary>Adds an empty item at the end of a band.</summary>
    public DashboardItem AddItem(int band) => InsertLine(band, int.MaxValue);

    /// <summary>Inserts a line holding an empty item before line <paramref name="index"/> of a grid band (a position in other bands).</summary>
    public DashboardItem InsertLine(int band, int index)
    {
        var item = NewItem(null);
        DashboardLayout.InsertAt(Panel, item, band, index);
        return item;
    }

    /// <summary>Removes an item and its visual, unless <see cref="ItemRemoving"/> cancels it.</summary>
    public bool Remove(DashboardItem item)
    {
        var args = new ItemRemovingEventArgs(item);
        ItemRemoving?.Invoke(this, args);
        if (args.Cancel) return false;

        using var edit = BeginEdit();
        ClearSelectionOf([item]);
        DashboardLayout.Remove(item);
        EnsureNotEmpty();
        edit.Commit();
        return true;
    }

    /// <summary>Removes the size constraints of an item: it takes the room its neighbours leave again.</summary>
    public void SetAutoSize(DashboardItem item) => DashboardLayout.SetAuto(item);

    /// <summary>Shows a visual (chart, text or image) in an item; null empties it.</summary>
    public void SetVisual(DashboardItem item, VisualDefinition? visual)
    {
        if (item.Visual is { } previous && previous.Id != visual?.Id) ClearSelectionOf([item]);
        item.Visual = visual;
        Commit();
    }

    /// <summary>Edits the content of an item according to its kind.</summary>
    public void EditItem(DashboardItem item)
    {
        switch (item.Kind)
        {
            case DashboardItemKind.Text: RequestTextEdit(item); break;
            case DashboardItemKind.Image: PickImage(item); break;
            default: EditVisual(item); break;
        }
    }

    /// <summary>Opens the visual editor: <see cref="EditVisualRequested"/>, or the built-in designer dialog.</summary>
    public void EditVisual(DashboardItem item)
    {
        var args = new EditVisualRequestedEventArgs(item, visual => SetVisual(item, visual));
        EditVisualRequested?.Invoke(this, args);
        if (args.Handled || Context is not { } context) return;

        var editor = new VisualEditorWindow(context, item.Kind == DashboardItemKind.Visual ? item.Visual : null) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Visual is { } visual)
            args.Complete(visual);
    }

    /// <summary>Turns an item into an empty text and starts editing it.</summary>
    public void AddText(DashboardItem item)
    {
        SetVisual(item, new VisualDefinition { Mark = MarkType.Text, Text = new TextContent() });
        RequestTextEdit(item);
    }

    /// <summary>Raises <see cref="TextEditRequested"/> (handled by <see cref="TextEditBehavior"/>).</summary>
    public void RequestTextEdit(DashboardItem item) => TextEditRequested?.Invoke(this, new TextEditRequestedEventArgs(item));

    /// <summary>Picks an image for an item: <see cref="ImageRequested"/>, or the built-in file dialog.</summary>
    public void PickImage(DashboardItem item)
    {
        var args = new ImageRequestedEventArgs(item, image => SetVisual(item, item.Visual is { Mark: MarkType.Image } visual
            ? visual with { Image = image }
            : new VisualDefinition { Mark = MarkType.Image, Image = image }));
        ImageRequested?.Invoke(this, args);
        if (args.Handled) return;

        var dialog = new OpenFileDialog { Filter = Strings.ImageFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var bytes = File.ReadAllBytes(dialog.FileName);
        string? problem = null;
        if (bytes.Length > MaxImageBytes)
            problem = Strings.Format(Strings.ImageTooLarge, MaxImageBytes / (1024 * 1024));
        var image = new ImageContent
        {
            Data = Convert.ToBase64String(bytes),
            MediaType = MediaTypeOf(dialog.FileName),
            Fit = args.Current?.Fit ?? ImageFit.Contain,
        };
        if (problem is null && ImageRenderer.Decode(image) is null)
            problem = Strings.ImageUnreadable;
        if (problem is not null)
        {
            MessageBox.Show(Window.GetWindow(this), problem, Strings.ChangeImage, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        args.Complete(image);
    }

    private static string MediaTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/png",
    };

    // --- Bands ---

    /// <summary>Inserts a band holding one empty item.</summary>
    public DashboardItem InsertBand(int index, BandMode mode = BandMode.Fit)
    {
        var item = NewItem(null);
        DashboardLayout.InsertBand(Panel, index, mode, item);
        return item;
    }

    /// <summary>Removes a band, its items and their visuals.</summary>
    public void RemoveBand(int index)
    {
        using var edit = BeginEdit();
        ClearSelectionOf(Panel.ChildrenOf(index).OfType<DashboardItem>());
        DashboardLayout.RemoveBand(Panel, index);
        EnsureNotEmpty();
        edit.Commit();
    }

    /// <summary>Changes the mode of a band; items keep their size on screen.</summary>
    public void SetBandMode(int index, BandMode mode) => DashboardLayout.SetBandMode(Panel, index, mode);

    /// <summary>There is always an item to start from.</summary>
    private void EnsureNotEmpty()
    {
        if (Panel.Children.Count == 0) DashboardLayout.Add(Panel, NewItem(null), 0);
    }

    /// <summary>A removed visual can no longer filter the others.</summary>
    private void ClearSelectionOf(IEnumerable<DashboardItem> items)
    {
        if (VisualView.GetFilterContext(this) is { Selection: { } selection } filters
            && items.Any(i => i.Visual?.Id == selection.SourceVisualId))
            filters.ClearSelection();
    }

    // --- Commands ---

    private void BindCommands()
    {
        BindItem(DashboardCommands.AddVisual, EditVisual);
        BindItem(DashboardCommands.AddText, AddText);
        BindItem(DashboardCommands.AddImage, PickImage);
        BindItem(DashboardCommands.EditItem, EditItem);
        BindItem(DashboardCommands.RemoveItem, item => Remove(item));
        BindItem(DashboardCommands.ResetSize, SetAutoSize);
        BindItem(DashboardCommands.AddRight, item => AddNextTo(item, ItemSide.Right));
        BindItem(DashboardCommands.AddBelow, item => AddNextTo(item, ItemSide.Below));
        BindBand(DashboardCommands.InsertBand, index => InsertBand(index));
        BindBand(DashboardCommands.RemoveBand, RemoveBand);
    }

    private void BindItem(ICommand command, Action<DashboardItem> execute) =>
        CommandBindings.Add(new CommandBinding(command,
            (_, e) => { if (Target(e) is { } item && !IsReadOnly) execute(item); },
            (_, e) => e.CanExecute = !IsReadOnly && Target(e) is not null));

    private void BindBand(ICommand command, Action<int> execute) =>
        CommandBindings.Add(new CommandBinding(command,
            (_, e) => { if (BandIndex(e.Parameter) is { } index && !IsReadOnly) execute(index); },
            (_, e) => e.CanExecute = !IsReadOnly && BandIndex(e.Parameter) is not null));

    private static DashboardItem? Target(RoutedEventArgs e) =>
        (e as ExecutedRoutedEventArgs)?.Parameter as DashboardItem
        ?? (e as CanExecuteRoutedEventArgs)?.Parameter as DashboardItem
        ?? ItemOf(e.OriginalSource as DependencyObject);

    private static int? BandIndex(object? parameter) => parameter switch
    {
        int index => index,
        string text when int.TryParse(text, out var index) => index,
        _ => null,
    };
}

/// <summary>An open group of changes (see <see cref="DashboardView.BeginEdit"/>); disposing it without committing cancels it.</summary>
public sealed class DashboardEdit : IDisposable
{
    private DashboardView? _view;

    internal DashboardEdit(DashboardView view) => _view = view;

    /// <summary>Reports the grouped changes as one change.</summary>
    public void Commit() => End(true);

    /// <summary>Puts the dashboard back as it was when the edit began.</summary>
    public void Cancel() => End(false);

    public void Dispose() => End(false);

    private void End(bool commit)
    {
        var view = _view;
        _view = null;
        view?.EndEdit(commit);
    }
}
