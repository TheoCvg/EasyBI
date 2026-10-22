using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>Consistency rules between a visual type and the dropped fields.</summary>
public static class VisualValidator
{
    /// <returns>A message for the end user, or null when the visual can be displayed.</returns>
    public static string? Validate(VisualDefinition visual) => visual.Mark switch
    {
        MarkType.Text => null,
        MarkType.Image when visual.Image is null => Strings.ValidationNoImage,
        MarkType.Image => null,
        MarkType.Table when visual.Axis.Count == 0 && visual.Legend is null && visual.Values.Count == 0 =>
            Strings.ValidationEmptyTable,
        MarkType.Table => null,
        _ when visual.Mark.IsCartesian() && visual.Values.Any(v => v.Mark is { } mark && !mark.IsCartesian()) =>
            Strings.ValidationSeriesMark,
        _ when visual.Values.Count == 0 =>
            Strings.ValidationNoMeasure,
        MarkType.Waterfall when visual.Axis.Count == 0 || visual.Legend is not null || visual.Values.Count != 1 =>
            Strings.ValidationWaterfall,
        MarkType.Gauge when visual.Axis.Count > 0 || visual.Legend is not null || visual.Values.Count != 1 =>
            Strings.ValidationGauge,
        MarkType.Kpi when visual.Axis.Count > 0 || visual.Legend is not null =>
            Strings.ValidationKpi,
        var mark when mark.IsCircular() && (visual.Axis.Count != 1 || visual.Legend is not null || visual.Values.Count != 1) =>
            Strings.ValidationPie,
        _ => null,
    };
}
