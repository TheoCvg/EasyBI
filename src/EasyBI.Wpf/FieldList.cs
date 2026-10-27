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
/// <see cref="Dimensions"/>, <see cref="Measures"/> and the report's <see cref="Variables"/>, declares the
/// <see cref="FieldListSlots"/> and acts through <see cref="AddMeasureCommand"/>, <see cref="EditMeasureCommand"/>,
/// <see cref="DeleteMeasureCommand"/>, <see cref="ResetMeasureCommand"/>, <see cref="AddVariableCommand"/>,
/// <see cref="EditVariableCommand"/> and <see cref="DeleteVariableCommand"/>.
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

    private static readonly DependencyPropertyKey VariablesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Variables), typeof(IReadOnlyList<VariableItem>), typeof(FieldList), new PropertyMetadata(Array.Empty<VariableItem>()));

    public static readonly DependencyProperty VariablesProperty = VariablesPropertyKey.DependencyProperty;

    /// <summary>Creates a variable of the report (parameter: its <see cref="VariableKind"/>, a parameter by default).</summary>
    public static RoutedCommand AddVariableCommand { get; } = new(nameof(AddVariableCommand), typeof(FieldList));

    /// <summary>Edits a variable; the parameter (or the data context of the source) is its <see cref="VariableItem"/>.</summary>
    public static RoutedCommand EditVariableCommand { get; } = new(nameof(EditVariableCommand), typeof(FieldList));

    /// <summary>Deletes a variable after a confirmation; same parameter as <see cref="EditVariableCommand"/>.</summary>
    public static RoutedCommand DeleteVariableCommand { get; } = new(nameof(DeleteVariableCommand), typeof(FieldList));

    /// <summary>Opens the editor of a new calculated measure of the <see cref="SelectedModel"/>.</summary>
    public static RoutedCommand AddMeasureCommand { get; } = new(nameof(AddMeasureCommand), typeof(FieldList));

    /// <summary>
    /// Edits a measure: a calculated measure of the report, or a measure of the model, which the report then redefines.
    /// The parameter (or the data context of the source) is its <see cref="ModelField"/>.
    /// </summary>
    public static RoutedCommand EditMeasureCommand { get; } = new(nameof(EditMeasureCommand), typeof(FieldList));

    /// <summary>Deletes a calculated measure after a confirmation; same parameter as <see cref="EditMeasureCommand"/>.</summary>
    public static RoutedCommand DeleteMeasureCommand { get; } = new(nameof(DeleteMeasureCommand), typeof(FieldList));

    /// <summary>
    /// Restores a measure of the model that the report redefines, after a confirmation; same parameter as
    /// <see cref="EditMeasureCommand"/>.
    /// </summary>
    public static RoutedCommand ResetMeasureCommand { get; } = new(nameof(ResetMeasureCommand), typeof(FieldList));

    static FieldList() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FieldList), new FrameworkPropertyMetadata(typeof(FieldList)));

    public FieldList()
    {
        CommandBindings.Add(new CommandBinding(AddMeasureCommand,
            (_, _) => { if (Context is { } context && SelectedModel is { } model) EditMeasure(new CalculatedMeasureEditor(context, model)); },
            (_, e) => e.CanExecute = CanAddMeasure));
        CommandBindings.Add(new CommandBinding(EditMeasureCommand,
            (_, e) => { if (Context is { } context && FieldOf(e) is { IsEditable: true } field) EditMeasure(new CalculatedMeasureEditor(context, field.Model, field.Definition)); },
            (_, e) => e.CanExecute = Context is not null && FieldOf(e) is { IsEditable: true }));
        CommandBindings.Add(new CommandBinding(DeleteMeasureCommand,
            (_, e) => { if (FieldOf(e) is { IsReportMeasure: true, Measure: { } measure }) DeleteMeasure(measure); },
            (_, e) => e.CanExecute = Context is not null && FieldOf(e) is { IsReportMeasure: true }));
        CommandBindings.Add(new CommandBinding(AddVariableCommand,
            (_, e) =>
            {
                if (Context is { } context)
                    VariableWindow.Create(context, Window.GetWindow(this), e.Parameter is VariableKind kind ? kind : VariableKind.Parameter, SelectedModel);
            },
            (_, e) => e.CanExecute = Context is not null));
        CommandBindings.Add(new CommandBinding(EditVariableCommand,
            (_, e) => { if (Context is { } context && VariableOf(e) is { } item) VariableWindow.Edit(context, item.Variable.Name, Window.GetWindow(this)); },
            (_, e) => e.CanExecute = Context is not null && VariableOf(e) is not null));
        CommandBindings.Add(new CommandBinding(DeleteVariableCommand,
            (_, e) => { if (VariableOf(e) is { } item) DeleteVariable(item.Variable); },
            (_, e) => e.CanExecute = Context is not null && VariableOf(e) is not null));
        CommandBindings.Add(new CommandBinding(ResetMeasureCommand,
            (_, e) => { if (FieldOf(e) is { IsRedefined: true } field) ResetMeasure(field); },
            (_, e) => e.CanExecute = Context is not null && FieldOf(e) is { IsRedefined: true }));
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

    /// <summary>The report's parameters, and its sets of values of the <see cref="SelectedModel"/>'s dimensions, matching the <see cref="Search"/>.</summary>
    public IReadOnlyList<VariableItem> Variables => (IReadOnlyList<VariableItem>)GetValue(VariablesProperty);

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
            if (_followed is not null)
            {
                WeakEventManager<MeasureContext, EventArgs>.RemoveHandler(_followed.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
                WeakEventManager<VariableContext, EventArgs>.RemoveHandler(_followed.Variables, nameof(VariableContext.Changed), OnMeasuresChanged);
            }
            _followed = Context;
            if (_followed is not null)
            {
                WeakEventManager<MeasureContext, EventArgs>.AddHandler(_followed.Measures, nameof(MeasureContext.Changed), OnMeasuresChanged);
                WeakEventManager<VariableContext, EventArgs>.AddHandler(_followed.Variables, nameof(VariableContext.Changed), OnMeasuresChanged);
            }
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
        var measures = Context?.Measures ?? new MeasureContext();
        SetValue(MeasuresPropertyKey, model is null ? Array.Empty<ModelField>() : measures.MeasuresOf(model.Metadata)
            .Select(f => new ModelField(model, f, measures.Find(model.Metadata.Name, f.Name)))
            .Where(f => TextMatch.Matches(f.Caption, search))
            .ToArray());
        SetValue(CanAddMeasurePropertyKey, model is not null && Context is not null);
        SetValue(VariablesPropertyKey, Context is not { } context ? Array.Empty<VariableItem>() : context.Variables.Variables
            .Where(v => v.Kind == VariableKind.Parameter || v.Model == model?.Metadata.Name)
            .Select(v => new VariableItem(v, Summary(context, v)))
            .Where(v => TextMatch.Matches(v.Caption, search))
            .ToArray());
        // The buttons of the template ask their commands again
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnMeasuresChanged(object? sender, EventArgs e) => Refresh();

    // The command parameter, or the data context of the command source (a button of its item)
    private static ModelField? FieldOf(RoutedEventArgs e) =>
        (e as ExecutedRoutedEventArgs)?.Parameter as ModelField
        ?? (e as CanExecuteRoutedEventArgs)?.Parameter as ModelField
        ?? (e.OriginalSource as FrameworkElement)?.DataContext as ModelField;

    private static VariableItem? VariableOf(RoutedEventArgs e) =>
        (e as ExecutedRoutedEventArgs)?.Parameter as VariableItem
        ?? (e as CanExecuteRoutedEventArgs)?.Parameter as VariableItem
        ?? (e.OriginalSource as FrameworkElement)?.DataContext as VariableItem;

    /// <summary>What a variable holds, in short: a parameter's value, a set's rule or number of values.</summary>
    internal static string Summary(EasyBIContext context, ReportVariable variable)
    {
        if (variable.Kind == VariableKind.Parameter) return VariableContext.Display(variable);
        if (variable.Rule is not { } rule) return Strings.Format(Strings.SetValueCount, variable.Members?.Count ?? 0);
        var model = context.Models.FirstOrDefault(m => m.Metadata.Name == variable.Model);
        var measure = model is null ? rule.Measure : context.Measures.GetField(model.Metadata, rule.Measure).Caption;
        var count = rule.CountVariable is { } name ? context.Variables.Find(name)?.DisplayName ?? name : rule.Count.ToString(EasyBILocalization.FormatCulture);
        return Strings.Format(rule.Bottom ? Strings.SetRuleBottomCount : Strings.SetRuleTopCount, count, measure);
    }

    private void DeleteVariable(ReportVariable variable)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!, Strings.Format(Strings.DeleteVariableConfirm, variable.DisplayName),
            Strings.DeleteVariable, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) Context?.Variables.Remove(variable.Name);
    }

    private void DeleteMeasure(CalculatedMeasure measure)
    {
        var answer = MessageBox.Show(Window.GetWindow(this)!, DeleteConfirmation(Context?.Measures ?? new MeasureContext(), measure),
            Strings.DeleteMeasure, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) Context?.Measures.Remove(measure.Model, measure.Name);
    }

    /// <summary>The question asked before deleting a measure, naming the measures whose formula uses it.</summary>
    internal static string DeleteConfirmation(MeasureContext measures, CalculatedMeasure measure) =>
        measures.Using(measure.Model, measure.Name) is { Count: > 0 } users
            ? Strings.Format(Strings.DeleteUsedMeasureConfirm, measure.Caption, string.Join(", ", users.Select(u => Strings.Format(Strings.QuotedCaption, u.Caption))))
            : Strings.Format(Strings.DeleteMeasureConfirm, measure.Caption);

    private void ResetMeasure(ModelField field)
    {
        var declared = field.Model.Metadata.GetField(field.Field.Name);
        var answer = MessageBox.Show(Window.GetWindow(this)!, Strings.Format(Strings.ResetMeasureConfirm, field.Caption, declared.Caption),
            Strings.ResetMeasure, MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK) Context?.Measures.Remove(field.Model.Metadata.Name, declared.Name);
    }

    private void EditMeasure(CalculatedMeasureEditor editor) =>
        new CalculatedMeasureWindow(editor) { Owner = Window.GetWindow(this) }.ShowDialog();
}
