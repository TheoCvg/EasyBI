using EasyBI.Abstractions;
using EasyBI.Wpf.Renderers;

namespace EasyBI.Wpf;

/// <summary>
/// Configuration partagée par le designer et les visuels : modèles disponibles,
/// renderers branchés et thème.
/// </summary>
public sealed class EasyBIContext
{
    private static readonly IVisualRenderer[] BuiltInRenderers = [new KpiRenderer(), new TableRenderer()];

    private readonly List<ISemanticModel> _models = [];
    private readonly List<IVisualRenderer> _renderers = [];
    private IVisualRenderer? _activeRenderer;
    private BiTheme _theme = BiTheme.Default;

    public static EasyBIContext Create() => new();

    /// <summary>Déclenché quand le renderer actif ou le thème change : les visuels se redessinent.</summary>
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
        ?? throw new KeyNotFoundException($"Modèle '{name}' non enregistré.");

    /// <summary>
    /// Négociation des capacités : renderer actif s'il sait faire, sinon un autre renderer branché,
    /// sinon un renderer intégré (KPI, table).
    /// </summary>
    public IVisualRenderer? ResolveRenderer(MarkType mark)
    {
        if (_activeRenderer?.Capabilities.Marks.Contains(mark) == true) return _activeRenderer;
        return _renderers.Concat(BuiltInRenderers).FirstOrDefault(r => r.Capabilities.Marks.Contains(mark));
    }
}
