using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>Chains: visual definition → query → execution → pivoted data.</summary>
public static class VisualPipeline
{
    /// <summary>True for the marks that show their own content (text, image) instead of querying a model.</summary>
    public static bool IsContent(this MarkType mark) => mark is MarkType.Text or MarkType.Image;

    /// <summary>Render request of a text or image visual: no query, empty data.</summary>
    public static RenderRequest PrepareContent(VisualDefinition visual, BiTheme? theme = null) =>
        new(visual, new ResultSet([], []), new ChartData(null, [], []), theme ?? BiTheme.Default);

    public static AnalyticQuery ToQuery(this VisualDefinition visual)
    {
        var groupBy = visual.Legend is null ? visual.Axis : [.. visual.Axis, visual.Legend];
        return new AnalyticQuery(
            groupBy,
            visual.Values,
            visual.Filters,
            visual.Sort is null ? null : [visual.Sort],
            visual.Top);
    }

    public static async Task<RenderRequest> PrepareAsync(
        ISemanticModel model,
        VisualDefinition visual,
        BiTheme? theme = null,
        CancellationToken cancellationToken = default)
    {
        var result = await model.ExecuteAsync(visual.ToQuery(), cancellationToken).ConfigureAwait(false);
        return new RenderRequest(visual, result, ChartDataBuilder.Build(visual, result), theme ?? BiTheme.Default);
    }
}

public static class ChartDataBuilder
{
    public static ChartData Build(VisualDefinition visual, ResultSet result)
    {
        var axis = visual.Axis.Select(a => result.IndexOf(a.Key)).ToArray();
        var legend = visual.Legend is null ? -1 : result.IndexOf(visual.Legend.Key);
        var measures = visual.Values.Select(v => result.IndexOf(v.Key)).ToArray();

        // Categories and series are identified by their raw values, not by their label:
        // two distinct values with the same display text do not merge.
        var categories = new List<object?[]>();
        var categoryIndex = new Dictionary<object?[], int>(ValuesComparer.Instance);
        var series = new List<(string Name, string? Format, object? LegendValue, Dictionary<int, double?> Values)>();
        var seriesIndex = new Dictionary<object?[], int>(ValuesComparer.Instance);

        foreach (var row in result.Rows)
        {
            var category = axis.Select(i => row[i]).ToArray();
            if (!categoryIndex.TryGetValue(category, out var c))
            {
                categoryIndex[category] = c = categories.Count;
                categories.Add(category);
            }

            for (var m = 0; m < measures.Length; m++)
            {
                var column = result.Columns[measures[m]];
                var legendValue = legend < 0 ? null : row[legend];
                object?[] seriesKey = [legendValue, m];
                if (!seriesIndex.TryGetValue(seriesKey, out var s))
                {
                    var name = legend < 0 ? column.Caption
                        : measures.Length == 1 ? ValueFormatter.Display(legendValue)
                        : $"{ValueFormatter.Display(legendValue)} · {column.Caption}";
                    seriesIndex[seriesKey] = s = series.Count;
                    series.Add((name, column.Format, legendValue, []));
                }
                var value = row[measures[m]];
                series[s].Values[c] = value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
        }

        var caption = axis.Length == 0 ? null : string.Join(" / ", axis.Select(i => result.Columns[i].Caption));
        return new ChartData(
            caption,
            categories.Select(c => c.Length == 0 ? Strings.Total : string.Join(" / ", c.Select(ValueFormatter.Display))).ToArray(),
            series.Select(s => new ChartSeries(
                s.Name,
                Enumerable.Range(0, categories.Count).Select(i => s.Values.GetValueOrDefault(i)).ToArray(),
                s.Format) { LegendValue = s.LegendValue }).ToArray())
        {
            CategoryValues = categories,
        };
    }

    private sealed class ValuesComparer : IEqualityComparer<object?[]>
    {
        public static ValuesComparer Instance { get; } = new();

        public bool Equals(object?[]? x, object?[]? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

        public int GetHashCode(object?[] values)
        {
            var hash = new HashCode();
            foreach (var value in values) hash.Add(value);
            return hash.ToHashCode();
        }
    }
}

/// <summary>Display texts of values, in the language and formats of <see cref="EasyBILocalization"/>.</summary>
public static class ValueFormatter
{
    public static string Display(object? value) => value switch
    {
        null => Strings.Blank,
        DateTime d => d.ToString("d", EasyBILocalization.FormatCulture),
        DateTimeOffset d => d.ToString("d", EasyBILocalization.FormatCulture),
        DateOnly d => d.ToString("d", EasyBILocalization.FormatCulture),
        DatePeriod { Grain: DateGrain.Quarter } p => Strings.Format(Strings.QuarterLabel, p.Key / 10, p.Key % 10),
        bool b => b ? Strings.Yes : Strings.No,
        IFormattable f => f.ToString(null, EasyBILocalization.FormatCulture),
        _ => value.ToString() ?? "",
    };

    public static string Format(double value, string? format) =>
        value.ToString(string.IsNullOrEmpty(format) ? "#,##0.##" : format, EasyBILocalization.FormatCulture);
}
