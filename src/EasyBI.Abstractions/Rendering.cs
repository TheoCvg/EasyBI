namespace EasyBI.Abstractions;

/// <summary>Une série prête à dessiner : une valeur (éventuellement absente) par catégorie.</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values, string? Format);

/// <summary>
/// Données pré-pivotées pour les graphiques catégoriels. Tous les renderers reçoivent
/// exactement la même forme, ce qui garantit un rendu cohérent d'une librairie à l'autre.
/// </summary>
public sealed record ChartData(string? CategoryCaption, IReadOnlyList<string> Categories, IReadOnlyList<ChartSeries> Series);

/// <summary>Palette commune à tous les renderers (couleurs hexadécimales #RRGGBB).</summary>
public sealed record BiTheme(IReadOnlyList<string> Palette)
{
    public static BiTheme Default { get; } = new([
        "#2E6BE6", "#E8833A", "#2BA37B", "#D64550", "#8A5CD6",
        "#C9A227", "#3AA6C9", "#8C6D52", "#D46BAE", "#6B7A8F"]);

    public string ColorAt(int index) => Palette[index % Palette.Count];
}

/// <summary>Ce qu'un renderer sait faire. Le designer s'en sert pour griser les options indisponibles.</summary>
public sealed record RendererCapabilities(IReadOnlySet<MarkType> Marks);

/// <summary>Tout ce dont un renderer a besoin pour dessiner un visuel.</summary>
public sealed record RenderRequest(VisualDefinition Visual, ResultSet Result, ChartData Chart, BiTheme Theme);
