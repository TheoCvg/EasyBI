using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>Consistency rules between a visual type and the dropped fields.</summary>
public static class VisualValidator
{
    /// <returns>A message for the end user, or null when the visual can be displayed.</returns>
    public static string? Validate(VisualDefinition visual)
    {
        if (visual.Type.IsCartesian() && visual.Values.Any(v => v.SeriesType is { } type && !type.IsCartesian()))
            return Strings.ValidationSeriesMark;
        var definition = VisualTypeCatalog.Default.Get(visual.Type);
        if (definition.Values.MinFields > 0 && visual.Values.Count == 0) return Strings.ValidationNoMeasure;
        return definition.Validate?.Invoke(visual);
    }
}
