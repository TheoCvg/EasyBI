using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// Shows a variable of the report (<see cref="EasyBIContext.Variables"/>) and lets the end user change a parameter. As
/// its formula says (see <see cref="VariableFormula"/>), a parameter shows a slider (<c>10 BETWEEN 3 AND 25</c>), a
/// list of its choices (<c>"Web" IN ("Web"; "Store")</c>), a date picker, a check box, or a box; a computed value shows
/// its value, a set its values. <see cref="VisualType.Variable"/> visuals show one; an application can place one anywhere.
/// </summary>
/// <remarks>
/// Standard controls get no explicit style, so that the application's control library applies. A change of the value
/// from elsewhere updates the controls without rebuilding them; a slider sets its value once the user pauses.
/// </remarks>
public class VariableView : ContentControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(VariableView),
        new PropertyMetadata(null, (d, e) => ((VariableView)d).OnContextChanged((EasyBIContext?)e.OldValue)));

    /// <summary>The name of the variable shown.</summary>
    public static readonly DependencyProperty VariableProperty = DependencyProperty.Register(
        nameof(Variable), typeof(string), typeof(VariableView), new PropertyMetadata(null, (d, _) => ((VariableView)d).Update(rebuild: true)));

    /// <summary>The dashboard's filters (inherited): a set or a computed value follows its report filters.</summary>
    public static readonly DependencyProperty FilterContextProperty = VisualView.FilterContextProperty.AddOwner(
        typeof(VariableView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits,
            (d, e) => ((VariableView)d).OnFilterContextChanged((FilterContext?)e.OldValue)));

    /// <summary>How long a slider waits once the user pauses before setting the value (the visuals using it query again).</summary>
    public static TimeSpan SliderDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    private ReportVariable? _built;
    private Action<ReportVariable>? _sync;
    private bool _syncing;
    private CancellationTokenSource? _loading;
    private DispatcherTimer? _pending;

    public VariableView()
    {
        Focusable = false;
        Loaded += (_, _) =>
        {
            Subscribe(Context);
            if (FilterContext is { } filters) filters.Changed += OnFiltersChanged;
            Update(rebuild: true);
        };
        Unloaded += (_, _) =>
        {
            Unsubscribe(Context);
            if (FilterContext is { } filters) filters.Changed -= OnFiltersChanged;
            _loading?.Cancel();
            Commit();
        };
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public string? Variable
    {
        get => (string?)GetValue(VariableProperty);
        set => SetValue(VariableProperty, value);
    }

    public FilterContext? FilterContext
    {
        get => (FilterContext?)GetValue(FilterContextProperty);
        set => SetValue(FilterContextProperty, value);
    }

    /// <summary>The values of the set, or the computed value, shown as last loaded (tests read them).</summary>
    internal IReadOnlyList<string> Members { get; private set; } = [];

    /// <summary>The values of the set being loaded (tests wait for it).</summary>
    internal Task MembersTask { get; private set; } = Task.CompletedTask;

    private void OnContextChanged(EasyBIContext? old)
    {
        if (IsLoaded)
        {
            Unsubscribe(old);
            Subscribe(Context);
        }
        Update(rebuild: true);
    }

    private void OnFilterContextChanged(FilterContext? old)
    {
        if (!IsLoaded) return;
        if (old is not null) old.Changed -= OnFiltersChanged;
        if (FilterContext is { } filters) filters.Changed += OnFiltersChanged;
        Update(rebuild: false);
    }

    private void Subscribe(EasyBIContext? context)
    {
        if (context is null) return;
        context.Variables.Changed += OnVariablesChanged;
        context.DataChanged += OnDataChanged;
    }

    private void Unsubscribe(EasyBIContext? context)
    {
        if (context is null) return;
        context.Variables.Changed -= OnVariablesChanged;
        context.DataChanged -= OnDataChanged;
    }

    private void OnVariablesChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => Update(rebuild: false));

    private void OnDataChanged(object? sender, DataChangedEventArgs e) => Dispatcher.InvokeAsync(() => Update(rebuild: false));

    // The report filters change the values of a set or a computed value
    private void OnFiltersChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => Update(rebuild: false));

    /// <summary>Shows the variable: the same controls with its new value while its formula stays.</summary>
    private void Update(bool rebuild)
    {
        if (!IsLoaded) return;
        if (Context is not { } context || Variable is not { } name || context.Variables.Find(name) is not { } variable)
        {
            _built = null;
            Content = Message(Variable is null ? "" : Strings.Format(Strings.VariableMissing, Variable));
            return;
        }
        if (rebuild || _built is null || Shape(_built) != Shape(variable))
        {
            _built = variable;
            Content = VariableFormula.Definition(variable) is ParameterDefinition parameter
                ? BuildParameter(context, variable, parameter)
                : BuildValues(variable);
        }
        else
        {
            _built = variable;
            _syncing = true;
            try
            {
                _sync?.Invoke(variable);
            }
            finally
            {
                _syncing = false;
            }
        }
        if (VariableFormula.Definition(variable).Kind != VariableKind.Parameter) MembersTask = LoadMembersAsync(context, variable);
    }

    // What the controls are built from: everything but the value
    private static ReportVariable Shape(ReportVariable variable) => variable with { Value = null };

    // --- Parameters ---

    private FrameworkElement BuildParameter(EasyBIContext context, ReportVariable parameter, ParameterDefinition definition)
    {
        var panel = new StackPanel();
        panel.Children.Add(Caption(parameter.Name));
        panel.Children.Add(definition switch
        {
            { DataType: DataType.Number, Minimum: { } min, Maximum: { } max } => Slider(context, parameter, min, max, definition.Step),
            { Choices: { Count: > 0 } choices } => Choices(context, parameter, definition, choices),
            { DataType: DataType.Date } => DatePicker(context, parameter),
            { DataType: DataType.Boolean } => CheckBox(context, parameter),
            _ => TextBox(context, parameter, definition.DataType),
        });
        return panel;
    }

    private FrameworkElement Slider(EasyBIContext context, ReportVariable parameter, double min, double max, double? step)
    {
        step = step is > 0 ? step : 1;
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step.Value,
            LargeChange = step.Value,
            TickFrequency = step.Value,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Value = Number(VariableContext.ValueOf(parameter)) ?? min,
        };
        var shown = new TextBlock { MinWidth = 40, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        void Show(double value) => shown.Text = VariableContext.Display(Invariant(value), DataType.Number, parameter.Format);
        Show(slider.Value);
        slider.ValueChanged += (_, e) =>
        {
            Show(e.NewValue);
            if (!_syncing) Schedule(() => context.Variables.SetValue(parameter.Name, Invariant(e.NewValue)));
        };
        _sync = variable =>
        {
            slider.Value = Number(VariableContext.ValueOf(variable)) ?? min;
            Show(slider.Value);
        };
        var row = new DockPanel();
        DockPanel.SetDock(shown, Dock.Right);
        row.Children.Add(shown);
        row.Children.Add(slider);
        return row;
    }

    private FrameworkElement DatePicker(EasyBIContext context, ReportVariable parameter)
    {
        var picker = new DatePicker { SelectedDate = Date(VariableContext.ValueOf(parameter)), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
        picker.SelectedDateChanged += (_, _) =>
        {
            if (!_syncing) context.Variables.SetValue(parameter.Name, picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        };
        _sync = variable => picker.SelectedDate = Date(VariableContext.ValueOf(variable));
        return picker;
    }

    private FrameworkElement CheckBox(EasyBIContext context, ReportVariable parameter)
    {
        var box = new CheckBox { Content = parameter.Name, IsChecked = VariableContext.ValueOf(parameter) == bool.TrueString };
        box.Click += (_, _) => context.Variables.SetValue(parameter.Name, box.IsChecked == true ? bool.TrueString : bool.FalseString);
        _sync = variable => box.IsChecked = VariableContext.ValueOf(variable) == bool.TrueString;
        return box;
    }

    // The choices shown formatted, set in invariant form
    private FrameworkElement Choices(EasyBIContext context, ReportVariable parameter, ParameterDefinition definition, IReadOnlyList<string> choices)
    {
        var options = choices.Select(c => new Option<string>(c, () => VariableContext.Display(c, definition.DataType, parameter.Format))).ToArray();
        Option<string>? Selected(ReportVariable variable) => options.FirstOrDefault(o => o.Value == VariableContext.ValueOf(variable));
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(Option<string>.Label), SelectedItem = Selected(parameter), MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_syncing && combo.SelectedItem is Option<string> choice) context.Variables.SetValue(parameter.Name, choice.Value);
        };
        _sync = variable => combo.SelectedItem = Selected(variable);
        return combo;
    }

    // A number without bounds, or a text: set when the user presses Enter or leaves the box
    private FrameworkElement TextBox(EasyBIContext context, ReportVariable parameter, DataType type)
    {
        var isNumber = type == DataType.Number;
        string Shown(ReportVariable variable) => isNumber ? VariableContext.Display(VariableContext.ValueOf(variable), type) : VariableContext.ValueOf(variable) ?? "";
        var box = new TextBox { Text = Shown(parameter), MinWidth = 80, Padding = new Thickness(4, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };
        void Apply()
        {
            var current = context.Variables.Find(parameter.Name) ?? parameter;
            if (!isNumber) context.Variables.SetValue(parameter.Name, box.Text);
            else if (box.Text.Trim().Length == 0) context.Variables.SetValue(parameter.Name, null);
            else if (double.TryParse(box.Text, NumberStyles.Float | NumberStyles.AllowThousands, EasyBILocalization.FormatCulture, out var number))
                context.Variables.SetValue(parameter.Name, Invariant(number));
            else box.Text = Shown(current);
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Apply();
        };
        box.LostKeyboardFocus += (_, _) => Apply();
        _sync = variable => box.Text = Shown(variable);
        return box;
    }

    // --- Sets and computed values ---

    // The values, loaded by LoadMembersAsync
    private FrameworkElement BuildValues(ReportVariable variable)
    {
        _sync = null;
        var root = new DockPanel();
        var header = Caption(variable.Name);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var list = new ItemsControl { Focusable = false };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        list.Template = new ControlTemplate(typeof(ItemsControl))
        {
            VisualTree = Factory<ScrollViewer>(f =>
            {
                f.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                f.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                f.SetValue(ScrollViewer.CanContentScrollProperty, true);
                f.SetValue(FocusableProperty, false);
                f.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            }),
        };
        // A computed value reads as a figure
        var isValue = VariableFormula.Definition(variable).Kind == VariableKind.Computed;
        list.ItemTemplate = new DataTemplate { VisualTree = Factory<TextBlock>(f =>
        {
            f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            f.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            f.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));
            if (isValue) f.SetValue(TextBlock.FontSizeProperty, 20d);
        }) };
        _members = list;
        root.Children.Add(list);
        return root;
    }

    private ItemsControl? _members;

    private async Task LoadMembersAsync(EasyBIContext context, ReportVariable variable)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        try
        {
            var source = context.SetSource(FilterContext);
            IReadOnlyList<string> shown;
            if (VariableFormula.Definition(variable).Kind == VariableKind.Set)
            {
                var members = await context.Variables.MembersAsync(variable.Name, source, cts.Token);
                var metadata = context.Models.FirstOrDefault(m => m.Metadata.Name == variable.Model)?.Metadata;
                shown = [.. members.Select(m => VariableContext.DisplayMember(m, variable, metadata))];
            }
            else shown = [await context.Variables.DisplayAsync(variable.Name, source, cts.Token)];
            if (cts.IsCancellationRequested) return;
            Members = shown;
            if (_members is not null) _members.ItemsSource = Members;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested && _members is not null) _members.ItemsSource = new[] { ex.Message };
        }
    }

    // --- Helpers ---

    private void Schedule(Action commit)
    {
        _pending?.Stop();
        _pending = new DispatcherTimer(SliderDelay, DispatcherPriority.Normal, (_, _) => Commit(), Dispatcher) { Tag = commit };
        _pending.Start();
    }

    // Sets a pending value now (the slider was left, or the view unloads)
    private void Commit()
    {
        if (_pending is not { } pending) return;
        pending.Stop();
        _pending = null;
        (pending.Tag as Action)?.Invoke();
    }

    /// <summary>Sets the value a slider is waiting to set (tests do not wait for the delay).</summary>
    internal void CommitPending() => Commit();

    private static FrameworkElementFactory Factory<T>(Action<FrameworkElementFactory> setup)
    {
        var factory = new FrameworkElementFactory(typeof(T));
        setup(factory);
        return factory;
    }

    private static TextBlock Caption(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        Margin = new Thickness(0, 0, 0, 4),
        TextTrimming = TextTrimming.CharacterEllipsis,
    }.Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);

    private static TextBlock Message(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
        .Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);

    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static DateTime? Date(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null;
}
