using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>A pasted visual (with its new id) and the id of the visual it was copied from.</summary>
public sealed record PastedVisual(string CopiedId, VisualDefinition Visual);

/// <summary>
/// Copies visuals from one report to another (copy and paste, possibly between dashboards). The copied content is a
/// report in the <see cref="ReportDefinition.CurrentSchema"/> format holding the visuals and the calculated measures they
/// use, so that the target gets everything the visuals need.
/// </summary>
public static class VisualTransfer
{
    /// <summary>Name of the clipboard format holding copied visuals (see <see cref="Copy"/>).</summary>
    public const string ClipboardFormat = "EasyBI.Visuals";

    /// <summary>The visuals and the calculated measures they use, as report JSON.</summary>
    public static string Copy(IEnumerable<VisualDefinition> visuals, MeasureContext measures)
    {
        var copied = visuals.ToArray();
        return ReportSerializer.Serialize(new ReportDefinition
        {
            Visuals = copied,
            Measures = copied.SelectMany(measures.UsedBy).Distinct().ToArray(),
        });
    }

    /// <summary>
    /// The visuals of copied report JSON, ready to be added to a report: they get new ids, those of a model the target
    /// does not know are left out (texts and images have none), and the calculated measures they use are added to
    /// <paramref name="measures"/>. A measure whose name the target already uses for another definition is added under
    /// a new name, which its visuals follow. A copied redefinition of a model's measure (<paramref name="metadata"/> tells
    /// which fields a model declares) is added as a calculated measure of its own unless the target redefines the measure
    /// the same way, so that pasting never changes the target's measures. Content that is not copied visuals gives nothing.
    /// </summary>
    public static IReadOnlyList<PastedVisual> Paste(
        string json,
        MeasureContext measures,
        Func<string, bool> hasModel,
        Func<string, ModelMetadata?>? metadata = null)
    {
        ReportDefinition copied;
        try
        {
            copied = ReportSerializer.Deserialize(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException)
        {
            return [];
        }

        var renamed = new Dictionary<(string Model, string Name), string>();
        foreach (var measure in copied.Measures.Where(m => hasModel(m.Model)))
        {
            var existing = measures.Find(measure.Model, measure.Name) ?? measures.Measures.FirstOrDefault(m => m.Name == measure.Name);
            if (existing == measure) continue;
            var declared = metadata?.Invoke(measure.Model)?.Fields.FirstOrDefault(f => f.Name == measure.Name);
            if (existing is null && declared is null)
            {
                measures.Set(measure);
                continue;
            }
            var name = MeasureContext.NewName();
            renamed[(measure.Model, measure.Name)] = name;
            measures.Set(measure with { Name = name, Caption = measure.Caption.Length > 0 ? measure.Caption : declared?.Caption ?? measure.Name });
        }

        return copied.Visuals
            .Where(v => v.Mark is MarkType.Text or MarkType.Image || hasModel(v.Model))
            .Select(v => new PastedVisual(v.Id, Rename(v, renamed) with { Id = Guid.NewGuid().ToString("N") }))
            .ToArray();
    }

    private static VisualDefinition Rename(VisualDefinition visual, IReadOnlyDictionary<(string Model, string Name), string> renamed)
    {
        if (renamed.Count == 0) return visual;
        string NameOf(string measure) => renamed.GetValueOrDefault((visual.Model, measure), measure);
        var sort = visual.Sort;
        // A sort key starts with the measure's name ("calc_x", "calc_x.Sum"...)
        if (sort is not null && renamed.FirstOrDefault(r => r.Key.Model == visual.Model && (sort.Key == r.Key.Name || sort.Key.StartsWith(r.Key.Name + "."))) is { Value: { } name } match)
            sort = sort with { Key = name + sort.Key[match.Key.Name.Length..] };
        return visual with { Values = visual.Values.Select(v => v with { Measure = NameOf(v.Measure) }).ToArray(), Sort = sort };
    }
}
