using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EasyBI.Abstractions;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Self-service designer: the user drags fields onto shelves (or double-clicks them), reorders or moves the chips,
/// picks a visual type and sees a live preview. Drag and drop goes through <see cref="DragAndDrop.DragSource"/> and
/// <see cref="DragAndDrop.DropTarget"/>, the view-model deciding what each shelf accepts.
/// </summary>
public partial class ReportDesigner : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(ReportDesigner),
        new PropertyMetadata(null, (d, e) => ((ReportDesigner)d).OnContextChanged((EasyBIContext?)e.NewValue)));

    private static readonly DependencyPropertyKey VisualPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Visual), typeof(VisualDefinition), typeof(ReportDesigner), new PropertyMetadata(null));

    public static readonly DependencyProperty VisualProperty = VisualPropertyKey.DependencyProperty;

    public ReportDesigner() => InitializeComponent();

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
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
}
