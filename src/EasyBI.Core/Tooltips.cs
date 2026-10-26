using System.Text.RegularExpressions;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// The tooltips of charts: their default content (<see cref="Build"/>), following the visual's
/// <see cref="TooltipSettings"/>, and what a connector calls to get the content to show (<see cref="TooltipFor"/>).
/// </summary>
public static class Tooltips
{
    /// <summary>
    /// The visual types with tooltips (<see cref="VisualTypeDefinition.HasTooltips"/> in <paramref name="types"/>,
    /// EasyBI's types when null): EasyBI's charts.
    /// </summary>
    public static bool Has(VisualType type, VisualTypeCatalog? types = null) => (types ?? VisualTypeCatalog.Default).Get(type).HasTooltips;

    /// <summary>
    /// The content to show for a category and the hovered series: the host's (<see cref="RenderRequest.Tooltip"/>,
    /// with the application's changes), else EasyBI's default; null shows no tooltip. A connector calls this.
    /// </summary>
    public static TooltipContent? TooltipFor(this RenderRequest request, int category, IReadOnlyList<int> series) =>
        request.Tooltip is { } host ? host(category, series) : Build(request, category, series);

    /// <summary>
    /// EasyBI's content for a category (a slice, a step: the last category of a waterfall is its total) and the
    /// hovered series: the category as title; each series with its value, its share of the total if asked, and its
    /// color; the visual's tooltip measures; or the template's text. Null when the visual hides its tooltips.
    /// </summary>
    public static TooltipContent? Build(RenderRequest request, int category, IReadOnlyList<int> series)
    {
        var settings = request.Visual.Tooltip ?? new TooltipSettings();
        if (settings.IsHidden) return null;
        var chart = request.Visual.Type == VisualType.Waterfall ? Waterfall.Of(request.Chart).Chart : request.Chart;
        if (category < 0 || category >= chart.Categories.Count) return null;

        var formatting = ChartFormatting.For(request);
        var items = new List<TooltipItem>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["category"] = chart.Categories[category] };
        foreach (var index in series.Where(i => i >= 0 && i < chart.Series.Count).Distinct())
        {
            var source = chart.Series[index];
            var value = source.Values.ElementAtOrDefault(category);
            var text = value is { } number ? ValueFormatter.Format(number, source.Format) : "—";
            var percent = value is { } part && source.Values.OfType<double>().Sum() is var total && total != 0
                ? ValueFormatter.Format(part / total, "P1")
                : "";
            if (settings.ShowPercent && percent.Length > 0) text += $" ({percent})";
            var color = category < request.Chart.Categories.Count ? formatting.PointColor(index, category) : null;
            color ??= request.Visual.Type.IsCircular() ? request.Theme.ColorAt(category) : request.Theme.ColorAt(index);
            items.Add(new TooltipItem(settings.HideSeries ? "" : source.Name, text, color));
            fields.TryAdd("series", source.Name);
            fields.TryAdd("value", value is { } shown ? ValueFormatter.Format(shown, source.Format) : "");
            fields.TryAdd("percent", percent);
        }

        // The measures of the visual and of its tooltip, for the row of the category (and of the first series' legend)
        if (Row(request, category, series.FirstOrDefault()) is { } row)
        {
            var visualKeys = request.Visual.Values.Select(v => v.Key).ToHashSet();
            foreach (var column in request.Result.Columns.Where(c => c.Role == FieldKind.Measure))
            {
                var cell = row[request.Result.IndexOf(column.Key)];
                var text = cell is null ? "—" : ValueFormatter.Format(Convert.ToDouble(cell, System.Globalization.CultureInfo.InvariantCulture), column.Format);
                fields.TryAdd(column.Caption, text);
                if (MeasureName(request.Visual, column.Key) is { } name) fields.TryAdd(name, text);
                if (!visualKeys.Contains(column.Key) && settings.Values.Any(v => v.Key == column.Key))
                    items.Add(new TooltipItem(column.Caption, text));
            }
        }

        var content = new TooltipContent(settings.HideCategory ? null : chart.Categories[category], items);
        return string.IsNullOrWhiteSpace(settings.Template) ? content : content with { Text = Fill(settings.Template, fields) };
    }

    /// <summary>The fields a template can use for a visual: category, series, value, percent and its measures' names.</summary>
    public static IReadOnlyList<string> Fields(VisualDefinition visual) =>
        ["category", "series", "value", "percent", .. visual.Values.Concat(visual.Tooltip?.Values ?? []).Select(v => v.Measure).Distinct()];

    // The result row of a category, and of a series' legend value when the visual has a legend
    private static object?[]? Row(RenderRequest request, int category, int series)
    {
        var visual = request.Visual;
        var result = request.Result;
        if (category >= request.Chart.CategoryValues.Count) return null;
        var keys = request.Chart.CategoryValues[category];
        var columns = visual.Axis.Select(a => result.IndexOf(a.Key)).ToArray();
        var legend = visual.Legend is { } l ? result.IndexOf(l.Key) : -1;
        var legendValue = legend >= 0 ? InvariantValue.Format(request.Chart.Series.ElementAtOrDefault(series)?.LegendValue) : null;
        return result.Rows.FirstOrDefault(row =>
            columns.Select((c, i) => c >= 0 && InvariantValue.Format(row[c]) == InvariantValue.Format(keys.ElementAtOrDefault(i))).All(m => m)
            && (legend < 0 || InvariantValue.Format(row[legend]) == legendValue));
    }

    private static string? MeasureName(VisualDefinition visual, string key) =>
        visual.Values.Concat(visual.Tooltip?.Values ?? []).FirstOrDefault(v => v.Key == key)?.Measure;

    // {field} is replaced by its value; an unknown field stays as typed
    private static string Fill(string template, IReadOnlyDictionary<string, string> fields) =>
        FieldPattern.Replace(template, match => fields.TryGetValue(match.Groups[1].Value.Trim(), out var value) ? value : match.Value);

    private static readonly Regex FieldPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
}
