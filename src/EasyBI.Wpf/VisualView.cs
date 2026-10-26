using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Renderers;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// Displays a <see cref="VisualDefinition"/>: runs the query, then delegates
/// drawing to the renderer resolved by the context.
/// </summary>
/// <remarks>
/// The last result is cached: changing the renderer, theme, visual type or title redraws
/// without querying the source again. Charts take their colors from the <see cref="EasyBITheme"/> resources
/// (palette, text, grid lines, background), and redraw when the theme changes; a palette set on
/// <see cref="EasyBIContext.Theme"/> takes precedence over the resources'. With a <see cref="FilterContext"/> (inherited property,
/// set once on the dashboard container), the visual applies the global filters and becomes
/// interactive: clicking an element filters the other visuals.
/// </remarks>
public class VisualView : ContentControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context), typeof(EasyBIContext), typeof(VisualView),
        new PropertyMetadata(null, (d, e) => ((VisualView)d).OnContextChanged((EasyBIContext?)e.OldValue)));

    public static readonly DependencyProperty DefinitionProperty = DependencyProperty.Register(
        nameof(Definition), typeof(VisualDefinition), typeof(VisualView),
        new PropertyMetadata(null, (d, _) => ((VisualView)d).Refresh()));

    /// <summary>
    /// False holds the view's queries back (an off-screen visual of a dashboard): changes are only taken into account,
    /// with one query, once it is true again. True by default.
    /// </summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(VisualView),
        new PropertyMetadata(true, (d, e) => { if ((bool)e.NewValue) ((VisualView)d).Refresh(); }));

    public static readonly DependencyProperty ShowTitleProperty = DependencyProperty.Register(
        nameof(ShowTitle), typeof(bool), typeof(VisualView), new PropertyMetadata(true, (d, _) => ((VisualView)d).Draw()));

    /// <summary>Dashboard filters, inherited by all descendant elements.</summary>
    public static readonly DependencyProperty FilterContextProperty = DependencyProperty.RegisterAttached(
        nameof(FilterContext), typeof(FilterContext), typeof(VisualView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnFilterContextChanged));

    /// <summary>
    /// Raised (bubbling) when the user picks an element of the visual (a bar, a slice, a row), just before it changes the
    /// cross-filtering selection: containers use it to leave such a click to the visual (Ctrl+click on a dashboard).
    /// </summary>
    public static readonly RoutedEvent DataSelectedEvent = EventManager.RegisterRoutedEvent(
        "DataSelected", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(VisualView));

    // Follow the theme: these properties take the theme's resources, and redraw the chart when they change
    private static readonly DependencyProperty ForegroundProbeProperty = DependencyProperty.Register(
        "ForegroundProbe", typeof(object), typeof(VisualView), new PropertyMetadata(null, OnThemeChanged));

    private static readonly DependencyProperty PaletteProbeProperty = DependencyProperty.Register(
        "PaletteProbe", typeof(object), typeof(VisualView), new PropertyMetadata(null, OnThemeChanged));

    private CancellationTokenSource? _pending;
    private bool _redrawQueued;
    private RenderRequest? _last;
    private string? _lastQueryKey;

    public VisualView()
    {
        SetResourceReference(ForegroundProbeProperty, EasyBITheme.ForegroundBrushKey);
        SetResourceReference(PaletteProbeProperty, EasyBITheme.ChartPaletteKey);
        Loaded += (_, _) =>
        {
            Subscribe(Context, FilterContext);
            EasyBILocalization.CultureChanged += OnCultureChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            Unsubscribe(Context, FilterContext);
            EasyBILocalization.CultureChanged -= OnCultureChanged;
            if (_pending is not null) Cancel();
        };
    }

    public EasyBIContext? Context
    {
        get => (EasyBIContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    public VisualDefinition? Definition
    {
        get => (VisualDefinition?)GetValue(DefinitionProperty);
        set => SetValue(DefinitionProperty, value);
    }

    public bool ShowTitle
    {
        get => (bool)GetValue(ShowTitleProperty);
        set => SetValue(ShowTitleProperty, value);
    }

    public FilterContext? FilterContext
    {
        get => (FilterContext?)GetValue(FilterContextProperty);
        set => SetValue(FilterContextProperty, value);
    }

    /// <inheritdoc cref="DataSelectedEvent"/>
    public event RoutedEventHandler DataSelected
    {
        add => AddHandler(DataSelectedEvent, value);
        remove => RemoveHandler(DataSelectedEvent, value);
    }

    public static FilterContext? GetFilterContext(DependencyObject element) =>
        (FilterContext?)element.GetValue(FilterContextProperty);

    public static void SetFilterContext(DependencyObject element, FilterContext? value) =>
        element.SetValue(FilterContextProperty, value);

    /// <summary>Name of the renderer used for the last rendering (useful for debugging).</summary>
    public string? RenderedBy { get; private set; }

    private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Both probes change together when a theme is swapped: one redraw
        var view = (VisualView)d;
        if (view._redrawQueued) return;
        view._redrawQueued = true;
        view.Dispatcher.InvokeAsync(() =>
        {
            view._redrawQueued = false;
            view.Draw();
        });
    }

    private BiTheme ChartTheme(EasyBIContext context) => EasyBITheme.ChartTheme(this, context.Theme);

    private static void OnFilterContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var (old, current) = ((FilterContext?)e.OldValue, (FilterContext?)e.NewValue);
        if (d is VisualView view)
        {
            if (view.IsLoaded)
            {
                if (old is not null) old.Changed -= view.OnFiltersUpdated;
                if (current is not null) current.Changed += view.OnFiltersUpdated;
            }
            view.Refresh();
        }
    }

    private void OnContextChanged(EasyBIContext? old)
    {
        if (IsLoaded)
        {
            Unsubscribe(old, null);
            Subscribe(Context, null);
        }
        // Another context may expose different data under the same model name
        _lastQueryKey = null;
        Refresh();
    }

    private void Subscribe(EasyBIContext? context, FilterContext? filters)
    {
        if (context is not null)
        {
            context.Changed += OnContextUpdated;
            context.DataChanged += OnDataChanged;
            context.Measures.Changed += OnMeasuresUpdated;
        }
        if (filters is not null) filters.Changed += OnFiltersUpdated;
    }

    private void Unsubscribe(EasyBIContext? context, FilterContext? filters)
    {
        if (context is not null)
        {
            context.Changed -= OnContextUpdated;
            context.DataChanged -= OnDataChanged;
            context.Measures.Changed -= OnMeasuresUpdated;
        }
        if (filters is not null) filters.Changed -= OnFiltersUpdated;
    }

    // Renderer or theme: the data did not change, so only redraw.
    private void OnContextUpdated(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Draw);

    // The data changed behind the same query: forget the last one so that it runs again
    private void OnDataChanged(object? sender, DataChangedEventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        if (e.Model is not null && e.Model != Definition?.Model) return;
        _lastQueryKey = null;
        Refresh();
    });

    private void OnFiltersUpdated(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    // A calculated measure changed: only the visuals showing it get another query (it is part of the query key)
    private void OnMeasuresUpdated(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    // Captions and formatted values come with the result: the culture is part of the query key
    private void OnCultureChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Refresh);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Re-runs the query if it changed, otherwise redraws the last result; nothing while <see cref="IsActive"/> is false.</summary>
    public async void Refresh()
    {
        if (!IsLoaded || !IsActive || Context is not { } context || Definition is not { } visual)
            return;

        if (VisualValidator.Validate(visual, context.VisualTypes) is { } problem)
        {
            Cancel();
            Show(visual, Message(problem));
            return;
        }

        if (visual.Type.IsContent())
        {
            // Text and image: nothing to query
            Cancel();
            _last = VisualPipeline.PrepareContent(visual, ChartTheme(context));
            Draw();
            return;
        }

        var effective = FilterContext?.Apply(visual) ?? visual;
        var queryKey = JsonSerializer.Serialize(new
        {
            effective.Model,
            Query = effective.ToQuery(context.Measures.Measures, context.Models.FirstOrDefault(m => m.Metadata.Name == effective.Model)?.Metadata, context.VisualTypes),
            Culture = EasyBILocalization.UICulture.Name,
            Format = EasyBILocalization.FormatCulture.Name,
        }, ReportSerializer.Options);
        if (queryKey == _lastQueryKey)
        {
            // Same query (running or done): nothing to re-run
            Draw();
            return;
        }

        Cancel();
        var cts = _pending = new CancellationTokenSource();
        _lastQueryKey = queryKey;
        try
        {
            var model = context.GetModel(visual.Model);
            var request = await VisualPipeline.PrepareAsync(model, effective, ChartTheme(context), context.Measures.Measures, context.VisualTypes, cts.Token);
            if (cts.IsCancellationRequested) return;
            _last = request;
            _pending = null;
            Draw();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                _last = null;
                _lastQueryKey = null;
                ShowFailure(context, visual, ex, VisualFailureStage.Query, null);
            }
        }
        finally
        {
            if (_pending == cts) _pending = null;
            cts.Dispose();
        }
    }

    private void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
        _lastQueryKey = null;
    }

    /// <summary>Draws the last result with the current renderer and theme, without querying.</summary>
    private void Draw()
    {
        if (!IsLoaded || _pending is not null || _last is null || Context is not { } context || Definition is not { } visual)
            return;

        var renderer = context.ResolveRenderer(visual);
        if (renderer is null)
        {
            Show(visual, Message(Strings.Format(Strings.NoRenderer, visual.Type)));
            return;
        }

        var filters = FilterContext;
        var request = VisualPipeline.Redraw(_last, filters?.Apply(visual) ?? visual) with { Theme = ChartTheme(context) };
        if (filters is not null)
            request = request with
            {
                Highlight = filters.HighlightFor(request),
                // Ctrl+click adds to (or removes from) the selection of this visual
                Select = selection =>
                {
                    RaiseEvent(new RoutedEventArgs(DataSelectedEvent, this));
                    filters.Select(request, selection, ModifierKeysState.Ctrl);
                },
            };
        // Tooltips: EasyBI's content, with the application's changes
        var drawn = request;
        request = request with { Tooltip = (category, series) => context.TooltipFor(drawn, category, series) };
        // A long legend shows its entries by pages, turned by a pager over the chart
        var pager = visual.Type.IsCartesian() || visual.Type.IsCircular() ? new LegendPager() : null;
        request = request with { LegendPager = pager };

        try
        {
            RenderedBy = renderer.Name;
            var body = renderer.Render(request);
            Show(visual, pager is null ? body : new Grid { Children = { body, new ChartLegendPager { Pager = pager } } });
        }
        catch (Exception ex)
        {
            ShowFailure(context, visual, ex, VisualFailureStage.Render, renderer.Name);
        }
    }

    /// <summary>The failure the visual shows in place of its content; null while it shows its content or a message.</summary>
    internal VisualFailedEventArgs? Failure { get; private set; }

    /// <summary>
    /// A short message instead of the exception, with Retry (the query runs again) and, unless the application hides
    /// them, the details for a bug report.
    /// </summary>
    private void ShowFailure(EasyBIContext context, VisualDefinition visual, Exception exception, VisualFailureStage stage, string? renderer)
    {
        var failure = context.FailureFor(visual, exception, stage, renderer);
        var icon = new System.Windows.Shapes.Path
        {
            // A circle with an exclamation mark, cut out (even-odd)
            Data = Geometry.Parse("F0 M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z M11,6.5 L13,6.5 L13,13.5 L11,13.5 Z M11,15.5 L13,15.5 L13,17.5 L11,17.5 Z"),
            Width = 28,
            Height = 28,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, EasyBITheme.ErrorBrushKey);

        var message = Message(failure.Message);
        message.Margin = new Thickness(0, 0, 0, 10);
        message.VerticalAlignment = VerticalAlignment.Top;

        var retry = new Button { Content = Strings.VisualFailedRetry, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4) };
        retry.Click += (_, _) =>
        {
            _lastQueryKey = null;
            Refresh();
        };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        actions.Children.Add(retry);
        if (failure.ShowDetails)
        {
            var details = new Button { Content = Strings.VisualFailedDetails, Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(4) };
            details.Click += (_, _) => new VisualFailureWindow(failure) { Owner = Window.GetWindow(this) }.ShowDialog();
            actions.Children.Add(details);
        }

        var panel = new StackPanel { MaxWidth = 360, Margin = new Thickness(16) };
        panel.Children.Add(icon);
        panel.Children.Add(message);
        panel.Children.Add(actions);
        // A small dashboard tile shrinks the whole message rather than cutting its buttons
        Show(visual, new Viewbox { Child = panel, StretchDirection = StretchDirection.DownOnly });
        Failure = failure;
    }

    private void Show(VisualDefinition visual, UIElement body)
    {
        Failure = null;
        var root = new DockPanel { LastChildFill = true };
        if (ShowTitle && !string.IsNullOrWhiteSpace(visual.Title))
        {
            var title = new TextBlock
            {
                Text = visual.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 4, 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);
        }
        if (FilterContext is { } filters && filters.SelectionOf(visual.Id) is { } selection
            && Context?.Models.FirstOrDefault(m => m.Metadata.Name == selection.Model) is { } model)
        {
            var chip = SelectionChip(selection.Describe(model.Metadata), () => filters.ClearSelection(visual.Id));
            DockPanel.SetDock(chip, Dock.Top);
            root.Children.Add(chip);
        }
        root.Children.Add(body);
        Content = root;
    }

    private static FrameworkElement SelectionChip(string description, Action clearSelection)
    {
        var clear = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = Strings.ClearSelection,
        };
        clear.Click += (_, _) => clearSelection();
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        TextElement.SetFontSize(panel, 12);
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Format(Strings.Selection, description),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(clear);
        return new Border
        {
            Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(4, 0, 4, 6),
            Padding = new Thickness(10, 2, 4, 2),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
        }.Themed(Border.BackgroundProperty, EasyBITheme.AccentSoftBrushKey).Themed(Border.BorderBrushProperty, EasyBITheme.AccentSoftBorderBrushKey);
    }

    private static TextBlock Message(string text, ComponentResourceKey? brush = null) => new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(16),
    }.Themed(TextBlock.ForegroundProperty, brush ?? EasyBITheme.MutedForegroundBrushKey);
}

internal static class ThemeExtensions
{
    /// <summary>Binds a property to a theme resource (dynamically, so that it follows theme changes).</summary>
    public static T Themed<T>(this T element, DependencyProperty property, ComponentResourceKey key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
