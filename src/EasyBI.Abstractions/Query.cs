namespace EasyBI.Abstractions;

/// <summary>Référence à une dimension, avec une granularité optionnelle pour les dates.</summary>
public sealed record FieldRef(string Field, DateGrain Grain = DateGrain.None)
{
    /// <summary>Nom de la colonne produite dans le <see cref="ResultSet"/>.</summary>
    public string Key => Grain == DateGrain.None ? Field : $"{Field}.{Grain}";
}

/// <summary>Référence à une mesure ; l'agrégation par défaut du modèle peut être surchargée.</summary>
public sealed record MeasureRef(string Measure, Aggregation? Aggregation = null)
{
    public string Key => Aggregation is null ? Measure : $"{Measure}.{Aggregation}";
}

public enum FilterOperator { In, NotIn }

/// <summary>Filtre sur une dimension. Les valeurs sont stockées sous forme invariante pour rester sérialisables.</summary>
public sealed record FilterSpec(string Field, FilterOperator Operator, IReadOnlyList<string> Values);

public sealed record SortSpec(string Key, bool Descending = false);

/// <summary>
/// Requête analytique abstraite : "grouper par ... et agréger ...".
/// Elle ne connaît ni la source de données ni le rendu.
/// </summary>
public sealed record AnalyticQuery(
    IReadOnlyList<FieldRef> GroupBy,
    IReadOnlyList<MeasureRef> Measures,
    IReadOnlyList<FilterSpec>? Filters = null,
    IReadOnlyList<SortSpec>? Sort = null,
    int? Top = null);
