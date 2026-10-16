using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Selection made in a visual, which filters the other visuals of the same model.</summary>
public sealed record CrossFilter(string SourceVisualId, string Model, IReadOnlyList<FilterSpec> Filters, string Description);

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
    /// Selects the element clicked in a visual. Clicking the same selection again clears it.
    /// </summary>
    public void Select(RenderRequest request, DataSelection selection)
    {
        var cross = SelectionFilters.Build(request, selection);
        if (cross is null) return;
        var same = Selection is { } current
            && current.SourceVisualId == cross.SourceVisualId
            && current.Filters.Count == cross.Filters.Count
            && current.Filters.Zip(cross.Filters).All(p => SameFilter(p.First, p.Second));
        Selection = same ? null : cross;
        OnChanged();
    }

    public void ClearSelection()
    {
        if (Selection is null) return;
        Selection = null;
        OnChanged();
    }

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

/// <summary>Turns a click in a visual into equality filters on its dimensions.</summary>
public static class SelectionFilters
{
    /// <returns>The matching cross-filter, or null when the element carries no dimension (KPI, total).</returns>
    public static CrossFilter? Build(RenderRequest request, DataSelection selection)
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
        if (picked.Count == 0) return null;

        var filters = picked
            .Select(p => new FilterSpec(p.Field.Field, FilterOperator.In, [InvariantValue.Format(p.Value)], p.Field.Grain))
            .ToArray();
        var description = string.Join(", ", picked.Select(p =>
            $"{request.Result.Columns[request.Result.IndexOf(p.Field.Key)].Caption} = {ValueFormatter.Display(p.Value)}"));
        return new CrossFilter(visual.Id, visual.Model, filters, description);
    }
}
