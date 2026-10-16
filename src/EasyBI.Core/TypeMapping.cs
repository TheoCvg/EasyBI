using System.Globalization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

internal static class TypeMapping
{
    public static DataType ToDataType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsDate(type)) return DataType.Date;
        if (type == typeof(bool)) return DataType.Boolean;
        if (type.IsPrimitive || type == typeof(decimal)) return DataType.Number;
        return DataType.Text;
    }

    public static bool IsDate(Type type) =>
        type == typeof(DateTime) || type == typeof(DateOnly) || type == typeof(DateTimeOffset);
}

/// <summary>Converts filter values to/from an invariant (serializable) text form.</summary>
public static class InvariantValue
{
    /// <returns>The invariant form, or null for a missing value.</returns>
    public static string? Format(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("o", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DatePeriod p => p.Key.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static object? Parse(string? text, Type type)
    {
        if (text is null) return null;
        var underlying = Nullable.GetUnderlyingType(type);
        // Compatibility: "" used to mean a missing value for non-text types
        if (text.Length == 0 && (underlying is not null || !type.IsValueType) && type != typeof(string))
            return null;
        var target = underlying ?? type;
        if (target == typeof(string)) return text;
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateTimeOffset)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target == typeof(DateOnly)) return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        if (target.IsEnum) return Enum.Parse(target, text);
        return Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
    }
}
