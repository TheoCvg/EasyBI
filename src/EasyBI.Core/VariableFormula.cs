using System.Runtime.CompilerServices;
using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>What a <see cref="ReportVariable"/> holds, given by its formula.</summary>
public enum VariableKind
{
    /// <summary>A value the end user changes: <c>10 BETWEEN 3 AND 25</c>, <c>"Web" IN ("Web"; "Store")</c>.</summary>
    Parameter,

    /// <summary>A number computed once over the data the report's filters keep: <c>AVERAGE([Amount])</c>.</summary>
    Computed,

    /// <summary>Values of a dimension: <c>SELECT TOP 10 [Customer] ORDER BY [Amount] DESC</c>.</summary>
    Set,
}

/// <summary>What the formula of a <see cref="ReportVariable"/> defines (see <see cref="VariableFormula.Read"/>).</summary>
public abstract record VariableDefinition
{
    public abstract VariableKind Kind { get; }

    /// <summary>The type of the value: a parameter's, a number for a computed value; text for a set.</summary>
    public DataType DataType { get; init; } = DataType.Number;
}

/// <summary>A value the end user changes, and the values it can take.</summary>
public sealed record ParameterDefinition : VariableDefinition
{
    public override VariableKind Kind => VariableKind.Parameter;

    /// <summary>The value the formula gives, in invariant form.</summary>
    public string? Value { get; init; }

    /// <summary>With <see cref="Maximum"/>, the bounds of a number (a slider on the dashboard).</summary>
    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    /// <summary>The step of the slider; null for 1.</summary>
    public double? Step { get; init; }

    /// <summary>The values to choose from (a list on the dashboard), in invariant form; null for any value.</summary>
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>A number computed once, by the variable's formula read as a measure's over the whole data.</summary>
public sealed record ComputedDefinition : VariableDefinition
{
    public override VariableKind Kind => VariableKind.Computed;
}

/// <summary>Values of a dimension of the variable's model: listed, or chosen by conditions, an order and a count.</summary>
/// <param name="Field">The dimension.</param>
public sealed record SetDefinition(string Field) : VariableDefinition
{
    public override VariableKind Kind => VariableKind.Set;

    /// <summary>The number of values kept (TOP 10); null for all of them.</summary>
    public int? Top { get; init; }

    /// <summary>The variable giving the number of values kept (TOP [@N]).</summary>
    public string? TopVariable { get; init; }

    /// <summary>The values listed (IN (...)) in invariant form; null when the set does not list them.</summary>
    public IReadOnlyList<string?>? Members { get; init; }

    /// <summary>Whether the values are ordered from the largest (ORDER BY ... DESC).</summary>
    public bool Descending { get; init; }

    /// <summary>Whether conditions choose the values (WHERE).</summary>
    public bool HasConditions { get; init; }

    /// <summary>Whether the values are ordered by an expression (ORDER BY).</summary>
    public bool HasOrder { get; init; }

    /// <summary>Whether the values are only those listed, with nothing to query.</summary>
    public bool IsList => Members is not null && Top is null && TopVariable is null && !HasConditions && !HasOrder;
}

/// <summary>
/// The formulas of the report's variables (<see cref="ReportVariable"/>), in the language of the calculated measures
/// (<see cref="MeasureFormula"/>) with a few words:
/// <list type="table">
/// <item><term><c>10</c>, <c>"Web"</c>, <c>"2025-01-01"</c>, <c>TRUE</c></term><description>a parameter of that value (a text written yyyy-MM-dd is a date)</description></item>
/// <item><term><c>10 BETWEEN 3 AND 25 STEP 1</c></term><description>a number parameter shown as a slider</description></item>
/// <item><term><c>"Web" IN ("Web"; "Store")</c></term><description>a parameter chosen from a list</description></item>
/// <item><term><c>SELECT [Customer] IN ("Smith"; "Jones")</c></term><description>a set listing values of a dimension</description></item>
/// <item><term><c>SELECT TOP [@N] [Customer] WHERE [Region] = "North" AND [Amount] &gt; 1000 ORDER BY [Amount] DESC</c></term>
/// <description>a set chosen by conditions (on dimensions, they keep rows; on measures, they keep values), an order and a count</description></item>
/// <item><term>any other formula, <c>AVERAGE([Amount])</c></term><description>a number computed once</description></item>
/// </list>
/// Like measures, a formula is stored with field names and shown with captions; it uses other variables as
/// <c>[@Name]</c>, but not itself.
/// </summary>
public static class VariableFormula
{
    // Read once per formula: visuals ask at every query
    private static readonly ConditionalWeakTable<string, Box> Definitions = new();

    private sealed class Box(VariableDefinition? definition)
    {
        public VariableDefinition? Definition { get; } = definition;
    }

    /// <summary>What a stored formula defines; null when it cannot be read.</summary>
    public static VariableDefinition? Read(string formula) =>
        Definitions.GetValue(formula, f => new Box(MeasureFormula.ReadVariable(f))).Definition;

    /// <summary>What a variable's formula defines, a computed value when it cannot be read.</summary>
    public static VariableDefinition Definition(ReportVariable variable) => Read(variable.Formula) ?? new ComputedDefinition();

    /// <summary>
    /// Checks the formula typed by the user (fields named by caption, or by name) for the variable
    /// <paramref name="name"/> of a model, which the report's <paramref name="measures"/> and other
    /// <paramref name="variables"/> may help define.
    /// </summary>
    /// <returns>Null with the <paramref name="stored"/> formula when it is valid, otherwise the problem and where it is written.</returns>
    public static FormulaProblem? Check(
        string text,
        ModelMetadata metadata,
        out string? stored,
        IEnumerable<CalculatedMeasure>? measures = null,
        string? name = null,
        IEnumerable<ReportVariable>? variables = null) =>
        MeasureFormula.CheckVariable(text, metadata, out stored, measures, name, variables, byCaption: true);

    /// <summary>Checks a stored formula; null when it is valid, otherwise the problem worded for the end user.</summary>
    public static string? Validate(
        string formula,
        ModelMetadata metadata,
        IEnumerable<CalculatedMeasure>? measures = null,
        string? name = null,
        IEnumerable<ReportVariable>? variables = null) =>
        MeasureFormula.CheckVariable(formula, metadata, out _, measures, name, variables, byCaption: false)?.Message;

    /// <summary>The variables a stored formula uses (<c>[@N]</c>: N), once each.</summary>
    public static IReadOnlyList<string> Variables(string formula) => MeasureFormula.Variables(formula);
}
