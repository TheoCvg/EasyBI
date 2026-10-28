namespace EasyBI;

/// <summary>
/// Members of .NET 8 that .NET Standard 2.0 and .NET Framework lack and that cannot be added as extension methods
/// (static members of framework types). Compiled into every library, for every target, so the code reads the same.
/// </summary>
internal static class Compat
{
    /// <summary>Same as .NET 8's Math.Clamp: <paramref name="value"/> kept within [min, max].</summary>
    /// <exception cref="ArgumentException"><paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
    {
        if (min.CompareTo(max) > 0)
            throw new ArgumentException($"'{min}' cannot be greater than {max}.", nameof(min));
        return value.CompareTo(min) < 0 ? min : value.CompareTo(max) > 0 ? max : value;
    }

    /// <summary>Same as .NET 8's char.IsAsciiDigit.</summary>
    public static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    /// <summary>Same as .NET 8's char.IsAsciiLetter.</summary>
    public static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    /// <summary>Same as .NET 8's char.IsAsciiLetterOrDigit.</summary>
    public static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);

    /// <summary>Same as .NET 8's double.IsFinite: neither infinite nor NaN.</summary>
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>Same as .NET 8's Enum.GetValues&lt;T&gt;.</summary>
    public static T[] GetValues<T>() where T : struct, Enum => (T[])Enum.GetValues(typeof(T));

    /// <summary>Same as .NET 8's Array.Fill: every element of <paramref name="array"/> set to <paramref name="value"/>.</summary>
    public static void Fill<T>(T[] array, T value)
    {
        for (var i = 0; i < array.Length; i++) array[i] = value;
    }
}
