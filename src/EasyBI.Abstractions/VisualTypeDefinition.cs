namespace EasyBI.Abstractions;

/// <summary>The sections of the designer's gallery of visual types, by what a visual shows.</summary>
public enum VisualTypeGroup { Comparison, Trend, Composition, IndicatorsAndTables }

/// <summary>
/// What a format is edited for: the cells of a table or a matrix, the value of a KPI, or the points of a chart. The
/// designer's format editor only shows what applies; what it hides is kept, for a change of the visual's type.
/// </summary>
public enum FormatTarget { Grid, Kpi, Chart }

/// <summary>
/// What a visual type takes in one of its wells (axis, legend or values): whether it uses it, how many fields, and the
/// name the designer gives it.
/// </summary>
public sealed record VisualWell
{
    /// <summary>A well the type does not use: the designer refuses fields on it.</summary>
    public static VisualWell Unused { get; } = new() { IsUsed = false };

    public bool IsUsed { get; init; } = true;

    /// <summary>The fields the type needs to be drawn.</summary>
    public int MinFields { get; init; }

    /// <summary>The fields the type takes at most; null without limit.</summary>
    public int? MaxFields { get; init; }

    /// <summary>The name of the well for the type; null for its usual name (Axis, Legend, Values).</summary>
    public Func<string>? Caption { get; init; }
}

/// <summary>
/// Everything EasyBI needs to know about a <see cref="VisualType"/> besides drawing it (a renderer's job): its name, the
/// fields it takes, its settings and its rules. The icon the designer shows for it is a WPF resource (EasyBI.Wpf's
/// <c>VisualTypeIcons</c>).
/// </summary>
public sealed record VisualTypeDefinition(VisualType Type, Func<string> Caption)
{
    /// <summary>The section of the designer's gallery offering the type; null when the gallery does not offer it.</summary>
    public VisualTypeGroup? Group { get; init; }

    /// <summary>The categories: up to three dimensions by default.</summary>
    public VisualWell Axis { get; init; } = new() { MaxFields = 3 };

    /// <summary>The dimension splitting the values into series.</summary>
    public VisualWell Legend { get; init; } = new() { MaxFields = 1 };

    /// <summary>The measures: at least one by default.</summary>
    public VisualWell Values { get; init; } = new() { MinFields = 1 };

    /// <summary>True when the type shows tooltips, which the end user can enrich with other measures.</summary>
    public bool HasTooltips { get; init; }

    /// <summary>What the format of the type's values is edited for.</summary>
    public FormatTarget Format { get; init; } = FormatTarget.Chart;

    /// <summary>
    /// Settings of the type, offered by the designer whatever the renderer drawing it, and saved in
    /// <see cref="VisualDefinition.Extensions"/> under the type's id (EasyBI.Core's <c>GetTypeOption</c> reads them).
    /// </summary>
    public IReadOnlyList<RendererOption> Options { get; init; } = [];

    /// <summary>
    /// A rule of the type: a message for the end user, or null when the visual can be drawn. It is checked before the
    /// number of fields of each well, so that its message, more precise, comes first.
    /// </summary>
    public Func<VisualDefinition, string?>? Validate { get; init; }
}
