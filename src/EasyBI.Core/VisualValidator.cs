using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>Consistency rules between a visual type and the dropped fields.</summary>
public static class VisualValidator
{
    /// <summary>
    /// Checks a visual against the definition of its type in <paramref name="types"/> (EasyBI's types when null): its
    /// rule, then the number of fields of each well.
    /// </summary>
    /// <returns>A message for the end user, or null when the visual can be displayed.</returns>
    public static string? Validate(VisualDefinition visual, VisualTypeCatalog? types = null)
    {
        if (visual.Type.IsCartesian() && visual.Values.Any(v => v.SeriesType is { } type && !type.IsCartesian()))
            return Strings.ValidationSeriesMark;
        var definition = (types ?? VisualTypeCatalog.Default).Get(visual.Type);
        if (definition.Values.MinFields > 0 && visual.Values.Count == 0) return Strings.ValidationNoMeasure;
        return definition.Validate?.Invoke(visual)
            ?? Check(definition.Axis, visual.Axis.Count, Strings.WellAxis)
            ?? Check(definition.Legend, visual.Legend is null ? 0 : 1, Strings.WellLegend)
            ?? Check(definition.Values, visual.Values.Count, Strings.WellValues);
    }

    private static string? Check(VisualWell well, int count, string name)
    {
        var caption = well.Caption?.Invoke() ?? name;
        if (!well.IsUsed) return count > 0 ? Strings.Format(Strings.ValidationWellUnused, caption) : null;
        if (count < well.MinFields)
            return well.MinFields == 1 ? Strings.Format(Strings.ValidationWellMissing, caption) : Strings.Format(Strings.ValidationWellMinimum, caption, well.MinFields);
        if (count > well.MaxFields)
            return well.MaxFields == 1 ? Strings.Format(Strings.ValidationWellSingle, caption) : Strings.Format(Strings.ValidationWellMaximum, caption, well.MaxFields);
        return null;
    }
}
