using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>The texts a visual shows for the values of its dimensions (<see cref="FieldRef.Labels"/>).</summary>
public static class ValueLabels
{
    /// <summary>The text of a value of the dimension: the visual's own label for it, else the formatted value.</summary>
    public static string Display(this FieldRef field, object? value) =>
        field.Labels is { Count: > 0 } labels
        && labels.TryGetValue(InvariantValue.Format(value) ?? "", out var text)
        && !string.IsNullOrWhiteSpace(text)
            ? text
            : ValueFormatter.Display(value);

    /// <summary>The field with a label for a value (a blank text removes it); null labels when none is left.</summary>
    public static FieldRef WithLabel(this FieldRef field, object? value, string? text)
    {
        var labels = field.Labels?.ToDictionary(l => l.Key, l => l.Value) ?? [];
        var key = InvariantValue.Format(value) ?? "";
        if (string.IsNullOrWhiteSpace(text)) labels.Remove(key);
        else labels[key] = text.Trim();
        return field with { Labels = labels.Count == 0 ? null : labels };
    }
}
