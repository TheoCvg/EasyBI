using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// The visual types and their definitions (<see cref="VisualTypeDefinition"/>), in the order of the designer's gallery:
/// EasyBI's, then those an application <see cref="Add"/>s. A type it does not know (from a report written by another
/// application) gets a definition with the default rules.
/// </summary>
public sealed class VisualTypeCatalog
{
    private readonly List<VisualTypeDefinition> _definitions;
    private readonly bool _isReadOnly;

    /// <summary>A catalog of EasyBI's types, to which an application adds its own.</summary>
    public VisualTypeCatalog() : this(BuiltIn(), isReadOnly: false)
    {
    }

    private VisualTypeCatalog(IEnumerable<VisualTypeDefinition> definitions, bool isReadOnly)
    {
        _definitions = [.. definitions];
        _isReadOnly = isReadOnly;
    }

    /// <summary>EasyBI's own types, read-only: what is used without an application's catalog.</summary>
    public static VisualTypeCatalog Default { get; } = new(BuiltIn(), isReadOnly: true);

    public IReadOnlyList<VisualTypeDefinition> Definitions => _definitions;

    public bool Contains(VisualType type) => _definitions.Any(d => d.Type == type);

    public VisualTypeDefinition Get(VisualType type) => _definitions.Find(d => d.Type == type) ?? new(type, () => type.Id);

    /// <summary>
    /// Declares a type of the application. Its id has a prefix, such as <c>company.treemap</c>, so that it never takes
    /// the name of a type EasyBI adds later.
    /// </summary>
    /// <exception cref="ArgumentException">The id has no prefix.</exception>
    /// <exception cref="InvalidOperationException">The type is already declared (<see cref="Replace"/> changes it).</exception>
    public VisualTypeCatalog Add(VisualTypeDefinition definition)
    {
        ThrowIfReadOnly();
        var id = definition.Type.Id;
        if (id.IndexOf('.') is <= 0 || id.EndsWith('.'))
            throw new ArgumentException($"The id of a visual type declared by an application has a prefix, such as 'company.{id}': '{id}' has none.", nameof(definition));
        if (Contains(definition.Type)) throw new InvalidOperationException($"The visual type '{id}' is already declared: replace its definition instead.");
        _definitions.Add(definition);
        return this;
    }

    /// <summary>Changes the definition of a type, EasyBI's or the application's (its name, its section, its rules...).</summary>
    /// <exception cref="InvalidOperationException">The catalog does not have the type.</exception>
    public VisualTypeCatalog Replace(VisualTypeDefinition definition)
    {
        ThrowIfReadOnly();
        var index = _definitions.FindIndex(d => d.Type == definition.Type);
        if (index < 0) throw new InvalidOperationException($"The visual type '{definition.Type}' is not declared: add it instead.");
        _definitions[index] = definition;
        return this;
    }

    /// <summary>
    /// Takes a type out of the designer's gallery (its <see cref="VisualTypeDefinition.Group"/> becomes null): the end
    /// user no longer picks it, the visuals of this type still show.
    /// </summary>
    public VisualTypeCatalog Hide(VisualType type) => Replace(Get(type) with { Group = null });

    private void ThrowIfReadOnly()
    {
        if (_isReadOnly) throw new InvalidOperationException("The default catalog cannot change: change the catalog of the application (EasyBIContext.VisualTypes).");
    }

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
            Axis = rows with { MaxFields = 4 },
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
