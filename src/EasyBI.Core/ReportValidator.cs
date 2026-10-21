using EasyBI.Abstractions;
using EasyBI.Core.Resources;

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

        // Text and image visuals carry their content and reference no model
        foreach (var visual in report.Visuals.Where(v => !v.Mark.IsContent()))
        {
            var where = string.IsNullOrWhiteSpace(visual.Title)
                ? Strings.Format(Strings.ReportUntitledVisual, visual.Mark)
                : Strings.Format(Strings.ReportTitledVisual, visual.Title);
            if (!byName.TryGetValue(visual.Model, out var model))
            {
                problems.Add(Strings.Format(Strings.ReportUnknownModel, where, visual.Model));
                continue;
            }
            foreach (var axis in visual.Axis.Append(visual.Legend).OfType<FieldRef>())
                CheckDimension(model, axis.Field, axis.Grain, where, problems);
            // The report's calculated measures are checked once, below
            foreach (var value in visual.Values.Where(v => !report.Measures.Any(m => m.Model == visual.Model && m.Name == v.Measure)))
                CheckField(model, value.Measure, FieldKind.Measure, where, problems);
            foreach (var filter in visual.Filters)
                CheckFilter(model, filter, where, problems);

            if (visual.Sort is { } sort)
            {
                var keys = visual.Axis.Append(visual.Legend).OfType<FieldRef>().Select(f => f.Key)
                    .Concat(visual.Values.Select(v => v.Key));
                if (!keys.Contains(sort.Key))
                    problems.Add(Strings.Format(Strings.ReportSortNotDisplayed, where, sort.Key));
            }
        }

        foreach (var filter in report.Filters)
        {
            if (!byName.TryGetValue(filter.Model, out var model))
                problems.Add(Strings.Format(Strings.ReportUnknownModel, Strings.ReportFilter, filter.Model));
            else
                CheckFilter(model, filter.Filter, Strings.ReportFilter, problems);
        }

        foreach (var measure in report.Measures)
        {
            var where = Strings.Format(Strings.ReportCalculatedMeasure, measure.Caption);
            if (!byName.TryGetValue(measure.Model, out var model))
                problems.Add(Strings.Format(Strings.ReportUnknownModel, where, measure.Model));
            else if (MeasureFormula.Validate(measure.Formula, model) is { } problem)
                problems.Add(Strings.Format(Strings.ReportInvalidFormula, where, problem));
        }
        return problems;
    }

    private static void CheckFilter(ModelMetadata model, FilterSpec filter, string where, List<string> problems)
    {
        if (filter.Operator == FilterOperator.AnyOf)
        {
            foreach (var alternative in (filter.Alternatives ?? []).SelectMany(group => group))
                CheckFilter(model, alternative, where, problems);
        }
        else if (!filter.Operator.IsPeriod())
            CheckDimension(model, filter.Field, filter.Grain, where, problems);
        else if (CheckField(model, filter.Field, FieldKind.Dimension, where, problems) is { DataType: not DataType.Date })
            problems.Add(Strings.Format(Strings.ReportPeriodOnNonDate, where, filter.Field));
    }

    private static void CheckDimension(ModelMetadata model, string name, DateGrain grain, string where, List<string> problems)
    {
        if (CheckField(model, name, FieldKind.Dimension, where, problems) is { } field
            && grain != DateGrain.None && field.DataType != DataType.Date)
            problems.Add(Strings.Format(Strings.ReportGrainOnNonDate, where, grain, name));
    }

    private static FieldDescriptor? CheckField(ModelMetadata model, string name, FieldKind kind, string where, List<string> problems)
    {
        var field = model.Fields.FirstOrDefault(f => f.Name == name);
        if (field is null)
            problems.Add(Strings.Format(Strings.ReportMissingField, where, name, model.Name));
        else if (field.Kind != kind)
            problems.Add(Strings.Format(kind == FieldKind.Dimension ? Strings.ReportNotADimension : Strings.ReportNotAMeasure, where, name));
        return field?.Kind == kind ? field : null;
    }
}
