using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Self-service designer: the user drags fields onto shelves (or double-clicks them), reorders or moves the chips,
/// picks a visual type and sees a live preview. Drag and drop goes through <see cref="DragAndDrop.DragSource"/> and
/// <see cref="DragAndDrop.DropTarget"/>, the view-model deciding what each shelf accepts.
/// </summary>
/// <remarks>
/// <para>
/// Its <c>DataContext</c> is a <see cref="DesignerViewModel"/>, which the application creates and keeps:
/// <c>&lt;bi:ReportDesigner DataContext="{Binding Designer}" /&gt;</c>. The application loads a visual into it, reads the
/// visual being designed, and gives it the <see cref="DesignerViewModel.Rules"/> its changes must pass (also declared
/// as behaviors: <see cref="DesignerRuleBehavior"/>).
/// </para>
/// <para>
/// Lookless: the default template (in <c>Themes/ReportDesigner.xaml</c>) binds to the view-model and declares the
/// <see cref="ReportDesignerSlots"/>.
/// </para>
/// </remarks>
public class ReportDesigner : Control
{
    /// <summary>Whether the field panel, on the left, is shown (two-way; true by default).</summary>
    public static readonly DependencyProperty IsFieldPanelOpenProperty = DependencyProperty.Register(
        nameof(IsFieldPanelOpen), typeof(bool), typeof(ReportDesigner), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Whether the properties panel, on the right, is shown (two-way; true by default). Selecting a chip opens it.</summary>
    public static readonly DependencyProperty IsPropertiesPanelOpenProperty = DependencyProperty.Register(
        nameof(IsPropertiesPanelOpen), typeof(bool), typeof(ReportDesigner), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    static ReportDesigner() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ReportDesigner), new FrameworkPropertyMetadata(typeof(ReportDesigner)));

    public ReportDesigner()
    {
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is DesignerViewModel old) old.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is DesignerViewModel designer) designer.PropertyChanged += OnViewModelPropertyChanged;
        };
        // Undo and redo the changes of the visual (a text box being edited keeps its own)
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => ViewModel?.Undo(), (_, e) => e.CanExecute = ViewModel?.CanUndo == true));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => ViewModel?.Redo(), (_, e) => e.CanExecute = ViewModel?.CanRedo == true));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Redo, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        PreviewKeyDown += OnPreviewKeyDown;
        // A click in the wells (on a chip...) gives the designer the keyboard, for the shortcuts
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (!TakesTyping(e.OriginalSource as DependencyObject) && !IsKeyboardFocused) Focus();
        };
        // The wells show whether they accept what is dragged, from the start of the drag (the events are static)
        Loaded += (_, _) =>
        {
            DragAndDrop.DragSource.DragStarted += OnDragStarted;
            DragAndDrop.DragSource.DragEnded += OnDragEnded;
        };
        Unloaded += (_, _) =>
        {
            DragAndDrop.DragSource.DragStarted -= OnDragStarted;
            DragAndDrop.DragSource.DragEnded -= OnDragEnded;
        };
    }

    /// <summary>
    /// The shortcuts of the chips: arrows go from one to another (Up and Down from one well to another), Ctrl+Left/Right
    /// move the selected one within its well, Delete removes it, Escape selects the visual again. Controls taking typing
    /// (text boxes, lists) keep their keys.
    /// </summary>
    protected virtual void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } designer || TakesTyping(e.OriginalSource as DependencyObject)) return;
        var control = ModifierKeysState.Ctrl;
        switch (e.Key)
        {
            case Key.Left or Key.Right when control:
                designer.MoveSelected(forward: e.Key == Key.Right);
                break;
            case Key.Left or Key.Right:
                designer.SelectNext(forward: e.Key == Key.Right);
                break;
            case Key.Up or Key.Down:
                designer.SelectAcross(down: e.Key == Key.Down);
                break;
            case Key.Delete or Key.Back when designer.SelectedItem is not null:
                designer.RemoveSelected();
                break;
            case Key.Escape when designer.SelectedItem is not null:
                designer.SelectedItem = null;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Text boxes, lists, date pickers... and their popups
    private static bool TakesTyping(DependencyObject? element)
    {
        for (; element is not null; element = ParentOf(element))
        {
            if (element is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.Selector or DatePicker) return true;
            if (element is ReportDesigner) return false;
        }
        return false;
    }

    // A popup's content (the items of an open list) has its own visual root: its logical parent, the popup, leads
    // back to the control that opened it
    private static DependencyObject? ParentOf(DependencyObject element) =>
        (element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(element) : null)
            ?? LogicalTreeHelper.GetParent(element);

    private void OnDragStarted(object? sender, DragAndDrop.DragStartedEventArgs e)
    {
        if (ViewModel is { } designer) designer.DraggedItem = e.Item;
    }

    private void OnDragEnded(object? sender, EventArgs e)
    {
        if (ViewModel is { } designer) designer.DraggedItem = null;
    }

    public bool IsFieldPanelOpen
    {
        get => (bool)GetValue(IsFieldPanelOpenProperty);
        set => SetValue(IsFieldPanelOpenProperty, value);
    }

    public bool IsPropertiesPanelOpen
    {
        get => (bool)GetValue(IsPropertiesPanelOpenProperty);
        set => SetValue(IsPropertiesPanelOpenProperty, value);
    }

    // The view-model the template shows: the data context
    private DesignerViewModel? ViewModel => DataContext as DesignerViewModel;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Selecting a chip shows its settings: the properties panel opens if it was folded
        if (e.PropertyName == nameof(DesignerViewModel.SelectedItem) && ViewModel?.SelectedItem is not null)
            SetCurrentValue(IsPropertiesPanelOpenProperty, true);
    }
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
}
