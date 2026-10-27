using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// Filter bar of a dashboard: global filters (slicers) applied to every visual of the same
/// model, plus a reminder of the current selection (cross-filter) with a way to clear it.
/// It uses the inherited <see cref="FilterContext"/>, as <see cref="VisualView"/> does.
/// </summary>
/// <remarks>
/// Lookless: the default template (in <c>Themes/FilterBar.xaml</c>) shows <see cref="Slicers"/> and
/// <see cref="SelectionDescription"/>, declares the <see cref="FilterBarSlots"/> and acts through
/// <see cref="AddFilterCommand"/>, <see cref="RemoveFilterCommand"/> and <see cref="ClearSelectionCommand"/>.
/// </remarks>
public class FilterBar : Control
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(FilterBar),
        new PropertyMetadata(null, (d, _) => ((FilterBar)d).Sync()));

    public static readonly DependencyProperty FilterContextProperty = VisualView.FilterContextProperty.AddOwner(
        typeof(FilterBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits,
            (d, e) => ((FilterBar)d).OnFilterContextChanged((FilterContext?)e.OldValue, (FilterContext?)e.NewValue)));

    private static readonly DependencyPropertyKey SelectionDescriptionPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectionDescription), typeof(string), typeof(FilterBar), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionDescriptionProperty = SelectionDescriptionPropertyKey.DependencyProperty;

    /// <summary>Opens the menu of the dimensions to filter on, under the command source.</summary>
    public static RoutedCommand AddFilterCommand { get; } = new(nameof(AddFilterCommand), typeof(FilterBar));

    /// <summary>Removes a global filter: the parameter (or the data context of the source) is its <see cref="SlicerViewModel"/>.</summary>
    public static RoutedCommand RemoveFilterCommand { get; } = new(nameof(RemoveFilterCommand), typeof(FilterBar));

    /// <summary>Clears the current selection (cross-filter).</summary>
    public static RoutedCommand ClearSelectionCommand { get; } = new(nameof(ClearSelectionCommand), typeof(FilterBar));

    private readonly ObservableCollection<SlicerViewModel> _slicers = [];

    static FilterBar() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FilterBar), new FrameworkPropertyMetadata(typeof(FilterBar)));

    public FilterBar()
    {
        Slicers = new ReadOnlyObservableCollection<SlicerViewModel>(_slicers);
        CommandBindings.Add(new CommandBinding(AddFilterCommand,
            (_, e) => OpenAddMenu(e.OriginalSource as UIElement ?? this),
            (_, e) => e.CanExecute = FilterContext is not null && Context is not null));
        CommandBindings.Add(new CommandBinding(RemoveFilterCommand,
            (_, e) => { if (SlicerOf(e) is { } slicer) Remove(slicer); },
            (_, e) => e.CanExecute = SlicerOf(e) is not null));
        CommandBindings.Add(new CommandBinding(ClearSelectionCommand,
            (_, _) => FilterContext?.ClearSelection(),
            (_, e) => e.CanExecute = FilterContext?.Selections.Count > 0));
        Loaded += (_, _) =>
        {
            if (FilterContext is { } filters) filters.Changed += OnFiltersUpdated;
            Sync();
        };
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
        Unloaded += (_, _) =>
        {
            if (FilterContext is { } filters) filters.Changed -= OnFiltersUpdated;
        };
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public FilterContext? FilterContext
    {
        get => (FilterContext?)GetValue(FilterContextProperty);
        set => SetValue(FilterContextProperty, value);
    }

    /// <summary>The global filters shown, an empty one included until it is removed.</summary>
    public ReadOnlyObservableCollection<SlicerViewModel> Slicers { get; }

    /// <summary>The current selection (cross-filter) in words, or null when nothing is selected.</summary>
    public string? SelectionDescription => (string?)GetValue(SelectionDescriptionProperty);

    private void OnFilterContextChanged(FilterContext? old, FilterContext? current)
    {
        if (IsLoaded)
        {
            if (old is not null) old.Changed -= OnFiltersUpdated;
            if (current is not null) current.Changed += OnFiltersUpdated;
        }
        Sync();
    }

    private void OnFiltersUpdated(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Sync);

    // Slicer captions and value labels are built once: rebuild them in the new language
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        _slicers.Clear();
        Sync();
    });

    /// <summary>
    /// Brings the display in line with the <see cref="FilterContext"/>. Slicers are only rebuilt when
    /// the filters changed elsewhere (a report was opened): an empty slicer stays visible.
    /// </summary>
    private void Sync()
    {
        var filters = FilterContext;
        SetValue(SelectionDescriptionPropertyKey,
            filters is not { Selections.Count: > 0 } ? null
            : filters.DescribeSelections(model => Context?.Models.FirstOrDefault(m => m.Metadata.Name == model)?.Metadata) is { Length: > 0 } description
                ? Strings.Format(Strings.Selection, description)
                : "");
        CommandManager.InvalidateRequerySuggested();

        if (filters is null || Context is not { } context)
        {
            _slicers.Clear();
            return;
        }

        var shown = _slicers.Select(s => s.ToReportFilter()).OfType<ReportFilter>().ToList();
        if (shown.Count == filters.Filters.Count && shown.Zip(filters.Filters).All(p => Same(p.First, p.Second)))
            return;

        _slicers.Clear();
        foreach (var filter in filters.Filters)
        {
            if (context.Models.FirstOrDefault(m => m.Metadata.Name == filter.Model) is not { } model
                || model.Metadata.Fields.FirstOrDefault(f => f.Name == filter.Filter.Field) is not { } field)
                continue;
            _slicers.Add(CreateSlicer(model, field, filter.Filter.Values, filter.Filter.Operator, filter.Filter.Grain));
        }
    }

    private SlicerViewModel CreateSlicer(ISemanticModel model, FieldDescriptor field, IEnumerable<string?>? values, FilterOperator @operator, DateGrain grain = DateGrain.None)
    {
        SlicerViewModel? slicer = null;
        var caption = Context!.Models.Count > 1 ? $"{model.Metadata.Caption} › {field.Caption}" : field.Caption;
        slicer = new SlicerViewModel(model.Metadata.Name, caption,
            new FilterItemViewModel(field, model, () => Apply(slicer!), values, @operator, grain, Context.Variables));
        return slicer;
    }

    private void Apply(SlicerViewModel slicer)
    {
        var filters = FilterContext;
        if (filters is null) return;
        if (slicer.ToReportFilter() is { } filter) filters.SetFilter(filter.Model, filter.Filter);
        else filters.RemoveFilter(slicer.Model, slicer.Filter.Field.Name);
    }

    private void OpenAddMenu(UIElement target)
    {
        if (Context is not { } context) return;
        var menu = new ContextMenu { PlacementTarget = target };
        foreach (var model in context.Models)
        {
            var fields = model.Metadata.Dimensions
                .Where(f => !_slicers.Any(s => s.Model == model.Metadata.Name && s.Filter.Field == f));
            var parent = context.Models.Count > 1 ? new MenuItem { Header = model.Metadata.Caption } : null;
            foreach (var field in fields)
            {
                var item = new MenuItem { Header = field.Caption };
                item.Click += (_, _) => _slicers.Add(CreateSlicer(model, field, null, FilterOperator.In));
                if (parent is not null) parent.Items.Add(item);
                else menu.Items.Add(item);
            }
            if (parent is { HasItems: true }) menu.Items.Add(parent);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = Strings.NoDimensionAvailable, IsEnabled = false });
        menu.IsOpen = true;
    }

    private void Remove(SlicerViewModel slicer)
    {
        _slicers.Remove(slicer);
        FilterContext?.RemoveFilter(slicer.Model, slicer.Filter.Field.Name);
    }

    private static SlicerViewModel? SlicerOf(RoutedEventArgs e) =>
        (e as ExecutedRoutedEventArgs)?.Parameter as SlicerViewModel
        ?? (e as CanExecuteRoutedEventArgs)?.Parameter as SlicerViewModel
        ?? (e.OriginalSource as FrameworkElement)?.DataContext as SlicerViewModel;

    private static bool Same(ReportFilter a, ReportFilter b) =>
        a.Model == b.Model && a.Filter.Field == b.Filter.Field && a.Filter.Operator == b.Filter.Operator
        && a.Filter.Grain == b.Filter.Grain && a.Filter.Values.SequenceEqual(b.Filter.Values);
}

/// <summary>A global filter of the bar: a field of a model and its checked values.</summary>
public sealed class SlicerViewModel(string model, string caption, FilterItemViewModel filter)
{
    public string Model { get; } = model;

    public string Caption { get; } = caption;

    public FilterItemViewModel Filter { get; } = filter;

    public ReportFilter? ToReportFilter() => Filter.ToFilterSpec() is { } spec ? new ReportFilter(Model, spec) : null;
}
