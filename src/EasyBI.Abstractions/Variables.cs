namespace EasyBI.Abstractions;

/// <summary>
/// A variable of a report, written as a formula over a model (the end user types <c>N = 10 BETWEEN 3 AND 25</c> in the
/// formula editor). What the formula is gives what the variable holds:
/// <list type="bullet">
/// <item>a value (<c>10</c>, <c>"Web"</c>, <c>"2025-01-01"</c>, <c>TRUE</c>), optionally with the values the end user can
/// choose from (<c>10 BETWEEN 3 AND 25 STEP 1</c>, <c>"Web" IN ("Web"; "Store")</c>): a parameter, which the dashboard
/// changes;</item>
/// <item>a set of values of a dimension: <c>SELECT TOP [@N] [Customer] WHERE [Region] = "North" ORDER BY [Amount] DESC</c>,
/// or a list: <c>SELECT [Customer] IN ("Smith"; "Jones")</c>;</item>
/// <item>any other formula, such as <c>AVERAGE([Amount])</c>: a number computed once over the data the report's
/// filters keep.</item>
/// </list>
/// A variable is referenced by its <see cref="Name"/>: <c>[@N]</c> in a formula (<c>[Amount] * [@Rate]</c>) or as a value
/// of a filter (a set's values with <see cref="FilterOperator.In"/> or <see cref="FilterOperator.NotIn"/>), <c>{N}</c> in
/// a title or a text ("Top {N} suppliers"), by <see cref="VisualDefinition.TopVariable"/> for the number of groups a
/// visual keeps, and by <see cref="FieldRef.Set"/> to group a dimension in "in the set" and "others". EasyBI replaces
/// the references by the values before asking a model for data, so a source never sees them.
/// </summary>
public sealed record ReportVariable
{
    /// <summary>What references it and what the end user reads (see <see cref="IsValidName"/>).</summary>
    public string Name { get; init; } = "";

    /// <summary>The model whose fields the formula uses.</summary>
    public string Model { get; init; } = "";

    /// <summary>The formula, with field names (shown with captions).</summary>
    public string Formula { get; init; } = "";

    /// <summary>
    /// The value the end user gave a parameter on the dashboard, in invariant form like filter values (a number with a
    /// decimal point, a date as yyyy-MM-dd, a boolean as True or False); null for the value its formula gives.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>.NET format of a number or a date where it is shown ("P1", "N0"...); null for the default.</summary>
    public string? Format { get; init; }

    /// <summary>
    /// Whether a name can reference a variable: not empty, without spaces around it, without the characters that
    /// delimit references (<c>[ ] { } @ "</c>).
    /// </summary>
    public static bool IsValidName(string? name) =>
        name is { Length: > 0 } && name.Trim() == name && name.IndexOfAny(['[', ']', '{', '}', '@', '"']) < 0;
}
