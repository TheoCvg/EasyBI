using System.Globalization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// The look of one value once its measure's <see cref="ValueFormat"/> is applied; colors are resolved
/// (<c>#RRGGBB</c>), null where the renderer keeps its own.
/// </summary>
public sealed record ValueStyle
{
    public string? Foreground { get; init; }

    public string? Background { get; init; }

    public bool IsBold { get; init; }

    public bool IsItalic { get; init; }

    public TextAlign? Alignment { get; init; }

    /// <summary>Length of the data bar, from -1 to 1 (negative values to the left); null without a bar.</summary>
    public double? Bar { get; init; }

    public string? BarColor { get; init; }

    /// <summary>A symbol shown before the value (▲, ●...), in <see cref="IconColor"/>.</summary>
    public string? Icon { get; init; }

    public string? IconColor { get; init; }
}

/// <summary>
/// Applies a measure's <see cref="ValueFormat"/> to its values. The scale, the bars and the automatic thresholds of
/// the icons compare a value with the values shown (<c>values</c>: those of the cells, not of the totals).
/// </summary>
public sealed class ValueStyler
{
    private readonly ValueFormat _format;
    private readonly BiTheme _theme;
    private readonly double _min;
    private readonly double _max;

    public ValueStyler(ValueFormat format, IEnumerable<double?> values, BiTheme theme)
    {
        _format = format;
        _theme = theme;
        var shown = values.OfType<double>().Where(Compat.IsFinite).ToList();
        (_min, _max) = shown.Count == 0 ? (0, 0) : (shown.Min(), shown.Max());
    }

    /// <summary>A styler for the measure, or null when it has no format.</summary>
    public static ValueStyler? For(MeasureRef? measure, IEnumerable<double?> values, BiTheme theme) =>
        measure?.Format is { } format ? new ValueStyler(format, values, theme) : null;

    /// <summary>The look of <paramref name="value"/>; a missing value only takes the fixed style.</summary>
    public ValueStyle Style(double? value)
    {
        var rule = value is { } v ? _format.Rules.FirstOrDefault(r => Matches(r, v)) : null;
        var style = new ValueStyle
        {
            Foreground = _theme.Resolve(rule?.Color ?? _format.Color),
            Background = Fill(rule?.Background),
            IsBold = _format.IsBold || rule?.IsBold == true,
            IsItalic = _format.IsItalic,
            Alignment = _format.Alignment,
        };
        if (value is not { } number) return style;

        if (_format.Scale is { } scale && style.Background is null)
            style = style with { Background = ScaleColor(scale, number) };
        if (_format.Bar is { } bar)
        {
            var largest = Math.Max(Math.Abs(_min), Math.Abs(_max));
            var length = largest == 0 ? 0 : Compat.Clamp(number / largest, -1, 1);
            style = style with { Bar = length, BarColor = _theme.Resolve(length < 0 ? bar.NegativeColor : bar.Color) };
        }
        if (_format.Icons is { } icons)
        {
            var low = icons.Low ?? _min + (_max - _min) / 3;
            var high = icons.High ?? _min + 2 * (_max - _min) / 3;
            var level = number >= high ? 2 : number < low ? 0 : 1;
            style = style with
            {
                Icon = Icon(icons.Kind, level),
                IconColor = _theme.Resolve(level switch { 2 => ThemeColors.Success, 0 => ThemeColors.Error, _ => ThemeColors.Warning }),
            };
        }
        return style;
    }

    /// <summary>Whether a rule's condition holds for a value; a missing bound never matches, except an open end of a range.</summary>
    public static bool Matches(FormatRule rule, double value) => rule.Operator switch
    {
        FilterOperator.GreaterThan => rule.Value is { } v && value > v,
        FilterOperator.GreaterThanOrEqual => rule.Value is { } v && value >= v,
        FilterOperator.LessThan => rule.Value is { } v && value < v,
        FilterOperator.LessThanOrEqual => rule.Value is { } v && value <= v,
        FilterOperator.Range => (rule.Value is not null || rule.Maximum is not null)
            && (rule.Value is not { } min || value >= min) && (rule.Maximum is not { } max || value <= max),
        _ => false,
    };

    private static string Icon(IconKind kind, int level) => (kind, level) switch
    {
        (IconKind.Arrows, 2) => "↑",
        (IconKind.Arrows, 1) => "→",
        (IconKind.Arrows, _) => "↓",
        (IconKind.Triangles, 2) => "▲",
        (IconKind.Triangles, 1) => "▬",
        (IconKind.Triangles, _) => "▼",
        _ => "●",
    };

    private string? ScaleColor(ColorScale scale, double value)
    {
        var position = _max == _min ? 1 : (value - _min) / (_max - _min);
        if (scale.Middle is { } middle)
        {
            return position < 0.5
                ? Mix(Fill(scale.Minimum), Fill(middle), position * 2)
                : Mix(Fill(middle), Fill(scale.Maximum), position * 2 - 1);
        }
        return Mix(Fill(scale.Minimum), Fill(scale.Maximum), position);
    }

    /// <summary>Share of a theme color in a background: the theme's colors are made for text, a value stays readable on them once softened.</summary>
    public const double FillStrength = 0.3;

    // A theme color as a background: mixed with the theme's background; a custom color is taken as is
    private string? Fill(string? color) =>
        color is null || color.StartsWith('#')
            ? _theme.Resolve(color)
            : Mix(_theme.Background ?? "#FFFFFF", _theme.Resolve(color), FillStrength);

    /// <summary>The color between two others at <paramref name="ratio"/> (0: the first, 1: the second), as #RRGGBB.</summary>
    public static string? Mix(string? from, string? to, double ratio)
    {
        if (Parse(from) is not { } a) return to;
        if (Parse(to) is not { } b) return from;
        ratio = Compat.Clamp(ratio, 0, 1);
        int Channel(int x, int y) => (int)Math.Round(x + (y - x) * ratio);
        return $"#{Channel(a.R, b.R):X2}{Channel(a.G, b.G):X2}{Channel(a.B, b.B):X2}";
    }

    // #RRGGBB or #AARRGGBB (the alpha is dropped)
    private static (int R, int G, int B)? Parse(string? color)
    {
        if (color is null || !color.StartsWith('#') || color.Length is not (7 or 9)) return null;
        var hex = color[^6..];
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF)
            : null;
    }
}
