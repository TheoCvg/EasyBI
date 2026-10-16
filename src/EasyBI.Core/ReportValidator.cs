using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Checks that a loaded report matches the available models: a JSON file may
/// reference a model or a field that has since been renamed.
/// </summary>
public static class ReportValidator
{
    /// <returns>The problems found, worded for the end user (empty when the report is consistent).</returns>
    public static IReadOnlyList<string> Validate(ReportDefinition report, IEnumerable<ModelMetadata> models)
    {
        var byName = models.ToDictionary(m => m.Name);
        var problems = new List<string>();

        foreach (var visual in report.Visuals)
        {
            var where = string.IsNullOrWhiteSpace(visual.Title) ? $"Visuel {visual.Mark}" : $"« {visual.Title} »";
            if (!byName.TryGetValue(visual.Model, out var model))
            {
                problems.Add($"{where} : modèle '{visual.Model}' inconnu.");
                continue;
            }
            foreach (var axis in visual.Axis.Append(visual.Legend).OfType<FieldRef>())
                CheckDimension(model, axis.Field, axis.Grain, where, problems);
            foreach (var value in visual.Values)
                CheckField(model, value.Measure, FieldKind.Measure, where, problems);
            foreach (var filter in visual.Filters)
                CheckDimension(model, filter.Field, filter.Grain, where, problems);

            if (visual.Sort is { } sort)
            {
                var keys = visual.Axis.Append(visual.Legend).OfType<FieldRef>().Select(f => f.Key)
                    .Concat(visual.Values.Select(v => v.Key));
                if (!keys.Contains(sort.Key))
                    problems.Add($"{where} : tri sur '{sort.Key}', qui n'est pas affiché par le visuel.");
            }
        }

        foreach (var filter in report.Filters)
        {
            if (!byName.TryGetValue(filter.Model, out var model))
                problems.Add($"Filtre du rapport : modèle '{filter.Model}' inconnu.");
            else
                CheckDimension(model, filter.Filter.Field, filter.Filter.Grain, "Filtre du rapport", problems);
        }
        return problems;
    }

    private static void CheckDimension(ModelMetadata model, string name, DateGrain grain, string where, List<string> problems)
    {
        if (CheckField(model, name, FieldKind.Dimension, where, problems) is { } field
            && grain != DateGrain.None && field.DataType != DataType.Date)
            problems.Add($"{where} : granularité '{grain}' sur '{name}', qui n'est pas une date.");
    }

    private static FieldDescriptor? CheckField(ModelMetadata model, string name, FieldKind kind, string where, List<string> problems)
    {
        var field = model.Fields.FirstOrDefault(f => f.Name == name);
        if (field is null)
            problems.Add($"{where} : champ '{name}' absent du modèle '{model.Name}'.");
        else if (field.Kind != kind)
            problems.Add($"{where} : '{name}' n'est pas une {(kind == FieldKind.Dimension ? "dimension" : "mesure")}.");
        return field?.Kind == kind ? field : null;
    }
}
