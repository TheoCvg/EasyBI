using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// The visual types and their definitions (<see cref="VisualTypeDefinition"/>), in the order of the designer's gallery.
/// A type it does not know (from a report written by another application) gets a definition with the default rules.
/// </summary>
public sealed class VisualTypeCatalog
{
    private readonly Dictionary<VisualType, VisualTypeDefinition> _definitions;

    public VisualTypeCatalog(IEnumerable<VisualTypeDefinition> definitions)
    {
        Definitions = [.. definitions];
        _definitions = Definitions.ToDictionary(d => d.Type);
    }

    /// <summary>EasyBI's own types.</summary>
    public static VisualTypeCatalog Default { get; } = new(BuiltIn());

    public IReadOnlyList<VisualTypeDefinition> Definitions { get; }

    public VisualTypeDefinition Get(VisualType type) => _definitions.GetValueOrDefault(type) ?? new(type, () => type.Id);

    private static IEnumerable<VisualTypeDefinition> BuiltIn()
    {
        var category = new VisualWell { MinFields = 1, MaxFields = 1, Caption = () => Strings.WellCategory };
        var value = new VisualWell { MinFields = 1, MaxFields = 1, Caption = () => Strings.WellValue };
        var rows = new VisualWell { MaxFields = 3, Caption = () => Strings.WellRows };

        yield return Chart(VisualType.Bar, () => Strings.VisualTypeBar, VisualTypeGroup.Comparison);
        yield return Chart(VisualType.StackedBar, () => Strings.VisualTypeStackedBar, VisualTypeGroup.Comparison);
        yield return Chart(VisualType.Scatter, () => Strings.VisualTypeScatter, VisualTypeGroup.Comparison);
        yield return Chart(VisualType.Line, () => Strings.VisualTypeLine, VisualTypeGroup.Trend);
        yield return Chart(VisualType.Area, () => Strings.VisualTypeArea, VisualTypeGroup.Trend);
        foreach (var (type, caption) in new (VisualType, Func<string>)[]
                 {
                     (VisualType.Pie, () => Strings.VisualTypePie),
                     (VisualType.Donut, () => Strings.VisualTypeDonut),
                 })
        {
            yield return Chart(type, caption, VisualTypeGroup.Composition) with
            {
                Axis = category,
                Legend = VisualWell.Unused,
                Values = value,
                Validate = v => v.Axis.Count != 1 || v.Legend is not null || v.Values.Count != 1 ? Strings.ValidationPie : null,
            };
        }
        yield return Chart(VisualType.Waterfall, () => Strings.VisualTypeWaterfall, VisualTypeGroup.Composition) with
        {
            Axis = new() { MinFields = 1, MaxFields = 3 },
            Legend = VisualWell.Unused,
            Values = value,
            Validate = v => v.Axis.Count == 0 || v.Legend is not null || v.Values.Count != 1 ? Strings.ValidationWaterfall : null,
        };
        yield return new(VisualType.Kpi, () => Strings.VisualTypeKpi)
        {
            Group = VisualTypeGroup.IndicatorsAndTables,
            Axis = VisualWell.Unused,
            Legend = VisualWell.Unused,
            Format = FormatTarget.Kpi,
            Validate = v => v.Axis.Count > 0 || v.Legend is not null ? Strings.ValidationKpi : null,
        };
        yield return new(VisualType.Gauge, () => Strings.VisualTypeGauge)
        {
            Group = VisualTypeGroup.IndicatorsAndTables,
            Axis = VisualWell.Unused,
            Legend = VisualWell.Unused,
            Values = value,
            Format = FormatTarget.Kpi,
            Validate = v => v.Axis.Count > 0 || v.Legend is not null || v.Values.Count != 1 ? Strings.ValidationGauge : null,
        };
        yield return new(VisualType.Table, () => Strings.VisualTypeTable)
        {
            Group = VisualTypeGroup.IndicatorsAndTables,
            Axis = rows,
            Legend = VisualWell.Unused,
            Values = new(),
            Format = FormatTarget.Grid,
            Validate = v => v.Axis.Count == 0 && v.Legend is null && v.Values.Count == 0 ? Strings.ValidationEmptyTable : null,
        };
        yield return new(VisualType.Matrix, () => Strings.VisualTypeMatrix)
        {
            Group = VisualTypeGroup.IndicatorsAndTables,
            Axis = rows,
            Legend = new() { MaxFields = 1, Caption = () => Strings.WellColumns },
            Format = FormatTarget.Grid,
        };
        // Content: written in the dashboard, not built from fields
        yield return Content(VisualType.Text, () => Strings.VisualTypeText);
        yield return Content(VisualType.Image, () => Strings.VisualTypeImage) with
        {
            Validate = v => v.Image is null ? Strings.ValidationNoImage : null,
        };
    }

    private static VisualTypeDefinition Chart(VisualType type, Func<string> caption, VisualTypeGroup group) =>
        new(type, caption) { Group = group, HasTooltips = true };

    private static VisualTypeDefinition Content(VisualType type, Func<string> caption) =>
        new(type, caption) { Axis = VisualWell.Unused, Legend = VisualWell.Unused, Values = VisualWell.Unused };
}
