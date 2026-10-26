using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>Chains: visual definition → query → execution → pivoted data.</summary>
public static class VisualPipeline
{
    private static readonly HashSet<VisualType> Cartesian =
        new HashSet<VisualType> { VisualType.Bar, VisualType.StackedBar, VisualType.Line, VisualType.Area, VisualType.Scatter };

    /// <summary>True for the types that show their own content (text, image) instead of querying a model.</summary>
    public static bool IsContent(this VisualType type) => type == VisualType.Text || type == VisualType.Image;

    /// <summary>True for the types drawn on category and value axes, which can be mixed in one chart.</summary>
    public static bool IsCartesian(this VisualType type) => Cartesian.Contains(type);

    /// <summary>True for the types drawn as slices of a circle (one category per slice): a pie and a donut.</summary>
    public static bool IsCircular(this VisualType type) => type == VisualType.Pie || type == VisualType.Donut;

    /// <summary>The chart type of a measure's series: its own type in a cartesian chart, otherwise the visual's.</summary>
    public static VisualType SeriesType(this VisualDefinition visual, MeasureRef measure) =>
        visual.Type.IsCartesian() && measure.SeriesType is { } type && type.IsCartesian() ? type : visual.Type;

    /// <summary>Every type a renderer must support to draw the visual: its own and those of its measures.</summary>
    public static IReadOnlyCollection<VisualType> TypesOf(VisualDefinition visual) =>
        visual.Values.Select(visual.SeriesType).Append(visual.Type).ToHashSet();

    /// <summary>Render request of a text or image visual: no query, empty data.</summary>
    public static RenderRequest PrepareContent(VisualDefinition visual, BiTheme? theme = null) =>
        new(visual, new ResultSet([], []), new ChartData(null, [], []), theme ?? BiTheme.Default);

    /// <summary>
    /// The query of a visual; those of the end user's calculated <c>measures</c> it shows go with it, with those they use
    /// (and, with the model's <c>metadata</c>, those its formula measures use). The visual <paramref name="types"/> tell
    /// whether it has tooltips, whose measures are queried with it (EasyBI's types when null).
    /// </summary>
    public static AnalyticQuery ToQuery(this VisualDefinition visual, IEnumerable<CalculatedMeasure>? measures = null, ModelMetadata? metadata = null,
        VisualTypeCatalog? types = null) =>
        visual.ToQuery(measures, metadata, Tooltips.Has(visual.Type, types));

    /// <summary>The query of a visual, with the measures of its tooltips when it <paramref name="hasTooltips"/>.</summary>
    public static AnalyticQuery ToQuery(this VisualDefinition visual, IEnumerable<CalculatedMeasure>? measures, ModelMetadata? metadata, bool hasTooltips)
    {
        var groupBy = visual.Legend is null ? visual.Axis : [.. visual.Axis, visual.Legend];
        // A chart's tooltips may show other measures: they are queried with it, not drawn
        IEnumerable<MeasureRef> tooltip = hasTooltips ? visual.Tooltip?.Values ?? [] : [];
        // Shown, in a tooltip, or filtered on, and what they use
        var names = visual.Values.Concat(tooltip).Select(v => v.Measure)
            .Concat(visual.Filters.Where(f => f.Operator.IsComparison()).Select(f => f.Field));
        var used = measures is null ? null : MeasureContext.Needed(names, measures.Where(m => m.Model == visual.Model), metadata);
        return new AnalyticQuery(
            groupBy,
            // Chart types, axes and formats only change the drawing: changing them must not query again
            visual.Values.Concat(tooltip)
                .Select(v => v with { SeriesType = null, SecondaryAxis = false, Format = null })
                .DistinctBy(v => v.Key)
                .ToArray(),
            visual.Filters,
            visual.Sort is null ? null : [visual.Sort],
            visual.Top,
            used is { Count: > 0 } ? used : null);
    }

    /// <summary>
    /// The request for drawing <paramref name="visual"/> with the result of a previous query. Used when only display
    /// settings changed (title, chart types, axes...): the chart data is rebuilt, the source is not queried again.
    /// </summary>
    public static RenderRequest Redraw(RenderRequest previous, VisualDefinition visual) =>
        previous with { Visual = visual, Chart = ChartDataBuilder.Build(visual, previous.Result) };

    /// <summary>
    /// What <paramref name="visual"/> asks its model for: the end user's calculated <c>measures</c> it uses, whether its
    /// type has tooltips (EasyBI's <paramref name="types"/> when null), today's date and EasyBI's cultures.
    /// </summary>
    public static VisualDataRequest ToDataRequest(this VisualDefinition visual, ModelMetadata? metadata = null,
        IEnumerable<CalculatedMeasure>? measures = null, VisualTypeCatalog? types = null)
    {
        var hasTooltips = Tooltips.Has(visual.Type, types);
        return new(visual)
        {
            Measures = measures is null ? [] : visual.ToQuery(measures, metadata, hasTooltips).Calculations ?? [],
            HasTooltips = hasTooltips,
            UICulture = EasyBILocalization.UICulture.Name,
            FormatCulture = EasyBILocalization.FormatCulture.Name,
        };
    }

    /// <summary>Asks the model for a visual's data, with the end user's calculated <c>measures</c> it may show.</summary>
    public static async Task<RenderRequest> PrepareAsync(
        ISemanticModel model,
        VisualDefinition visual,
        BiTheme? theme = null,
        IEnumerable<CalculatedMeasure>? measures = null,
        VisualTypeCatalog? types = null,
        CancellationToken cancellationToken = default)
    {
        var data = await model.GetDataAsync(visual.ToDataRequest(model.Metadata, measures, types), cancellationToken).ConfigureAwait(false);
        return Prepare(visual, data, theme);
    }

    /// <summary>The request for drawing a visual with its data, wherever they come from.</summary>
    public static RenderRequest Prepare(VisualDefinition visual, VisualData data, BiTheme? theme = null) =>
        new(visual, data.Result, ChartDataBuilder.Build(visual, data.Result), theme ?? BiTheme.Default) { Matrix = data.Matrix };
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
        var series = new List<(string Name, string? Format, object? LegendValue, int Measure, Dictionary<int, double?> Values)>();
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
                        : measures.Length == 1 ? visual.Legend!.Display(legendValue)
                        : $"{visual.Legend!.Display(legendValue)} · {column.Caption}";
                    seriesIndex[seriesKey] = s = series.Count;
                    series.Add((name, column.Format, legendValue, m, []));
                }
                var value = row[measures[m]];
                series[s].Values[c] = value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
        }

        var caption = axis.Length == 0 ? null : string.Join(" / ", axis.Select(i => result.Columns[i].Caption));
        // The visual's labels replace the values they name
        var labels = categories.Select(c => (IReadOnlyList<string>)[.. c.Select((value, i) => visual.Axis[i].Display(value))]).ToArray();
        return new ChartData(
            caption,
            labels.Select(c => c.Count == 0 ? Strings.Total : string.Join(" / ", c)).ToArray(),
            series.Select(s => new ChartSeries(
                s.Name,
                Enumerable.Range(0, categories.Count).Select(i => s.Values.GetValueOrDefault(i)).ToArray(),
                s.Format)
            {
                LegendValue = s.LegendValue,
                SeriesType = visual.SeriesType(visual.Values[s.Measure]),
                SecondaryAxis = visual.Type.IsCartesian() && visual.Values[s.Measure].SecondaryAxis,
                MeasureKey = visual.Values[s.Measure].Key,
            }).ToArray())
        {
            CategoryValues = categories,
            CategoryLabels = labels,
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
