using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>An element picked in a visual: the value of each dimension that identifies it (a bar: its category and series).</summary>
public sealed record SelectedElement(IReadOnlyList<FieldRef> Fields, IReadOnlyList<object?> Values)
{
    /// <summary>Invariant form of each value, as the filters hold it.</summary>
    public IReadOnlyList<string?> Keys { get; } = [.. Values.Select(InvariantValue.Format)];

    /// <summary>Equality filters on each dimension.</summary>
    public IReadOnlyList<FilterSpec> Filters => [.. Fields.Select((f, i) => f.Matching([Keys[i]]))];

    /// <summary>True when both designate the same element (same dimensions, same values).</summary>
    public bool SameAs(SelectedElement other) => Fields.SequenceEqual(other.Fields) && Keys.SequenceEqual(other.Keys);

    internal string Describe(ModelMetadata metadata) =>
        string.Join(", ", Fields.Select((f, i) => $"{Caption(metadata, f)} = {f.Display(Values[i])}"));

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
                return [.. Elements[0].Fields.Select((f, i) => f.Matching(i == varying ? [.. Elements.Select(e => e.Keys[i]).Distinct()] : [Elements[0].Keys[i]]))];
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
                ? string.Join(or, Elements.Select(e => f.Display(e.Values[i])).Distinct())
                : f.Display(Elements[0].Values[i]))));
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
/// and cross-filtering (transient selections made by clicking in visuals, at most one per visual).
/// </summary>
public sealed class FilterContext
{
    private readonly List<ReportFilter> _filters = [];
    private readonly List<CrossFilter> _selections = [];

    /// <summary>Raised whenever a global filter or the selection changes.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ReportFilter> Filters => _filters;

    /// <summary>
    /// Selections made in visuals, one per source visual (Ctrl+click in another visual adds one). A visual is filtered
    /// by the selections of the others: a row must belong to each of them.
    /// </summary>
    public IReadOnlyList<CrossFilter> Selections => _selections;

    /// <summary>The selection made in a visual, if any.</summary>
    public CrossFilter? SelectionOf(string visualId) => _selections.FirstOrDefault(s => s.SourceVisualId == visualId);

    /// <summary>
    /// Adds or replaces the global filter on this field. A filter without values is removed, except "one of no value"
    /// (<see cref="FilterOperator.In"/>), which keeps no row: every value unchecked.
    /// </summary>
    public void SetFilter(string model, FilterSpec filter)
    {
        var index = _filters.FindIndex(f => f.Model == model && f.Filter.Field == filter.Field);
        if (filter.Values.Count == 0 && filter.Operator != FilterOperator.In)
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
        _selections.Clear();
        OnChanged();
    }

    /// <summary>
    /// Selects the element clicked in a visual, in place of every current selection; clicking the only selected element
    /// again clears it. With <paramref name="extend"/> (Ctrl+click), the element is added to the selection of its visual,
    /// or removed from it when it was already selected, and the selections of the other visuals are kept.
    /// </summary>
    public void Select(RenderRequest request, DataSelection selection, bool extend = false)
    {
        if (SelectionFilters.Element(request, selection) is not { } element) return;
        var visual = request.Visual;
        var index = _selections.FindIndex(s => s.SourceVisualId == visual.Id);
        if (extend && index >= 0)
        {
            var current = _selections[index];
            List<SelectedElement> elements = [.. current.Elements.Where(e => !e.SameAs(element))];
            if (elements.Count == current.Elements.Count) elements.Add(element);
            if (elements.Count == 0) _selections.RemoveAt(index);
            else _selections[index] = current with { Elements = elements };
        }
        else if (extend)
        {
            _selections.Add(new CrossFilter(visual.Id, visual.Model, [element]));
        }
        else
        {
            var same = _selections is [{ Elements: [var only] } previous] && previous.SourceVisualId == visual.Id && only.SameAs(element);
            _selections.Clear();
            if (!same) _selections.Add(new CrossFilter(visual.Id, visual.Model, [element]));
        }
        OnChanged();
    }

    /// <summary>Clears every selection.</summary>
    public void ClearSelection()
    {
        if (_selections.Count == 0) return;
        _selections.Clear();
        OnChanged();
    }

    /// <summary>Clears the selection made in one visual (removed from a dashboard, or its own clear button).</summary>
    public void ClearSelection(string visualId)
    {
        if (_selections.RemoveAll(s => s.SourceVisualId == visualId) > 0)
            OnChanged();
    }

    /// <summary>
    /// Readable summary of every selection in the current language, "Region = North or South and Channel = Web";
    /// <paramref name="metadata"/> gives the metadata of a model (selections of unknown models are left out).
    /// </summary>
    public string DescribeSelections(Func<string, ModelMetadata?> metadata)
    {
        var several = _selections.Count > 1;
        return string.Join($" {Strings.SelectionAnd} ", _selections
            .Select(s => (Selection: s, Model: metadata(s.Model)))
            .Where(d => d.Model is not null)
            .Select(d => (d.Selection, Text: d.Selection.Describe(d.Model!)))
            .Select(d => several && d.Selection.Elements.Count > 1 ? $"({d.Text})" : d.Text));
    }

    /// <summary>The elements of the selection to emphasize in the request's visual, when it is the source of one.</summary>
    public IReadOnlyList<DataSelection>? HighlightFor(RenderRequest request) =>
        SelectionOf(request.Visual.Id) is { } selection ? SelectionFilters.Highlight(request, selection) : null;

    /// <summary>
    /// Filters to add to a visual: global filters of its model, then the selections made in the other visuals of the
    /// same model (a visual is not filtered by its own selection).
    /// </summary>
    public IReadOnlyList<FilterSpec> FiltersFor(VisualDefinition visual)
    {
        var filters = _filters.Where(f => f.Model == visual.Model).Select(f => f.Filter).ToList();
        foreach (var selection in _selections.Where(s => s.Model == visual.Model && s.SourceVisualId != visual.Id))
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

            case MatrixSelection { Row: var r, Column: var c } when request.Matrix is { } matrix:
                if (r is { } matrixRow && matrixRow >= 0 && matrixRow < matrix.Rows.Count)
                    picked.AddRange(visual.Axis.Select((a, i) => (a, matrix.Rows[matrixRow].Values.ElementAtOrDefault(i))));
                if (c is { } matrixColumn && matrixColumn >= 0 && matrixColumn < matrix.Columns.Count && visual.Legend is { } columns)
                    picked.Add((columns, matrix.Columns[matrixColumn].Values[0]));
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
            if (request.Matrix is { } matrix)
            {
                highlight.AddRange(MatrixHighlight(visual, matrix, element));
                continue;
            }
            if (visual.Type == VisualType.Table)
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

    // A matrix's element: its rows (axis values), its column (legend value), or the cells of both
    private static IEnumerable<DataSelection> MatrixHighlight(VisualDefinition visual, MatrixData matrix, SelectedElement element)
    {
        var axis = visual.Axis.Count;
        var onRows = element.Fields.Count >= axis && axis > 0 && element.Fields.Take(axis).SequenceEqual(visual.Axis);
        var columnField = onRows ? axis : 0;
        var onColumn = element.Fields.Count == columnField + 1 && element.Fields[columnField] == visual.Legend;
        if (!onRows && !onColumn || element.Fields.Count != (onRows ? axis : 0) + (onColumn ? 1 : 0)) return [];

        int?[] rows = onRows
            ? [.. Enumerable.Range(0, matrix.Rows.Count)
                .Where(r => Enumerable.Range(0, axis).All(i => InvariantValue.Format(matrix.Rows[r].Values[i]) == element.Keys[i]))
                .Select(r => (int?)r)]
            : [null];
        int?[] columns = onColumn
            ? [.. Enumerable.Range(0, matrix.Columns.Count)
                .Where(c => InvariantValue.Format(matrix.Columns[c].Values[0]) == element.Keys[columnField])
                .Select(c => (int?)c)]
            : [null];
        return rows.SelectMany(r => columns.Select(c => (DataSelection)new MatrixSelection(r, c)));
    }

    /// <summary>
    /// True when a part of a matrix is drawn as usual: a cell (row and column), a row's header or total (no column), a
    /// column's total (no row), the grand total (neither). Nothing highlighted, or the part belongs to the highlight.
    /// </summary>
    public static bool IsMatrixEmphasized(this RenderRequest request, int? row, int? column) =>
        request.Highlight is not { Count: > 0 } highlight
        || highlight.OfType<MatrixSelection>().Any(m => (row, column) switch
        {
            ({ } r, { } c) => (m.Row is null || m.Row == r) && (m.Column is null || m.Column == c),
            ({ } r, null) => m.Row == r,
            (null, { } c) => m.Column == c,
            _ => false,
        });

    /// <summary>True when a row of a table is drawn as usual: nothing is highlighted, or it is part of the highlight.</summary>
    public static bool IsRowEmphasized(this RenderRequest request, int row) =>
        request.Highlight is not { Count: > 0 } highlight || highlight.Any(h => h is RowSelection r && r.Row == row);
}
