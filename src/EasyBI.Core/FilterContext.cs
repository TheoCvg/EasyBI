using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>An element picked in a visual: the value of each dimension that identifies it (a bar: its category and series).</summary>
public sealed record SelectedElement(IReadOnlyList<FieldRef> Fields, IReadOnlyList<object?> Values)
{
    /// <summary>Invariant form of each value, as the filters hold it.</summary>
    public IReadOnlyList<string?> Keys { get; } = [.. Values.Select(InvariantValue.Format)];

    /// <summary>Equality filters on each dimension.</summary>
    public IReadOnlyList<FilterSpec> Filters => [.. Fields.Select((f, i) => new FilterSpec(f.Field, FilterOperator.In, [Keys[i]], f.Grain))];

    /// <summary>True when both designate the same element (same dimensions, same values).</summary>
    public bool SameAs(SelectedElement other) => Fields.SequenceEqual(other.Fields) && Keys.SequenceEqual(other.Keys);

    internal string Describe(ModelMetadata metadata) =>
        string.Join(", ", Fields.Select((f, i) => $"{Caption(metadata, f)} = {ValueFormatter.Display(Values[i])}"));

    internal static string Caption(ModelMetadata metadata, FieldRef field) =>
        DateGrains.Caption(metadata.Fields.FirstOrDefault(f => f.Name == field.Field)?.Caption ?? field.Field, field.Grain);
}

/// <summary>
/// Selection made in a visual, which filters the other visuals of the same model: one or several
/// <see cref="Elements"/> (Ctrl+click), a row being kept when it belongs to any of them.
/// </summary>
public sealed record CrossFilter(string SourceVisualId, string Model, IReadOnlyList<SelectedElement> Elements)
{
    /// <summary>
    /// Filters added to the other visuals: equality filters for one element, a list of values when the elements only
    /// differ on one dimension, otherwise one <see cref="FilterOperator.AnyOf"/> filter.
    /// </summary>
    public IReadOnlyList<FilterSpec> Filters
    {
        get
        {
            if (Elements.Count == 1) return Elements[0].Filters;
            if (VaryingField() is { } varying)
                return [.. Elements[0].Fields.Select((f, i) => new FilterSpec(f.Field, FilterOperator.In,
                    i == varying ? [.. Elements.Select(e => e.Keys[i]).Distinct()] : [Elements[0].Keys[i]], f.Grain))];
            return [FilterSpec.AnyOf([.. Elements.Select(e => e.Filters)])];
        }
    }

    /// <summary>Readable summary in the current language: "Region = North, Channel = Web", "Region = North or South".</summary>
    public string Describe(ModelMetadata metadata)
    {
        if (Elements.Count == 1) return Elements[0].Describe(metadata);
        var or = $" {Strings.SelectionOr} ";
        if (VaryingField() is { } varying)
            return string.Join(", ", Elements[0].Fields.Select((f, i) => $"{SelectedElement.Caption(metadata, f)} = " + (i == varying
                ? string.Join(or, Elements.Select(e => ValueFormatter.Display(e.Values[i])).Distinct())
                : ValueFormatter.Display(Elements[0].Values[i]))));
        return string.Join(or, Elements.Select(e => $"({e.Describe(metadata)})"));
    }

    /// <summary>The only dimension on which the elements differ, when they all have the same dimensions.</summary>
    private int? VaryingField()
    {
        var first = Elements[0];
        if (Elements.Any(e => !e.Fields.SequenceEqual(first.Fields))) return null;
        var varying = Enumerable.Range(0, first.Fields.Count)
            .Where(i => Elements.Any(e => e.Keys[i] != first.Keys[i]))
            .ToList();
        return varying.Count == 1 ? varying[0] : null;
    }
}

/// <summary>
/// Filtering state of a dashboard: global filters (slicers, saved with the report)
/// and cross-filtering (transient selection made by clicking in a visual).
/// </summary>
public sealed class FilterContext
{
    private readonly List<ReportFilter> _filters = [];

    /// <summary>Raised whenever a global filter or the selection changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ReportFilter> Filters => _filters;

    public CrossFilter? Selection { get; private set; }

    /// <summary>Adds or replaces the global filter on this field; a filter without values is removed.</summary>
    public void SetFilter(string model, FilterSpec filter)
    {
        var index = _filters.FindIndex(f => f.Model == model && f.Filter.Field == filter.Field);
        if (filter.Values.Count == 0)
        {
            if (index >= 0) RemoveFilter(model, filter.Field);
            return;
        }
        if (index >= 0 && SameFilter(_filters[index].Filter, filter)) return;
        if (index >= 0) _filters[index] = new ReportFilter(model, filter);
        else _filters.Add(new ReportFilter(model, filter));
        OnChanged();
    }

    public void RemoveFilter(string model, string field)
    {
        if (_filters.RemoveAll(f => f.Model == model && f.Filter.Field == field) > 0)
            OnChanged();
    }

    /// <summary>Replaces all global filters (when a report is opened) and clears the selection.</summary>
    public void Load(IEnumerable<ReportFilter> filters)
    {
        _filters.Clear();
        _filters.AddRange(filters);
        Selection = null;
        OnChanged();
    }

    /// <summary>
    /// Selects the element clicked in a visual, in place of the current selection; clicking the only selected element
    /// again clears it. With <paramref name="extend"/> (Ctrl+click), the element is added to the selection of the same
    /// visual, or removed from it when it was already selected.
    /// </summary>
    public void Select(RenderRequest request, DataSelection selection, bool extend = false)
    {
        if (SelectionFilters.Element(request, selection) is not { } element) return;
        var visual = request.Visual;
        if (extend && Selection is { } current && current.SourceVisualId == visual.Id)
        {
            List<SelectedElement> elements = [.. current.Elements.Where(e => !e.SameAs(element))];
            if (elements.Count == current.Elements.Count) elements.Add(element);
            Selection = elements.Count == 0 ? null : current with { Elements = elements };
        }
        else
        {
            var same = Selection is { Elements: [var only] } previous && previous.SourceVisualId == visual.Id && only.SameAs(element);
            Selection = same ? null : new CrossFilter(visual.Id, visual.Model, [element]);
        }
        OnChanged();
    }

    public void ClearSelection()
    {
        if (Selection is null) return;
        Selection = null;
        OnChanged();
    }

    /// <summary>The elements of the selection to emphasize in the request's visual, when it is the selection's source.</summary>
    public IReadOnlyList<DataSelection>? HighlightFor(RenderRequest request) =>
        Selection is { } selection && selection.SourceVisualId == request.Visual.Id
            ? SelectionFilters.Highlight(request, selection)
            : null;

    /// <summary>
    /// Filters to add to a visual: global filters of its model, then the selection made
    /// in another visual of the same model (the source visual is not filtered by its own selection).
    /// </summary>
    public IReadOnlyList<FilterSpec> FiltersFor(VisualDefinition visual)
    {
        var filters = _filters.Where(f => f.Model == visual.Model).Select(f => f.Filter).ToList();
        if (Selection is { } selection && selection.Model == visual.Model && selection.SourceVisualId != visual.Id)
            filters.AddRange(selection.Filters);
        return filters;
    }

    /// <summary>The visual as it must be executed, dashboard filters included.</summary>
    public VisualDefinition Apply(VisualDefinition visual)
    {
        var extra = FiltersFor(visual);
        return extra.Count == 0 ? visual : visual with { Filters = [.. visual.Filters, .. extra] };
    }

    private static bool SameFilter(FilterSpec a, FilterSpec b) =>
        a.Field == b.Field && a.Operator == b.Operator && a.Grain == b.Grain && a.Values.SequenceEqual(b.Values);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Turns a click in a visual into the element it designates, and finds the selected elements in a visual.</summary>
public static class SelectionFilters
{
    /// <summary>Opacity of the elements left out of the highlight.</summary>
    public const double DimmedOpacity = 0.3;

    /// <returns>The matching cross-filter, or null when the element carries no dimension (KPI, total).</returns>
    public static CrossFilter? Build(RenderRequest request, DataSelection selection) =>
        Element(request, selection) is { } element ? new CrossFilter(request.Visual.Id, request.Visual.Model, [element]) : null;

    /// <returns>The clicked element, or null when it carries no dimension (KPI, total).</returns>
    public static SelectedElement? Element(RenderRequest request, DataSelection selection)
    {
        var visual = request.Visual;
        var picked = new List<(FieldRef Field, object? Value)>();
        switch (selection)
        {
            case CategorySelection { Category: var c, Series: var s } when c >= 0 && c < request.Chart.CategoryValues.Count:
                var values = request.Chart.CategoryValues[c];
                for (var i = 0; i < visual.Axis.Count && i < values.Count; i++)
                    picked.Add((visual.Axis[i], values[i]));
                if (visual.Legend is { } legend && s is { } series && series >= 0 && series < request.Chart.Series.Count)
                    picked.Add((legend, request.Chart.Series[series].LegendValue));
                break;

            case RowSelection { Row: var r } when r >= 0 && r < request.Result.Rows.Count:
                var row = request.Result.Rows[r];
                foreach (var field in visual.Axis.Append(visual.Legend).OfType<FieldRef>())
                    picked.Add((field, row[request.Result.IndexOf(field.Key)]));
                break;
        }
        return picked.Count == 0 ? null : new SelectedElement([.. picked.Select(p => p.Field)], [.. picked.Select(p => p.Value)]);
    }

    /// <summary>
    /// Where the selected elements are in the visual's current data: rows of a table, categories (and series) of a
    /// chart. Elements no longer shown are left out.
    /// </summary>
    public static IReadOnlyList<DataSelection> Highlight(RenderRequest request, CrossFilter selection)
    {
        var visual = request.Visual;
        var highlight = new List<DataSelection>();
        foreach (var element in selection.Elements)
        {
            if (visual.Mark == MarkType.Table)
            {
                var columns = element.Fields.Select(f => request.Result.IndexOf(f.Key)).ToArray();
                if (columns.Any(c => c < 0)) continue;
                for (var r = 0; r < request.Result.Rows.Count; r++)
                    if (columns.Select((c, i) => InvariantValue.Format(request.Result.Rows[r][c]) == element.Keys[i]).All(m => m))
                        highlight.Add(new RowSelection(r));
                continue;
            }

            // A chart's element is made of its axis values, then possibly of its legend value
            var axis = visual.Axis.Count;
            if (element.Fields.Count < axis || !element.Fields.Take(axis).SequenceEqual(visual.Axis)) continue;
            var hasSeries = element.Fields.Count == axis + 1;
            if (element.Fields.Count > axis + 1 || hasSeries && element.Fields[axis] != visual.Legend) continue;

            var chart = request.Chart;
            for (var c = 0; c < chart.CategoryValues.Count; c++)
            {
                var values = chart.CategoryValues[c];
                if (values.Count < axis || Enumerable.Range(0, axis).Any(i => InvariantValue.Format(values[i]) != element.Keys[i]))
                    continue;
                if (!hasSeries)
                    highlight.Add(new CategorySelection(c));
                else
                    for (var s = 0; s < chart.Series.Count; s++)
                        if (InvariantValue.Format(chart.Series[s].LegendValue) == element.Keys[axis])
                            highlight.Add(new CategorySelection(c, s));
            }
        }
        return highlight;
    }

    /// <summary>
    /// True when the point of a series in a category is drawn as usual: nothing is highlighted, or it is part of the
    /// highlight.
    /// </summary>
    public static bool IsEmphasized(this RenderRequest request, int category, int series) =>
        request.Highlight is not { Count: > 0 } highlight
        || highlight.Any(h => h is CategorySelection c && c.Category == category && (c.Series is null || c.Series == series));

    /// <summary>True when a series has at least one emphasized point, or nothing is highlighted.</summary>
    public static bool IsEmphasized(this RenderRequest request, int series) =>
        request.Highlight is not { Count: > 0 } highlight
        || highlight.Any(h => h is CategorySelection c && (c.Series is null || c.Series == series));

    /// <summary>True when a row of a table is drawn as usual: nothing is highlighted, or it is part of the highlight.</summary>
    public static bool IsRowEmphasized(this RenderRequest request, int row) =>
        request.Highlight is not { Count: > 0 } highlight || highlight.Any(h => h is RowSelection r && r.Row == row);
}
