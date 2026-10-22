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

    /// <summary>The <see cref="MeasureRef.Key"/> of the measure the series shows (its format applies to it).</summary>
    public string? MeasureKey { get; init; }
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

/// <summary>
/// Colors shared by all renderers, as hexadecimal #RRGGBB or #AARRGGBB strings: the series palette, and the colors of
/// the chart's text, axes, grid lines and background. A null color lets the renderer use its own default.
/// </summary>
public sealed record BiTheme(IReadOnlyList<string> Palette)
{
    /// <summary>Text of legends, titles and tooltips.</summary>
    public string? Foreground { get; init; }

    /// <summary>Axis labels and other secondary text.</summary>
    public string? MutedForeground { get; init; }

    /// <summary>Grid lines and axis lines.</summary>
    public string? GridLine { get; init; }

    /// <summary>What the chart is drawn on (tooltips, slice borders).</summary>
    public string? Background { get; init; }

    /// <summary>The theme's colors for good, warning and bad values (conditional formats); null keeps EasyBI's.</summary>
    public string? Success { get; init; }

    public string? Warning { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// The color a format names: a theme color (<see cref="ThemeColors"/>) or <c>#RRGGBB</c> as is; null for no color.
    /// </summary>
    public string? Resolve(string? color) => color switch
    {
        null or "" => null,
        ThemeColors.Accent => ColorAt(0),
        ThemeColors.Success => Success ?? "#2BA37B",
        ThemeColors.Warning => Warning ?? "#D97706",
        ThemeColors.Error => Error ?? "#D64550",
        ThemeColors.Muted => MutedForeground ?? "#6B7280",
        _ => color,
    };

    public static BiTheme Default { get; } = new([
        "#2E6BE6", "#E8833A", "#2BA37B", "#D64550", "#8A5CD6",
        "#C9A227", "#3AA6C9", "#8C6D52", "#D46BAE", "#6B7A8F"]);

    public string ColorAt(int index) => Palette[index % Palette.Count];
}

/// <summary>What a renderer can draw. The designer uses it to disable unavailable options.</summary>
public sealed record RendererCapabilities(IReadOnlySet<MarkType> Marks)
{
    /// <summary>
    /// Settings specific to the renderer, offered to the end user by the designer and saved in
    /// <see cref="VisualDefinition.Extensions"/> under the renderer's name.
    /// </summary>
    public IReadOnlyList<RendererOption> Options { get; init; } = [];
}

/// <summary>Element picked by the user in a visual (click), source of a cross-filter.</summary>
public abstract record DataSelection;

/// <summary>A category of <see cref="ChartData"/>, optionally narrowed to one series (legend).</summary>
public sealed record CategorySelection(int Category, int? Series = null) : DataSelection;

/// <summary>A row of the <see cref="ResultSet"/> (table).</summary>
public sealed record RowSelection(int Row) : DataSelection;

/// <summary>
/// An element of a <see cref="MatrixData"/>: a cell (row and column), a whole row (its header or total, no column) or a
/// whole column (its total, no row).
/// </summary>
public sealed record MatrixSelection(int? Row, int? Column) : DataSelection;

/// <summary>A row or a column of a matrix: the value of each of its dimensions, and how it reads.</summary>
public sealed record MatrixMember(IReadOnlyList<object?> Values, IReadOnlyList<string> Labels);

/// <summary>A measure shown in each cell of a matrix.</summary>
public sealed record MatrixMeasure(string Key, string Caption, string? Format);

/// <summary>
/// Pre-pivoted data of a <see cref="MarkType.Matrix"/>: rows (the axis), columns (the legend, none without one) and,
/// for each measure, the value of every cell and the totals. Each total is the measure computed on all the rows it
/// covers (an average stays an average), not a sum of cells.
/// </summary>
public sealed record MatrixData(
    IReadOnlyList<string> RowCaptions,
    string? ColumnCaption,
    IReadOnlyList<MatrixMember> Rows,
    IReadOnlyList<MatrixMember> Columns,
    IReadOnlyList<MatrixMeasure> Measures)
{
    /// <summary>Values by row, column and measure: <c>Cells[row][column][measure]</c>.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<double?>>> Cells { get; init; } = [];

    /// <summary>Total of each row (all its columns): <c>RowTotals[row][measure]</c>.</summary>
    public IReadOnlyList<IReadOnlyList<double?>> RowTotals { get; init; } = [];

    /// <summary>Total of each column (all the rows): <c>ColumnTotals[column][measure]</c>.</summary>
    public IReadOnlyList<IReadOnlyList<double?>> ColumnTotals { get; init; } = [];

    /// <summary>Total of the whole matrix, by measure.</summary>
    public IReadOnlyList<double?> GrandTotals { get; init; } = [];
}

/// <summary>Everything a renderer needs to draw a visual.</summary>
public sealed record RenderRequest(VisualDefinition Visual, ResultSet Result, ChartData Chart, BiTheme Theme)
{
    /// <summary>
    /// Called by the renderer when the user clicks a data element.
    /// Null when interactivity is disabled (designer preview, export...).
    /// </summary>
    public Action<DataSelection>? Select { get; init; }

    /// <summary>
    /// Elements selected in this visual (cross-filtering): the renderer draws them as usual and dims the others. A
    /// <see cref="CategorySelection"/> without a series covers every series of its category. Null when nothing is
    /// selected in this visual.
    /// </summary>
    public IReadOnlyList<DataSelection>? Highlight { get; init; }

    /// <summary>The pivoted data of a <see cref="MarkType.Matrix"/>; null for the other types.</summary>
    public MatrixData? Matrix { get; init; }
}
