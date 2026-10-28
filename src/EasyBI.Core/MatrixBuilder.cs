using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// Queries and pivots a <see cref="VisualType.Matrix"/>: its rows (the axis) with their totals, its cells (axis and
/// legend), the totals of its columns (the legend) and the grand total. Each level is queried, so a total is the
/// measure over all its rows, not a sum of cells.
/// </summary>
public static class MatrixBuilder
{
    /// <summary>
    /// Runs the queries of a matrix. The sort, <see cref="VisualDefinition.Top"/> and the filters on measures choose the
    /// rows; the cells and the totals of the columns then only count those rows.
    /// </summary>
    /// <returns>The cells' result (one row per row and column), and the pivoted matrix.</returns>
    public static Task<(ResultSet Cells, MatrixData Matrix)> ExecuteAsync(
        QuerySemanticModel model,
        VisualDefinition visual,
        IEnumerable<CalculatedMeasure>? measures = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(model, visual, measures, DateOnly.FromDateTime(DateTime.Today), cancellationToken);

    /// <summary>Runs the queries of a matrix on <paramref name="today"/>.</summary>
    public static async Task<(ResultSet Cells, MatrixData Matrix)> ExecuteAsync(
        QuerySemanticModel model,
        VisualDefinition visual,
        IEnumerable<CalculatedMeasure>? measures,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var query = visual.ToQuery(measures, model.Metadata);
        var metadata = model.Metadata;
        var rowFilters = (query.Filters ?? []).Where(f => !NumericFilters.IsOnMeasure(f, metadata, query.Calculations)).ToList();
        IReadOnlyList<FieldRef> columnFields = visual.Legend is { } legend ? [legend] : [];

        // Rows, with their totals: a sort on a column of the legend has no meaning for them
        var rowKeys = visual.Axis.Select(a => a.Key).Concat(query.Measures.Select(m => m.Key)).ToHashSet();
        var rowsQuery = query with
        {
            GroupBy = visual.Axis,
            Sort = query.Sort?.Where(s => rowKeys.Contains(s.Key)).ToArray() is { Length: > 0 } sort ? sort : null,
        };
        var rows = visual.Axis.Count > 0 ? await model.ExecuteWithTimeAsync(rowsQuery, today, cancellationToken).ConfigureAwait(false) : null;
        if (rows is not null && (query.Top is not null || rowFilters.Count < (query.Filters?.Count ?? 0)))
            rowFilters.Add(RowsFilter(visual.Axis, rows));

        var detailQuery = query with { Filters = rowFilters, Sort = null, Top = null };
        var cells = await model.ExecuteWithTimeAsync(detailQuery, today, cancellationToken).ConfigureAwait(false);
        var columns = columnFields.Count > 0
            ? await model.ExecuteWithTimeAsync(detailQuery with { GroupBy = columnFields }, today, cancellationToken).ConfigureAwait(false)
            : null;
        var total = await model.ExecuteWithTimeAsync(detailQuery with { GroupBy = [] }, today, cancellationToken).ConfigureAwait(false);

        return (cells, Pivot(visual, query.Measures, rows, cells, columns, total));
    }

    // Keeps the rows of the matrix: a list of values for one dimension, otherwise each row's values
    private static FilterSpec RowsFilter(IReadOnlyList<FieldRef> axis, ResultSet rows)
    {
        var keys = rows.Rows.Select(r => axis.Select((_, i) => InvariantValue.Format(r[i])).ToArray()).ToList();
        if (axis.Count == 1)
            return axis[0].Matching([.. keys.Select(k => k[0])], members: true);
        return FilterSpec.AnyOf([.. keys.Select(k => (IReadOnlyList<FilterSpec>)[.. axis.Select((a, i) => a.Matching([k[i]], members: true))])]);
    }

    private static MatrixData Pivot(
        VisualDefinition visual,
        IReadOnlyList<MeasureRef> measureRefs,
        ResultSet? rows,
        ResultSet cells,
        ResultSet? columns,
        ResultSet total)
    {
        var axis = visual.Axis.Count;
        var measures = measureRefs
            .Select(m => cells.Columns[cells.IndexOf(m.Key)])
            .Select(c => new MatrixMeasure(c.Key, c.Caption, c.Format))
            .ToArray();
        double?[] Values(ResultSet result, object?[] row) =>
            [.. measures.Select(m => result.IndexOf(m.Key) is var i and >= 0 ? ToNumber(row[i]) : null)];

        var rowMembers = rows?.Rows.Select(r => Member(r.Take(axis), visual.Axis)).ToArray() ?? [];
        var columnMembers = columns?.Rows.Select(r => Member(r.Take(1), [visual.Legend!])).ToArray() ?? [];
        var rowIndex = Index(rowMembers);
        var columnIndex = Index(columnMembers);

        var grid = rowMembers.Select(_ => columnMembers.Select(_ => new double?[measures.Length]).ToArray()).ToArray();
        if (columns is not null && rows is not null)
        {
            var legend = cells.IndexOf(visual.Legend!.Key);
            foreach (var row in cells.Rows)
            {
                if (rowIndex.TryGetValue(Key(row.Take(axis)), out var r) && columnIndex.TryGetValue(Key([row[legend]]), out var c))
                    grid[r][c] = Values(cells, row);
            }
        }

        return new MatrixData(
            rows is null ? [] : [.. visual.Axis.Select(a => rows.Columns[rows.IndexOf(a.Key)].Caption)],
            columns?.Columns[0].Caption,
            rowMembers,
            columnMembers,
            measures)
        {
            Cells = grid,
            RowTotals = [.. rows?.Rows.Select(r => (IReadOnlyList<double?>)Values(rows, r)) ?? []],
            ColumnTotals = [.. columns?.Rows.Select(r => (IReadOnlyList<double?>)Values(columns, r)) ?? []],
            GrandTotals = total.Rows.FirstOrDefault() is { } grand ? Values(total, grand) : new double?[measures.Length],
        };
    }

    // A member's values with their texts: the visual's labels, else the formatted values
    private static MatrixMember Member(IEnumerable<object?> values, IReadOnlyList<FieldRef> fields)
    {
        object?[] array = [.. values];
        return new MatrixMember(array, [.. array.Select((value, i) => fields[i].Display(value))]);
    }

    private static Dictionary<string, int> Index(IReadOnlyList<MatrixMember> members)
    {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < members.Count; i++) index.TryAdd(Key(members[i].Values), i);
        return index;
    }

    // Members are matched on their invariant values (a period's key, a date...)
    private static string Key(IEnumerable<object?> values) => string.Join("\u001f", values.Select(v => InvariantValue.Format(v) ?? "\u0000"));

    private static double? ToNumber(object? value) => value is null ? null : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
}
