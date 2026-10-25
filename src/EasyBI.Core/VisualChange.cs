using System.Text.Json;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// A change of a visual, from <see cref="Before"/> to <see cref="After"/>: what a rule of the designer accepts or refuses,
/// whatever the action that makes it (a type picked, a field dropped, a property edited...).
/// </summary>
public sealed record VisualChange(VisualDefinition Before, VisualDefinition After)
{
    /// <summary>
    /// True when a part of the visual differs, compared by value: <c>change.Changes(v =&gt; v.Type)</c>,
    /// <c>change.Changes(v =&gt; v.Values)</c>.
    /// </summary>
    public bool Changes<T>(Func<VisualDefinition, T> part) => Json(part(Before)) != Json(part(After));

    /// <summary>
    /// True when the fields of the wells differ: one added, removed or moved on the axis, in the legend, the values, the
    /// tooltip or the filters. Their settings (aggregation, format, date grain, filter values...) are not fields.
    /// </summary>
    public bool FieldsChanged => !Fields(Before).SequenceEqual(Fields(After));

    private static IEnumerable<string> Fields(VisualDefinition visual) =>
    [
        .. visual.Axis.Select(a => "axis:" + a.Field),
        .. visual.Legend is { } legend ? ["legend:" + legend.Field] : Array.Empty<string>(),
        .. visual.Values.Select(v => "value:" + v.Measure),
        .. visual.Tooltip?.Values.Select(v => "tooltip:" + v.Measure) ?? [],
        .. visual.Filters.Select(f => "filter:" + f.Field),
    ];

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ReportSerializer.Options);
}
