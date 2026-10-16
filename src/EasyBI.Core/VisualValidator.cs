using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Règles de cohérence entre un type de visuel et les champs déposés.</summary>
public static class VisualValidator
{
    /// <returns>Un message destiné à l'utilisateur, ou null si le visuel est affichable.</returns>
    public static string? Validate(VisualDefinition visual) => visual.Mark switch
    {
        MarkType.Table when visual.Axis.Count == 0 && visual.Legend is null && visual.Values.Count == 0 =>
            "Déposez des champs pour construire la table.",
        MarkType.Table => null,
        _ when visual.Values.Count == 0 =>
            "Déposez au moins une mesure dans « Valeurs ».",
        MarkType.Kpi when visual.Axis.Count > 0 || visual.Legend is not null =>
            "Un KPI n'utilise ni « Axe » ni « Légende ».",
        MarkType.Pie when visual.Axis.Count != 1 || visual.Legend is not null || visual.Values.Count != 1 =>
            "Un camembert demande une dimension en « Axe », une seule mesure et pas de légende.",
        _ => null,
    };
}
