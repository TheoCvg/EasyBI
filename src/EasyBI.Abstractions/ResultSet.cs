namespace EasyBI.Abstractions;

public sealed record ResultColumn(string Key, string Caption, FieldKind Role, DataType DataType, string? Format = null)
{
    /// <summary>For the changes of a measure (compared with another period), which way is good; null for other values.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Favorable? Change { get; init; }
}

/// <summary>Tabular pivot format between the query engine and the renderers.</summary>
public sealed class ResultSet
{
    public ResultSet(IReadOnlyList<ResultColumn> columns, IReadOnlyList<object?[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<ResultColumn> Columns { get; }

    public IReadOnlyList<object?[]> Rows { get; }

    public int IndexOf(string key)
    {
        for (var i = 0; i < Columns.Count; i++)
            if (Columns[i].Key == key) return i;
        throw new KeyNotFoundException($"Column '{key}' is not in the result.");
    }
}
