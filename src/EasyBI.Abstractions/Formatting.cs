using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>
/// How the values of a measure look in a table, a matrix or a KPI: a fixed style, then conditional formats. Colors
/// are names of the theme's colors (<see cref="ThemeColors"/>) or <c>#RRGGBB</c>. In a chart, the rules and the color
/// scale color the bars, points and slices, and a rule can show its zone on the value axis.
/// </summary>
public sealed record ValueFormat
{
    /// <summary>Text color of every value; a matching <see cref="Rules">rule</see> overrides it.</summary>
    public string? Color { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBold { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsItalic { get; init; }

    /// <summary>Alignment of the values; null keeps the default (numbers to the right).</summary>
    public TextAlign? Alignment { get; init; }

    /// <summary>Conditions on the value, checked in order: the first one that matches styles it.</summary>
    public IReadOnlyList<FormatRule> Rules { get; init; } = [];

    /// <summary>Background going from one color to another with the value, from the smallest to the largest shown.</summary>
    public ColorScale? Scale { get; init; }

    /// <summary>A bar in the cell, as long as the value compared with the largest one shown.</summary>
    public DataBar? Bar { get; init; }

    /// <summary>An icon before the value: high, middle or low.</summary>
    public IconSet? Icons { get; init; }
}

/// <summary>
/// A condition on the value (a comparison: <see cref="FilterOperator.GreaterThan"/>... <see cref="FilterOperator.Range"/>,
/// whose bounds are <see cref="Value"/> and, for a range, <see cref="Maximum"/>; null leaves a range open) and the
/// style it gives.
/// </summary>
public sealed record FormatRule
{
    public FilterOperator Operator { get; init; } = FilterOperator.GreaterThan;

    public double? Value { get; init; }

    public double? Maximum { get; init; }

    /// <summary>Text color of the matching values.</summary>
    public string? Color { get; init; }

    /// <summary>Background of the matching values.</summary>
    public string? Background { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsBold { get; init; }

    /// <summary>In a chart, shades the rule's zone on the value axis (from its value up for "greater than", between its bounds for a range).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowOnChart { get; init; }
}

/// <summary>A background color scale: <see cref="Minimum"/> at the smallest value, <see cref="Maximum"/> at the largest, optionally <see cref="Middle"/> halfway.</summary>
public sealed record ColorScale
{
    public string Minimum { get; init; } = ThemeColors.Error;

    public string? Middle { get; init; }

    public string Maximum { get; init; } = ThemeColors.Success;
}

/// <summary>Data bars: positive values to the right in <see cref="Color"/>, negative ones in <see cref="NegativeColor"/>.</summary>
public sealed record DataBar
{
    public string Color { get; init; } = ThemeColors.Accent;

    public string NegativeColor { get; init; } = ThemeColors.Error;
}

public enum IconKind { Arrows, Triangles, Circles }

/// <summary>
/// Icons by value: high from <see cref="High"/> on, low under <see cref="Low"/>, middle between; a null threshold splits
/// the values shown in thirds.
/// </summary>
public sealed record IconSet
{
    public IconKind Kind { get; init; } = IconKind.Arrows;

    public double? Low { get; init; }

    public double? High { get; init; }
}

/// <summary>Names of the theme's colors, which follow light and dark themes; any other color is written <c>#RRGGBB</c>.</summary>
public static class ThemeColors
{
    public const string Accent = "accent";
    public const string Success = "success";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Muted = "muted";
}
