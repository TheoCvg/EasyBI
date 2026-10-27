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
/// Shows a variable of the report (<see cref="EasyBIContext.Variables"/>) and lets the end user change it. A parameter
/// shows a slider (a number between a minimum and a maximum), a number box, a date picker, a check box, a list of its
/// choices or a text box; a set shows its values, the number of values its rule keeps, and the values to check for a
/// fixed set. <see cref="VisualType.Variable"/> visuals show one; an application can place one anywhere.
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

    /// <summary>The dashboard's filters (inherited): a set chosen by a rule follows its report filters.</summary>
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

    /// <summary>The values of the set shown, as last loaded (tests read them).</summary>
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

    // The report filters change the values of a set chosen by a rule
    private void OnFiltersChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => Update(rebuild: false));

    /// <summary>Shows the variable: the same controls with its new value while it keeps its kind and settings.</summary>
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
            Content = variable.Kind == VariableKind.Set ? BuildSet(context, variable) : BuildParameter(context, variable);
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
        if (variable.Kind == VariableKind.Set) MembersTask = LoadMembersAsync(context, variable);
    }

    // What the controls are built from: everything but the value (and a rule's count, which a box edits)
    private static ReportVariable Shape(ReportVariable variable) =>
        variable with { Value = null, Members = null, Rule = variable.Rule is { } rule ? rule with { Count = 0 } : null };

    // --- Parameters ---

    private FrameworkElement BuildParameter(EasyBIContext context, ReportVariable parameter)
    {
        var panel = new StackPanel();
        panel.Children.Add(Caption(parameter.DisplayName));
        panel.Children.Add(parameter.DataType switch
        {
            DataType.Number when parameter is { Minimum: { } min, Maximum: { } max } && max > min => Slider(context, parameter, min, max),
            DataType.Date => DatePicker(context, parameter),
            DataType.Boolean => CheckBox(context, parameter),
            DataType.Text when parameter.Choices is { Count: > 0 } choices => Choices(context, parameter, choices),
            _ => TextBox(context, parameter),
        });
        return panel;
    }

    private FrameworkElement Slider(EasyBIContext context, ReportVariable parameter, double min, double max)
    {
        var step = parameter.Step is > 0 and var s ? s : 1;
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            SmallChange = step,
            LargeChange = step,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Value = Number(parameter.Value) ?? min,
        };
        var shown = new TextBlock { MinWidth = 40, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        void Show(double value) => shown.Text = VariableContext.Display(parameter with { Value = Invariant(value) });
        Show(slider.Value);
        slider.ValueChanged += (_, e) =>
        {
            Show(e.NewValue);
            if (!_syncing) Schedule(() => context.Variables.SetValue(parameter.Name, Invariant(e.NewValue)));
        };
        _sync = variable =>
        {
            slider.Value = Number(variable.Value) ?? min;
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
        var picker = new DatePicker { SelectedDate = Date(parameter.Value), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 140 };
        picker.SelectedDateChanged += (_, _) =>
        {
            if (!_syncing) context.Variables.SetValue(parameter.Name, picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        };
        _sync = variable => picker.SelectedDate = Date(variable.Value);
        return picker;
    }

    private FrameworkElement CheckBox(EasyBIContext context, ReportVariable parameter)
    {
        var box = new CheckBox { Content = parameter.DisplayName, IsChecked = parameter.Value == bool.TrueString };
        box.Click += (_, _) => context.Variables.SetValue(parameter.Name, box.IsChecked == true ? bool.TrueString : bool.FalseString);
        _sync = variable => box.IsChecked = variable.Value == bool.TrueString;
        return box;
    }

    private FrameworkElement Choices(EasyBIContext context, ReportVariable parameter, IReadOnlyList<string> choices)
    {
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = parameter.Value, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectionChanged += (_, _) =>
        {
            if (!_syncing && combo.SelectedItem is string choice) context.Variables.SetValue(parameter.Name, choice);
        };
        _sync = variable => combo.SelectedItem = variable.Value;
        return combo;
    }

    // A number without bounds, or a text: set when the user presses Enter or leaves the box
    private FrameworkElement TextBox(EasyBIContext context, ReportVariable parameter)
    {
        var isNumber = parameter.DataType == DataType.Number;
        string Shown(ReportVariable variable) => isNumber ? VariableContext.Display(variable with { Format = null }) : variable.Value ?? "";
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

    // --- Sets ---

    private FrameworkElement BuildSet(EasyBIContext context, ReportVariable set)
    {
        var root = new DockPanel();
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(Caption(set.DisplayName));
        var model = context.Models.FirstOrDefault(m => m.Metadata.Name == set.Model);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) }.Themed(TextBlock.ForegroundProperty, EasyBITheme.MutedForegroundBrushKey);
        if (set.Rule is { } rule)
        {
            var measure = model is null ? rule.Measure : context.Measures.GetField(model.Metadata, rule.Measure).Caption;
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            if (rule.CountVariable is null)
            {
                // The count of a rule can be changed here; a parameter giving it has its own view
                var count = new TextBox { Text = rule.Count.ToString(EasyBILocalization.FormatCulture), Width = 48, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 6, 0) };
                void Apply()
                {
                    if (context.Variables.Find(set.Name) is not { Rule: { } current } latest) return;
                    if (int.TryParse(count.Text, NumberStyles.Integer, EasyBILocalization.FormatCulture, out var n) && n >= 0 && n != current.Count)
                        context.Variables.Set(latest with { Rule = current with { Count = n } });
                    else count.Text = current.Count.ToString(EasyBILocalization.FormatCulture);
                }
                count.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Apply();
                };
                count.LostKeyboardFocus += (_, _) => Apply();
                line.Children.Add(count);
                summary.Text = Strings.Format(rule.Bottom ? Strings.SetRuleBottomBy : Strings.SetRuleTopBy, measure);
                summary.VerticalAlignment = VerticalAlignment.Center;
                summary.Margin = new Thickness();
                _sync = variable =>
                {
                    if (variable.Rule is { } changed) count.Text = changed.Count.ToString(EasyBILocalization.FormatCulture);
                };
            }
            else
            {
                var countBy = context.Variables.Find(rule.CountVariable)?.DisplayName ?? rule.CountVariable;
                summary.Text = Strings.Format(rule.Bottom ? Strings.SetRuleBottomCount : Strings.SetRuleTopCount, countBy, measure);
                _sync = null;
            }
            line.Children.Add(summary);
            header.Children.Add(line);
        }
        else if (model is not null)
        {
            // A fixed set: its values are checked like a filter's
            var choose = new ToggleButton { Content = Strings.SetChooseValues, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
            var popup = new Popup { PlacementTarget = choose, StaysOpen = false, AllowsTransparency = true };
            choose.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(Popup.IsOpen)) { Source = popup, Mode = System.Windows.Data.BindingMode.TwoWay });
            FilterItemViewModel? editing = null;
            popup.Opened += (_, _) =>
            {
                var current = context.Variables.Find(set.Name) ?? set;
                editing = new FilterItemViewModel(model.Metadata.GetField(set.Field), model, () => { }, current.Members ?? []);
                popup.Child = new Border
                {
                    Padding = new Thickness(10),
                    BorderThickness = new Thickness(1),
                    Child = new FilterEditor { DataContext = editing },
                }.Themed(Border.BackgroundProperty, EasyBITheme.SurfaceBrushKey).Themed(Border.BorderBrushProperty, EasyBITheme.BorderBrushKey);
            };
            popup.Closed += (_, _) =>
            {
                if (editing is { IsLoading: false } closed && context.Variables.Find(set.Name) is { } latest)
                    context.Variables.Set(latest with { Members = CheckedValues(closed) });
                editing = null;
            };
            header.Children.Add(choose);
            header.Children.Add(popup);
            _sync = null;
        }
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
        list.ItemTemplate = new DataTemplate { VisualTree = Factory<TextBlock>(f =>
        {
            f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            f.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            f.SetValue(MarginProperty, new Thickness(0, 1, 0, 1));
        }) };
        _members = list;
        root.Children.Add(list);
        return root;
    }

    private ItemsControl? _members;

    /// <summary>The values a fixed set keeps from a list of values checked as a filter's.</summary>
    internal static IReadOnlyList<string?> CheckedValues(FilterItemViewModel values) =>
        [.. values.Values.Where(v => v.IsChecked).Select(v => InvariantValue.Format(v.Value))];

    private async Task LoadMembersAsync(EasyBIContext context, ReportVariable set)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        try
        {
            var members = await context.Variables.MembersAsync(set.Name, context.SetSource(FilterContext), cts.Token);
            if (cts.IsCancellationRequested) return;
            var metadata = context.Models.FirstOrDefault(m => m.Metadata.Name == set.Model)?.Metadata;
            Members = [.. members.Select(m => VariableContext.DisplayMember(m, set, metadata))];
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
