namespace EasyBI.Abstractions;

/// <summary>A series ready to draw: one (possibly missing) value per category.</summary>
public sealed record ChartSeries(string Name, IReadOnlyList<double?> Values, string? Format)
{
    /// <summary>Value of the Legend dimension that produced this series (null without a legend).</summary>
    public object? LegendValue { get; init; }

    /// <summary>How to draw the series in a cartesian chart, already resolved from its measure and the visual.</summary>
    public MarkType Mark { get; init; } = MarkType.Bar;

    /// <summary>True when the series uses the second value axis, on the right.</summary>
    public bool SecondaryAxis { get; init; }
}

/// <summary>
/// Pre-pivoted data for categorical charts. Every renderer receives exactly the
/// same shape, which keeps rendering consistent from one library to another.
/// </summary>
public sealed record ChartData(string? CategoryCaption, IReadOnlyList<string> Categories, IReadOnlyList<ChartSeries> Series)
{
    /// <summary>Raw values of the axis dimensions for each category (same order as <see cref="Categories"/>).</summary>
    public IReadOnlyList<IReadOnlyList<object?>> CategoryValues { get; init; } = [];
}

/// <summary>Palette shared by all renderers (hexadecimal #RRGGBB colors).</summary>
public sealed record BiTheme(IReadOnlyList<string> Palette)
{
    public static BiTheme Default { get; } = new([
        "#2E6BE6", "#E8833A", "#2BA37B", "#D64550", "#8A5CD6",
        "#C9A227", "#3AA6C9", "#8C6D52", "#D46BAE", "#6B7A8F"]);

    public string ColorAt(int index) => Palette[index % Palette.Count];
}

/// <summary>What a renderer can draw. The designer uses it to disable unavailable options.</summary>
public sealed record RendererCapabilities(IReadOnlySet<MarkType> Marks);

/// <summary>Element picked by the user in a visual (click), source of a cross-filter.</summary>
public abstract record DataSelection;

/// <summary>A category of <see cref="ChartData"/>, optionally narrowed to one series (legend).</summary>
public sealed record CategorySelection(int Category, int? Series = null) : DataSelection;

/// <summary>A row of the <see cref="ResultSet"/> (table).</summary>
public sealed record RowSelection(int Row) : DataSelection;

/// <summary>Everything a renderer needs to draw a visual.</summary>
public sealed record RenderRequest(VisualDefinition Visual, ResultSet Result, ChartData Chart, BiTheme Theme)
{
    /// <summary>
    /// Called by the renderer when the user clicks a data element.
    /// Null when interactivity is disabled (designer preview, export...).
    /// </summary>
    public Action<DataSelection>? Select { get; init; }
}
