using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>
/// What the tooltips of a chart show when a point is hovered. By default: the category as title, then each hovered
/// series with its value. The end user can hide parts, add the share of the total and other measures
/// (<see cref="Values"/>), or replace it all with a <see cref="Template"/>.
/// </summary>
public sealed record TooltipSettings
{
    /// <summary>No tooltip at all.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsHidden { get; init; }

    /// <summary>Leaves the category (the title) out.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideCategory { get; init; }

    /// <summary>Leaves the names of the series out, keeping their values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HideSeries { get; init; }

    /// <summary>Adds each value's share of its series' total (of the whole, for a pie).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowPercent { get; init; }

    /// <summary>Other measures shown for the hovered category, queried with the visual but not drawn.</summary>
    public IReadOnlyList<MeasureRef> Values { get; init; } = [];

    /// <summary>
    /// A text replacing the default content, with fields in braces: <c>{category}</c>, <c>{series}</c>,
    /// <c>{value}</c>, <c>{percent}</c>, and any measure of the visual or of <see cref="Values"/> by its name or caption,
    /// such as <c>{Margin}</c>. Null or empty keeps the default content.
    /// </summary>
    public string? Template { get; init; }
}

/// <summary>A line of a tooltip: a label (a series, a measure), its formatted value and, for a series, its color.</summary>
public sealed record TooltipItem(string Label, string Value, string? Color = null);

/// <summary>
/// The content of a tooltip, ready to show: a <see cref="Title"/> (the category) and <see cref="Items"/>, or a
/// <see cref="Text"/> from the visual's template. A connector shows it (with EasyBI.Wpf's <c>ChartTooltipHost</c>);
/// an application can change it before it opens.
/// </summary>
public sealed record TooltipContent(string? Title, IReadOnlyList<TooltipItem> Items)
{
    /// <summary>The text of the visual's template, which replaces the title and the items when set.</summary>
    public string? Text { get; init; }
}
