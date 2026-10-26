using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf;

/// <summary>
/// Configuration shared by the designer and the visuals: available models,
/// plugged-in renderers and theme.
/// </summary>
public sealed class EasyBIContext
{
    private static readonly IVisualRenderer[] BuiltInRenderers = [new KpiRenderer(), new GaugeRenderer(), new TableRenderer(), new MatrixRenderer(), new TextRenderer(), new ImageRenderer()];

    private readonly List<ISemanticModel> _models = [];
    private readonly List<IVisualRenderer> _renderers = [];
    private IVisualRenderer? _activeRenderer;
    private BiTheme _theme = BiTheme.Default;

    public static EasyBIContext Create() => new();

    /// <summary>Raised when the active renderer or the theme changes: visuals redraw.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised by <see cref="RefreshData"/>: the visuals of the model query again.</summary>
    public event EventHandler<DataChangedEventArgs>? DataChanged;

    /// <summary>
    /// Raised before the tooltip of a chart opens, with EasyBI's content (following the visual's settings): change
    /// <see cref="TooltipOpeningEventArgs.Content"/> to show something else, or set it to null to show nothing.
    /// </summary>
    public event EventHandler<TooltipOpeningEventArgs>? TooltipOpening;

    /// <summary>
    /// Raised when a visual cannot be shown because its query or its renderer threw, before it shows the failure: log
    /// it, change the message, or hide the details from the end user (see <see cref="VisualFailedEventArgs"/>).
    /// </summary>
    public event EventHandler<VisualFailedEventArgs>? VisualFailed;

    public IReadOnlyList<ISemanticModel> Models => _models;

    /// <summary>How a failed visual shows its failure: EasyBI's message, then the application's changes (<see cref="VisualFailed"/>).</summary>
    internal VisualFailedEventArgs FailureFor(VisualDefinition visual, Exception exception, VisualFailureStage stage, string? renderer)
    {
        var args = new VisualFailedEventArgs(visual, exception, stage, renderer);
        VisualFailed?.Invoke(this, args);
        return args;
    }

    /// <summary>The content of a chart's tooltip: EasyBI's, then the application's changes (<see cref="TooltipOpening"/>).</summary>
    internal TooltipContent? TooltipFor(RenderRequest request, int category, IReadOnlyList<int> series)
    {
        var args = new TooltipOpeningEventArgs(request, category, series, Tooltips.Build(request, category, series));
        TooltipOpening?.Invoke(this, args);
        return args.Content;
    }

    /// <summary>
    /// The calculated measures the end user defined in the open report: field lists offer them with the model's
    /// measures, and visuals compute them. A <see cref="Dashboard.DashboardView"/> loads and saves them with its document.
    /// </summary>
    public MeasureContext Measures { get; } = new();

    /// <summary>
    /// The visual types the designer offers and the visuals follow: EasyBI's, and those the application adds
    /// (<see cref="AddVisualType"/>) or the renderers bring. Change it before showing the designer.
    /// </summary>
    public VisualTypeCatalog VisualTypes { get; } = new();

    public IReadOnlyList<IVisualRenderer> Renderers => _renderers;

    public IVisualRenderer? ActiveRenderer
    {
        get => _activeRenderer;
        set
        {
            if (_activeRenderer == value) return;
            _activeRenderer = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public BiTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public EasyBIContext AddModel(ISemanticModel model)
    {
        _models.Add(model);
        return this;
    }

    /// <summary>
    /// Declares a visual type of the application (see <see cref="VisualTypeCatalog.Add"/>); a renderer listing it in
    /// its <see cref="RendererCapabilities.Types"/> draws it.
    /// </summary>
    public EasyBIContext AddVisualType(VisualTypeDefinition definition)
    {
        VisualTypes.Add(definition);
        return this;
    }

    /// <summary>
    /// Plugs in a renderer; the first one becomes the <see cref="ActiveRenderer"/>. The visual types it brings
    /// (<see cref="RendererCapabilities.VisualTypes"/>) are declared, unless they already are.
    /// </summary>
    public EasyBIContext UseRenderer(IVisualRenderer renderer)
    {
        foreach (var definition in renderer.Capabilities.VisualTypes.Where(d => !VisualTypes.Contains(d.Type)))
            VisualTypes.Add(definition);
        _renderers.Add(renderer);
        _activeRenderer ??= renderer;
        return this;
    }

    /// <summary>
    /// Tells the visuals that the data behind a model changed (rows added, source replaced): they query again, although
    /// their query is the same. Without a model name, every visual queries again. Can be called from any thread.
    /// </summary>
    public void RefreshData(string? model = null) => DataChanged?.Invoke(this, new DataChangedEventArgs(model));

    /// <summary>
    /// The measures a user can pick in a model: the model's own, as the report redefines them, then the calculated
    /// measures of the report.
    /// </summary>
    public IEnumerable<FieldDescriptor> MeasuresOf(ISemanticModel model) => Measures.MeasuresOf(model.Metadata);

    /// <summary>A field of a model, as the report redefines it, or one of the report's calculated measures on it.</summary>
    public FieldDescriptor GetField(ISemanticModel model, string name) => Measures.GetField(model.Metadata, name);

    public ISemanticModel GetModel(string name) =>
        _models.FirstOrDefault(m => m.Metadata.Name == name)
        ?? throw new KeyNotFoundException($"Model '{name}' is not registered.");

    /// <summary>Checks that a loaded report only references registered models and fields.</summary>
    public IReadOnlyList<string> Validate(ReportDefinition report) =>
        ReportValidator.Validate(report, _models.Select(m => m.Metadata));

    /// <summary>
    /// Capability negotiation: the active renderer if it can draw the type, otherwise another
    /// plugged-in renderer, otherwise a built-in one (KPI, table, text, image).
    /// </summary>
    public IVisualRenderer? ResolveRenderer(VisualType type) => Resolve(new HashSet<VisualType> { type });

    /// <summary>
    /// Same negotiation for a whole visual: the renderer must support every type it uses, including the chart types
    /// of its measures (a chart mixing bars and a line needs both).
    /// </summary>
    public IVisualRenderer? ResolveRenderer(VisualDefinition visual) => Resolve(VisualPipeline.TypesOf(visual));

    private IVisualRenderer? Resolve(IReadOnlySet<VisualType> types)
    {
        if (_activeRenderer is not null && types.IsSubsetOf(_activeRenderer.Capabilities.Types)) return _activeRenderer;
        return _renderers.Concat(BuiltInRenderers).FirstOrDefault(r => types.IsSubsetOf(r.Capabilities.Types));
    }
}

/// <summary>Which model's data changed; null for every model.</summary>
public sealed class DataChangedEventArgs(string? model) : EventArgs
{
    public string? Model { get; } = model;
}

/// <summary>The tooltip about to open over a chart (see <see cref="EasyBIContext.TooltipOpening"/>).</summary>
public sealed class TooltipOpeningEventArgs(RenderRequest request, int category, IReadOnlyList<int> series, TooltipContent? content) : EventArgs
{
    /// <summary>What the chart draws: its visual, data and theme.</summary>
    public RenderRequest Request { get; } = request;

    public VisualDefinition Visual => Request.Visual;

    /// <summary>The hovered category, an index in <see cref="ChartData.Categories"/> (a waterfall's total is the last one + 1).</summary>
    public int Category { get; } = category;

    /// <summary>The hovered series, indexes in <see cref="ChartData.Series"/>.</summary>
    public IReadOnlyList<int> Series { get; } = series;

    /// <summary>What the tooltip shows; null shows no tooltip.</summary>
    public TooltipContent? Content { get; set; } = content;
}
