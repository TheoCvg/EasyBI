using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>A run of neighbouring categories sharing the value of an outer axis field: <see cref="Count"/> categories from <see cref="First"/>.</summary>
public sealed record CategoryGroup(string Label, int First, int Count)
{
    /// <summary>The middle of the group on a category axis (categories at 0, 1, 2...).</summary>
    public double Center => First + (Count - 1) / 2.0;
}

/// <summary>
/// The categories of a chart with several axis fields, as a hierarchy: each category labelled by its innermost field
/// only ("North"), and the outer fields as groups of neighbouring categories ("2024-01" over its regions), so that a
/// connector draws short labels under each category and each outer value once under its group.
/// </summary>
public sealed record CategoryLevels(IReadOnlyList<string> Labels, IReadOnlyList<IReadOnlyList<CategoryGroup>> Groups)
{
    /// <summary>
    /// The levels of a chart's categories; null when the axis has a single field, or when the categories are not
    /// ordered by their outer fields (sorted on a measure: groups of one category each say nothing).
    /// </summary>
    public static CategoryLevels? Of(ChartData chart)
    {
        var keys = chart.CategoryValues;
        if (keys.Count == 0 || keys.Count != chart.Categories.Count || keys[0].Count < 2 || keys.Any(k => k.Count != keys[0].Count))
            return null;
        var depth = keys[0].Count;

        // From the field just above the labels to the outermost; a group also ends where a group of an outer field ends
        var groups = new List<IReadOnlyList<CategoryGroup>>();
        for (var level = depth - 2; level >= 0; level--)
        {
            var runs = new List<CategoryGroup>();
            var first = 0;
            for (var i = 1; i <= keys.Count; i++)
            {
                if (i < keys.Count && Enumerable.Range(0, level + 1).All(l => Equals(keys[i][l], keys[first][l]))) continue;
                runs.Add(new CategoryGroup(Text(chart, first, level), first, i - first));
                first = i;
            }
            groups.Add(runs);
        }
        // Mostly groups of one category: not ordered by the outer fields
        if (groups[0].Count > keys.Count * 0.75) return null;

        return new CategoryLevels([.. keys.Select((_, i) => Text(chart, i, depth - 1))], groups);
    }

    // The text of a category's value for a field, with the visual's labels when the chart has them
    private static string Text(ChartData chart, int category, int level) =>
        chart.CategoryLabels.Count > category && chart.CategoryLabels[category].Count > level
            ? chart.CategoryLabels[category][level]
            : ValueFormatter.Display(chart.CategoryValues[category][level]);
}
