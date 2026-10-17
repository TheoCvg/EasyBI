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
    private static readonly IVisualRenderer[] BuiltInRenderers = [new KpiRenderer(), new TableRenderer(), new TextRenderer(), new ImageRenderer()];

    private readonly List<ISemanticModel> _models = [];
    private readonly List<IVisualRenderer> _renderers = [];
    private IVisualRenderer? _activeRenderer;
    private BiTheme _theme = BiTheme.Default;

    public static EasyBIContext Create() => new();

    /// <summary>Raised when the active renderer or the theme changes: visuals redraw.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ISemanticModel> Models => _models;

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

    public EasyBIContext UseRenderer(IVisualRenderer renderer)
    {
        _renderers.Add(renderer);
        _activeRenderer ??= renderer;
        return this;
    }

    public ISemanticModel GetModel(string name) =>
        _models.FirstOrDefault(m => m.Metadata.Name == name)
        ?? throw new KeyNotFoundException($"Model '{name}' is not registered.");

    /// <summary>Checks that a loaded report only references registered models and fields.</summary>
    public IReadOnlyList<string> Validate(ReportDefinition report) =>
        ReportValidator.Validate(report, _models.Select(m => m.Metadata));

    /// <summary>
    /// Capability negotiation: the active renderer if it can draw the mark, otherwise another
    /// plugged-in renderer, otherwise a built-in one (KPI, table, text, image).
    /// </summary>
    public IVisualRenderer? ResolveRenderer(MarkType mark)
    {
        if (_activeRenderer?.Capabilities.Marks.Contains(mark) == true) return _activeRenderer;
        return _renderers.Concat(BuiltInRenderers).FirstOrDefault(r => r.Capabilities.Marks.Contains(mark));
    }
}
