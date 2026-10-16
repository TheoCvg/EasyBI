using System.Globalization;
using EasyBI.Abstractions;

namespace EasyBI.Core;

internal static class TypeMapping
{
    public static DataType ToDataType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(DateTime)) return DataType.Date;
        if (type == typeof(bool)) return DataType.Boolean;
        if (type.IsPrimitive || type == typeof(decimal)) return DataType.Number;
        return DataType.Text;
    }
}

/// <summary>Conversion des valeurs de filtre vers/depuis une forme texte invariante (sérialisable).</summary>
public static class InvariantValue
{
    public static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static object? Parse(string text, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (text.Length == 0 && (underlying is not null || !type.IsValueType) && type != typeof(string))
            return null;
        var target = underlying ?? type;
        if (target == typeof(string)) return text;
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (target.IsEnum) return Enum.Parse(target, text);
        return Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
    }
}
