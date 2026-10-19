using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf;

/// <summary>
/// The fields of a semantic model, ready to be dragged (as <see cref="ModelField"/>): onto the shelves of a
/// <see cref="Designer.ReportDesigner"/>, or onto a dashboard to make or complete a visual. A model picker lists the
/// models of the <see cref="Context"/>; double-clicking a field runs <see cref="ActivateCommand"/>.
/// </summary>
public partial class FieldList : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(FieldList),
        new PropertyMetadata(null, (d, _) => ((FieldList)d).OnContextChanged()));

    /// <summary>The model whose fields are listed (two-way); the first model of the context by default.</summary>
    public static readonly DependencyProperty SelectedModelProperty = DependencyProperty.Register(
        nameof(SelectedModel), typeof(ISemanticModel), typeof(FieldList),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FieldList)d).Refresh()));

    /// <summary>Run with the <see cref="ModelField"/> double-clicked.</summary>
    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.Register(
        nameof(ActivateCommand), typeof(ICommand), typeof(FieldList), new PropertyMetadata(null));

    public FieldList()
    {
        InitializeComponent();
        ModelPicker.SelectionChanged += (_, _) =>
        {
            if (ModelPicker.SelectedValue is ISemanticModel model) SelectedModel = model;
        };
        foreach (var list in new[] { DimensionList, MeasureList })
        {
            list.MouseDoubleClick += (_, e) =>
            {
                // The event's source is the list itself: find the item under the pointer
                if (list.InputHitTest(e.GetPosition(list)) is DependencyObject hit
                    && ItemsControl.ContainerFromElement(list, hit) is ListBoxItem { DataContext: ModelField field }
                    && ActivateCommand is { } command && command.CanExecute(field))
                    command.Execute(field);
            };
        }
        // Captions follow the language (weak subscription: the list may be shorter-lived than the shared texts)
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, (_, _) => OnContextChanged(), string.Empty);
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public ISemanticModel? SelectedModel
    {
        get => (ISemanticModel?)GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
    }

    public ICommand? ActivateCommand
    {
        get => (ICommand?)GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    private void OnContextChanged()
    {
        var models = Context?.Models ?? [];
        ModelPicker.ItemsSource = models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption)).ToArray();
        if (SelectedModel is null || !models.Contains(SelectedModel)) SetCurrentValue(SelectedModelProperty, models.FirstOrDefault());
        Refresh();
    }

    private void Refresh()
    {
        ModelPicker.SelectedValue = SelectedModel;
        var model = SelectedModel;
        DimensionList.ItemsSource = model?.Metadata.Dimensions.Select(f => new ModelField(model, f)).ToArray();
        MeasureList.ItemsSource = model?.Metadata.Measures.Select(f => new ModelField(model, f)).ToArray();
    }
}
