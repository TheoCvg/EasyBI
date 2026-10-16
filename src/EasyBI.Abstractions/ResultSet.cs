namespace EasyBI.Abstractions;

public sealed record ResultColumn(string Key, string Caption, FieldKind Role, DataType DataType, string? Format = null);

/// <summary>Format pivot tabulaire entre le moteur de requête et les renderers.</summary>
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
        throw new KeyNotFoundException($"Colonne '{key}' absente du résultat.");
    }
}
