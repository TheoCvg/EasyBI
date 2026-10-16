namespace EasyBI.Abstractions;

public enum MarkType { Bar, Line, Pie, Kpi, Table }

/// <summary>
/// Définition d'un visuel, indépendante de toute librairie graphique.
/// C'est ce que l'utilisateur construit dans le designer et ce qui est sauvegardé en JSON.
/// </summary>
public sealed record VisualDefinition
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Title { get; init; } = "";

    /// <summary>Nom du modèle sémantique interrogé.</summary>
    public required string Model { get; init; }

    public MarkType Mark { get; init; } = MarkType.Bar;

    /// <summary>Catégories (axe X d'un graphique, lignes d'une table).</summary>
    public IReadOnlyList<FieldRef> Axis { get; init; } = [];

    /// <summary>Dimension qui éclate les valeurs en plusieurs séries.</summary>
    public FieldRef? Legend { get; init; }

    public IReadOnlyList<MeasureRef> Values { get; init; } = [];

    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    public SortSpec? Sort { get; init; }

    public int? Top { get; init; }

    /// <summary>Options propres à un renderer donné (clé = nom du renderer), pour ne pas brider la norme.</summary>
    public IReadOnlyDictionary<string, string>? Extensions { get; init; }
}

/// <summary>Un rapport = un ensemble de visuels. Le schéma est versionné.</summary>
public sealed record ReportDefinition
{
    public const string CurrentSchema = "easybi/v1";

    public string Schema { get; init; } = CurrentSchema;

    public string Title { get; init; } = "Rapport";

    public IReadOnlyList<VisualDefinition> Visuals { get; init; } = [];
}
