using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// The fields of a semantic model, ready to be dragged (as <see cref="ModelField"/>): onto the shelves of a
/// <see cref="Designer.ReportDesigner"/>, or onto a dashboard to make or complete a visual. A model picker lists the
/// models of the <see cref="Context"/>; double-clicking a field runs <see cref="ActivateCommand"/>.
/// </summary>
/// <remarks>
/// Lookless: the default template (in <c>Themes/FieldList.xaml</c>) shows <see cref="ModelOptions"/>,
/// <see cref="Dimensions"/> and <see cref="Measures"/>, declares the <see cref="FieldListSlots"/> and acts through
/// <see cref="AddMeasureCommand"/>, <see cref="EditMeasureCommand"/> and <see cref="DeleteMeasureCommand"/>.
/// </remarks>
public class FieldList : Control
{
    private EasyBIContext? _followed;

    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(FieldList),
        new PropertyMetadata(null, (d, _) => ((FieldList)d).OnContextChanged()));

    /// <summary>The model whose fields are listed (two-way); the first model of the context by default.</summary>
    public static readonly DependencyProperty SelectedModelProperty = DependencyProperty.Register(
        nameof(SelectedModel), typeof(ISemanticModel), typeof(FieldList),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FieldList)d).Refresh()));

    /// <summary>Text searched in the field captions (two-way; see <see cref="TextMatch"/>): only the matching fields are listed.</summary>
    public static readonly DependencyProperty SearchProperty = DependencyProperty.Register(
        nameof(Search), typeof(string), typeof(FieldList),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FieldList)d).Refresh()));

    /// <summary>Run with the <see cref="ModelField"/> double-clicked.</summary>
    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.Register(
        nameof(ActivateCommand), typeof(ICommand), typeof(FieldList), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey ModelOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ModelOptions), typeof(IReadOnlyList<Option<ISemanticModel>>), typeof(FieldList),
        new PropertyMetadata(Array.Empty<Option<ISemanticModel>>()));

    public static readonly DependencyProperty ModelOptionsProperty = ModelOptionsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey DimensionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Dimensions), typeof(IReadOnlyList<ModelField>), typeof(FieldList), new PropertyMetadata(Array.Empty<ModelField>()));

    public static readonly DependencyProperty DimensionsProperty = DimensionsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey MeasuresPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Measures), typeof(IReadOnlyList<ModelField>), typeof(FieldList), new PropertyMetadata(Array.Empty<ModelField>()));

    public static readonly DependencyProperty MeasuresProperty = MeasuresPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanAddMeasurePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanAddMeasure), typeof(bool), typeof(FieldList), new PropertyMetadata(false));

    public static readonly DependencyProperty CanAddMeasureProperty = CanAddMeasurePropertyKey.DependencyProperty;

    /// <summary>Opens the editor of a new calculated measure of the <see cref="SelectedModel"/>.</summary>
    public static RoutedCommand AddMeasureCommand { get; } = new(nameof(AddMeasureCommand), typeof(FieldList));

    /// <summary>Edits a calculated measure: the parameter (or the data context of the source) is its <see cref="ModelField"/>.</summary>
    public static RoutedCommand EditMeasureCommand { get; } = new(nameof(EditMeasureCommand), typeof(FieldList));

    /// <summary>Deletes a calculated measure after a confirmation; same parameter as <see cref="EditMeasureCommand"/>.</summary>
    public static RoutedCommand DeleteMeasureCommand { get; } = new(nameof(DeleteMeasureCommand), typeof(FieldList));

    static FieldList() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FieldList), new FrameworkPropertyMetadata(typeof(FieldList)));

    public FieldList()
    {
        CommandBindings.Add(new CommandBinding(AddMeasureCommand,
            (_, _) => { if (Context is { } context && SelectedModel is { } model) EditMeasure(new CalculatedMeasureEditor(context, model)); },
            (_, e) => e.CanExecute = CanAddMeasure));
        CommandBindings.Add(new CommandBinding(EditMeasureCommand,
            (_, e) => { if (Context is { } context && Calculated(e) is { } field) EditMeasure(new CalculatedMeasureEditor(context, field.Model, field.Measure)); },
            (_, e) => e.CanExecute = Context is not null && Calculated(e) is not null));
        CommandBindings.Add(new CommandBinding(DeleteMeasureCommand,
            (_, e) => { if (Calculated(e) is { Measure: { } measure }) DeleteMeasure(measure); },
            (_, e) => e.CanExecute = Context is not null && Calculated(e) is not null));
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

    public string Search
    {
        get => (string)GetValue(SearchProperty);
        set => SetValue(SearchProperty, value);
    }

    public ICommand? ActivateCommand
    {
        get => (ICommand?)GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    /// <summary>The models of the <see cref="Context"/>, to pick the <see cref="SelectedModel"/> from.</summary>
    public IReadOnlyList<Option<ISemanticModel>> ModelOptions => (IReadOnlyList<Option<ISemanticModel>>)GetValue(ModelOptionsProperty);

    /// <summary>The dimensions of the <see cref="SelectedModel"/> matching the <see cref="Search"/>.</summary>
    public IReadOnlyList<ModelField> Dimensions => (IReadOnlyList<ModelField>)GetValue(DimensionsProperty);

    /// <summary>The measures of the <see cref="SelectedModel"/>, followed by the report's calculated measures, matching the <see cref="Search"/>.</summary>
    public IReadOnlyList<ModelField> Measures => (IReadOnlyList<ModelField>)GetValue(MeasuresProperty);

    /// <summary>Whether a calculated measure can be added: there is a context and a model.</summary>
    public bool CanAddMeasure => (bool)GetValue(CanAddMeasureProperty);

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        // The event's source is the control itself: find the field under the pointer
        for (var element = InputHitTest(e.GetPosition(this)) as DependencyObject; element is not null && element != this; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not ListBoxItem { DataContext: ModelField field }) continue;
            if (ActivateCommand is { } command && command.CanExecute(field)) command.Execute(field);
            return;
        }
    }

    private void OnContextChanged()
    {
        // The report's calculated measures are listed with the model's (weak: the list may be shorter-lived than the context)
        if (_followed != Context)
        {
            if (_followed is not null) WeakEventManager<MeasureContext, EventArgs>.RemoveHandler(_followed.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
            _followed = Context;
            if (_followed is not null) WeakEventManager<MeasureContext, EventArgs>.AddHandler(_followed.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
        }
        var models = Context?.Models ?? [];
        SetValue(ModelOptionsPropertyKey, models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption)).ToArray());
        if (SelectedModel is null || !models.Contains(SelectedModel)) SetCurrentValue(SelectedModelProperty, models.FirstOrDefault());
        Refresh();
    }

    private void Refresh()
    {
        var model = SelectedModel;
        var search = Search;
        SetValue(DimensionsPropertyKey, model is null ? Array.Empty<ModelField>() : model.Metadata.Dimensions
            .Where(f => TextMatch.Matches(f.Caption, search)).Select(f => new ModelField(model, f)).ToArray());
        SetValue(MeasuresPropertyKey, model is null ? Array.Empty<ModelField>() : model.Metadata.Measures.Select(f => new ModelField(model, f))
            .Concat(Context?.Measures.For(model.Metadata.Name).Select(m => new ModelField(model, MeasureContext.ToField(m), m)) ?? [])
            .Where(f => TextMatch.Matches(f.Caption, search))
            .ToArray());
        SetValue(CanAddMeasurePropertyKey, model is not null && Context is not null);
        // The buttons of the template ask their commands again
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnMeasuresChanged(object? sender, EventArgs e) => Refresh();

    // A calculated measure: the command parameter, or the data context of the command source (a button of its item)
    private static ModelField? Calculated(RoutedEventArgs e)
    {
        var field = (e as ExecutedRoutedEventArgs)?.Parameter as ModelField
            ?? (e as CanExecuteRoutedEventArgs)?.Parameter as ModelField
            ?? (e.OriginalSource as FrameworkElement)?.DataContext as ModelField;
        return field is { Measure: not null } ? field : null;
    }

    private void DeleteMeasure(CalculatedMeasure measure)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!, Strings.Format(Strings.DeleteMeasureConfirm, measure.Caption),
            Strings.DeleteMeasure, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) Context?.Measures.Remove(measure.Name);
    }

    private void EditMeasure(CalculatedMeasureEditor editor) =>
        new CalculatedMeasureWindow(editor) { Owner = Window.GetWindow(this) }.ShowDialog();
}
