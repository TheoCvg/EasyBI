using System.Globalization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>Enchaîne : définition du visuel → requête → exécution → données pivotées.</summary>
public static class VisualPipeline
{
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

        var categories = new List<string>();
        var categoryIndex = new Dictionary<string, int>();
        var series = new List<(string Name, string? Format, Dictionary<int, double?> Values)>();
        var seriesIndex = new Dictionary<string, int>();

        foreach (var row in result.Rows)
        {
            var category = axis.Length == 0 ? "Total" : string.Join(" / ", axis.Select(i => ValueFormatter.Display(row[i])));
            if (!categoryIndex.TryGetValue(category, out var c))
            {
                categoryIndex[category] = c = categories.Count;
                categories.Add(category);
            }

            foreach (var m in measures)
            {
                var column = result.Columns[m];
                var name = legend < 0 ? column.Caption
                    : measures.Length == 1 ? ValueFormatter.Display(row[legend])
                    : $"{ValueFormatter.Display(row[legend])} · {column.Caption}";
                if (!seriesIndex.TryGetValue(name, out var s))
                {
                    seriesIndex[name] = s = series.Count;
                    series.Add((name, column.Format, []));
                }
                series[s].Values[c] = row[m] is null ? null : Convert.ToDouble(row[m], CultureInfo.InvariantCulture);
            }
        }

        var caption = axis.Length == 0 ? null : string.Join(" / ", axis.Select(i => result.Columns[i].Caption));
        return new ChartData(
            caption,
            categories,
            series.Select(s => new ChartSeries(
                s.Name,
                Enumerable.Range(0, categories.Count).Select(i => s.Values.GetValueOrDefault(i)).ToArray(),
                s.Format)).ToArray());
    }
}

public static class ValueFormatter
{
    public static string Display(object? value) => value switch
    {
        null => "(vide)",
        DateTime d => d.ToString("d", CultureInfo.CurrentCulture),
        bool b => b ? "Oui" : "Non",
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? "",
    };

    public static string Format(double value, string? format) =>
        value.ToString(string.IsNullOrEmpty(format) ? "#,##0.##" : format, CultureInfo.CurrentCulture);
}
