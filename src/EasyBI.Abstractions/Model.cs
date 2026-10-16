namespace EasyBI.Abstractions;

/// <summary>Rôle analytique d'un champ : axe d'analyse ou valeur agrégée.</summary>
public enum FieldKind { Dimension, Measure }

public enum DataType { Text, Number, Date, Boolean }

public enum Aggregation { Sum, Count, Average, Min, Max }

/// <summary>Granularité appliquée à une dimension de type date.</summary>
public enum DateGrain { None, Year, Quarter, Month, Day }

/// <summary>Description d'un champ exposé à l'utilisateur final.</summary>
public sealed record FieldDescriptor(
    string Name,
    string Caption,
    FieldKind Kind,
    DataType DataType,
    Aggregation? DefaultAggregation = null,
    string? Format = null);

/// <summary>Métadonnées d'un modèle sémantique : ce que l'utilisateur a le droit d'analyser.</summary>
public sealed record ModelMetadata(string Name, string Caption, IReadOnlyList<FieldDescriptor> Fields)
{
    public FieldDescriptor GetField(string name) =>
        Fields.FirstOrDefault(f => f.Name == name)
        ?? throw new KeyNotFoundException($"Champ '{name}' inconnu dans le modèle '{Name}'.");

    public IEnumerable<FieldDescriptor> Dimensions => Fields.Where(f => f.Kind == FieldKind.Dimension);

    public IEnumerable<FieldDescriptor> Measures => Fields.Where(f => f.Kind == FieldKind.Measure);
}

/// <summary>
/// Point d'entrée qu'un développeur fournit : métadonnées + capacité à exécuter une requête analytique.
/// Une implémentation peut cibler IQueryable, SQL brut, une API REST, un cube OLAP...
/// </summary>
public interface ISemanticModel
{
    ModelMetadata Metadata { get; }

    Task<ResultSet> ExecuteAsync(AnalyticQuery query, CancellationToken cancellationToken = default);

    /// <summary>Valeurs distinctes d'une dimension, utilisées par les filtres.</summary>
    Task<IReadOnlyList<object?>> GetDistinctValuesAsync(string field, CancellationToken cancellationToken = default);
}
