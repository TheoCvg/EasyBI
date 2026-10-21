namespace EasyBI.Abstractions;

public enum RendererOptionKind
{
    /// <summary>On or off (a check box); the value is a <see cref="bool"/>.</summary>
    Toggle,

    /// <summary>One of <see cref="RendererOption.Choices"/> (a list); the value is the choice's <see cref="string"/>.</summary>
    Choice,

    /// <summary>A number; the value is a <see cref="double"/>.</summary>
    Number,
}

/// <summary>A value offered by a <see cref="RendererOptionKind.Choice"/> option.</summary>
/// <param name="Value">Value stored in the report: keep it stable.</param>
/// <param name="Label">Text shown to the end user, read each time so that it follows the EasyBI language.</param>
public sealed record RendererChoice(string Value, Func<string> Label);

/// <summary>
/// A setting a renderer offers to the end user, such as a scroll bar under the chart. The renderer declares it in
/// <see cref="RendererCapabilities.Options"/>; the designer shows it and saves its value with the visual, the renderer
/// reads the value when it draws (<c>visual.GetRendererOption&lt;bool&gt;(Name, "scrollable")</c> in EasyBI.Core).
/// </summary>
/// <param name="Key">Identifier of the option, stored in the report: keep it stable.</param>
/// <param name="Kind">How the designer edits it, and the type of its value.</param>
/// <param name="Label">Text shown to the end user, read each time so that it follows the EasyBI language.</param>
public sealed record RendererOption(string Key, RendererOptionKind Kind, Func<string> Label)
{
    /// <summary>Longer explanation (tooltip); null for none.</summary>
    public Func<string>? Description { get; init; }

    /// <summary>Value used while the user has not set one: a bool, a choice's value or a double, by <see cref="Kind"/>.</summary>
    public object? Default { get; init; }

    public IReadOnlyList<RendererChoice> Choices { get; init; } = [];

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    /// <summary>Visuals the option applies to (by mark type...); null for every visual the renderer draws.</summary>
    public Func<VisualDefinition, bool>? AppliesTo { get; init; }

    public static RendererOption Toggle(string key, Func<string> label, bool @default = false, Func<VisualDefinition, bool>? appliesTo = null) =>
        new(key, RendererOptionKind.Toggle, label) { Default = @default, AppliesTo = appliesTo };

    public static RendererOption Choice(string key, Func<string> label, IReadOnlyList<RendererChoice> choices, string? @default = null,
        Func<VisualDefinition, bool>? appliesTo = null) =>
        new(key, RendererOptionKind.Choice, label) { Choices = choices, Default = @default ?? choices.FirstOrDefault()?.Value, AppliesTo = appliesTo };

    public static RendererOption Number(string key, Func<string> label, double @default, double? minimum = null, double? maximum = null,
        Func<VisualDefinition, bool>? appliesTo = null) =>
        new(key, RendererOptionKind.Number, label) { Default = @default, Minimum = minimum, Maximum = maximum, AppliesTo = appliesTo };
}
