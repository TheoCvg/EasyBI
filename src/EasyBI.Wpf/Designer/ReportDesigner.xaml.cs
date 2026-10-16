using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Self-service designer: the user drags fields onto shelves,
/// picks a visual type and sees a live preview.
/// </summary>
public partial class ReportDesigner : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(ReportDesigner),
        new PropertyMetadata(null, (d, e) => ((ReportDesigner)d).OnContextChanged((EasyBIContext?)e.NewValue)));

    private static readonly DependencyPropertyKey VisualPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Visual), typeof(VisualDefinition), typeof(ReportDesigner), new PropertyMetadata(null));

    public static readonly DependencyProperty VisualProperty = VisualPropertyKey.DependencyProperty;

    private Point? _dragStart;

    public ReportDesigner()
    {
        InitializeComponent();
        foreach (var list in new[] { DimensionList, MeasureList })
        {
            list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(null);
            list.PreviewMouseMove += OnFieldMouseMove;
            list.MouseDoubleClick += (_, e) =>
            {
                if (FieldUnder(e) is { } field) ViewModel?.AddToDefaultShelf(field);
            };
        }
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    /// <summary>The visual being designed (read-only).</summary>
    public VisualDefinition? Visual => (VisualDefinition?)GetValue(VisualProperty);

    public DesignerViewModel? ViewModel => LayoutRoot.DataContext as DesignerViewModel;

    public void Edit(VisualDefinition visual) => ViewModel?.Load(visual);

    public void Clear() => ViewModel?.Clear();

    private void OnContextChanged(EasyBIContext? context)
    {
        if (ViewModel is { } old) old.PropertyChanged -= OnViewModelPropertyChanged;
        var vm = context is null ? null : new DesignerViewModel(context);
        if (vm is not null) vm.PropertyChanged += OnViewModelPropertyChanged;
        LayoutRoot.DataContext = vm;
        SetValue(VisualPropertyKey, vm?.Visual);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignerViewModel.Visual))
            SetValue(VisualPropertyKey, ViewModel?.Visual);
    }

    private void OnFieldMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start) return;
        var delta = start - e.GetPosition(null);
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (FieldUnder(e) is not { } field) return;
        _dragStart = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(FieldDescriptor), field), DragDropEffects.Copy);
    }

    private static FieldDescriptor? FieldUnder(RoutedEventArgs e) =>
        (e.OriginalSource as FrameworkElement)?.DataContext as FieldDescriptor;

    private void OnShelfDragLeave(object sender, DragEventArgs e) => Highlight((Border)sender, false);

    private void OnShelfDragOver(object sender, DragEventArgs e)
    {
        var shelf = (Border)sender;
        var accepted = e.Data.GetData(typeof(FieldDescriptor)) is FieldDescriptor field
            && ViewModel?.CanDrop(field, (Shelf)shelf.Tag) == true;
        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(shelf, accepted);
        e.Handled = true;
    }

    private void OnShelfDrop(object sender, DragEventArgs e)
    {
        var shelf = (Border)sender;
        Highlight(shelf, false);
        if (e.Data.GetData(typeof(FieldDescriptor)) is FieldDescriptor field)
            ViewModel?.Drop(field, (Shelf)shelf.Tag);
    }

    private void Highlight(Border shelf, bool on)
    {
        if (on) shelf.BorderBrush = (Brush)FindResource("AccentBrush");
        else shelf.ClearValue(Border.BorderBrushProperty);
    }
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
}
