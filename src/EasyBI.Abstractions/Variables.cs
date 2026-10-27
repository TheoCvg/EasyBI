using System.Text.Json.Serialization;

namespace EasyBI.Abstractions;

/// <summary>What a <see cref="ReportVariable"/> holds: one value, or a set of values of a dimension.</summary>
public enum VariableKind { Parameter, Set }

/// <summary>
/// A variable of a report, which the end user changes on the dashboard and the visuals use. A
/// <see cref="VariableKind.Parameter"/> holds one value (a number of suppliers, a rate, a date...), a
/// <see cref="VariableKind.Set"/> values of a dimension: a fixed list, or the first or last ones by a measure.
/// <para>
/// A variable is referenced by its <see cref="Name"/>: <c>[@N]</c> in a formula (<c>[Amount] * [@Rate]</c>) or as a
/// value of a filter (<c>≥ [@Threshold]</c>, a set's values with <see cref="FilterOperator.In"/> or
/// <see cref="FilterOperator.NotIn"/>), <c>{N}</c> in a title or a text ("Top {N} suppliers"), by
/// <see cref="VisualDefinition.TopVariable"/> for the number of groups a visual keeps, and by <see cref="FieldRef.Set"/>
/// to group a dimension in "in the set" and "others". EasyBI replaces the references by the values before asking a model
/// for data, so a source never sees them.
/// </para>
/// </summary>
public sealed record ReportVariable
{
    /// <summary>What references it: letters, digits and underscores (see <see cref="IsValidName"/>).</summary>
    public string Name { get; init; } = "";

    /// <summary>What the end user reads; empty for the <see cref="Name"/>.</summary>
    public string Caption { get; init; } = "";

    public VariableKind Kind { get; init; }

    /// <summary>The type of a parameter's value (a number by default).</summary>
    public DataType DataType { get; init; } = DataType.Number;

    /// <summary>
    /// A parameter's value, in invariant form like filter values (a number with a decimal point, a date as yyyy-MM-dd,
    /// a boolean as True or False); null when it has none.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>The smallest value of a number parameter; with <see cref="Maximum"/>, the dashboard shows a slider.</summary>
    public double? Minimum { get; init; }

    /// <summary>The largest value of a number parameter.</summary>
    public double? Maximum { get; init; }

    /// <summary>The step of a number parameter's slider; null for 1.</summary>
    public double? Step { get; init; }

    /// <summary>The values a text parameter can take, offered in a list; null for any text.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>.NET format of a number or date parameter's value where it is shown ("P1", "N0"...); null for the default.</summary>
    public string? Format { get; init; }

    /// <summary>The model of a set's dimension.</summary>
    public string Model { get; init; } = "";

    /// <summary>The dimension whose values a set holds.</summary>
    public string Field { get; init; } = "";

    /// <summary>The values of a fixed set, in invariant form (null for a missing value); unused with a <see cref="Rule"/>.</summary>
    public IReadOnlyList<string?>? Members { get; init; }

    /// <summary>The rule choosing a set's values; null for a fixed set (<see cref="Members"/>).</summary>
    public SetRule? Rule { get; init; }

    /// <summary>The caption, or the name when it has none.</summary>
    [JsonIgnore]
    public string DisplayName => Caption.Length > 0 ? Caption : Name;

    /// <summary>Whether a name can reference a variable: letters, digits and underscores, not starting with a digit.</summary>
    public static bool IsValidName(string? name) =>
        name is { Length: > 0 } && !char.IsDigit(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');
}

/// <summary>
/// The values of a set chosen by a measure: the <see cref="Count"/> values of the dimension with the highest values of
/// <see cref="Measure"/> (the lowest with <see cref="Bottom"/>), over the data the report's filters of its model keep.
/// </summary>
/// <param name="Measure">A measure of the model, or one of the report's calculated measures.</param>
public sealed record SetRule(string Measure)
{
    public int Count { get; init; } = 10;

    /// <summary>A number parameter giving the count instead of <see cref="Count"/>; null for the count itself.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CountVariable { get; init; }

    /// <summary>The values with the lowest values of the measure, instead of the highest.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Bottom { get; init; }
}
