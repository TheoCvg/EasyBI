using System.Globalization;

namespace EasyBI.Core;

/// <summary>
/// Language of the EasyBI user interface and of the values it formats. Setting <see cref="Culture"/>
/// switches every EasyBI text at runtime; leaving it null follows the thread's current cultures.
/// </summary>
public static class EasyBILocalization
{
    private static CultureInfo? _culture;

    /// <summary>Raised after <see cref="Culture"/> changes: views refresh their texts and captions.</summary>
    public static event EventHandler? CultureChanged;

    /// <summary>The culture used by EasyBI, or null to follow <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    public static CultureInfo? Culture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value)) return;
            _culture = value;
            CultureChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Culture used to look up texts.</summary>
    public static CultureInfo UICulture => _culture ?? CultureInfo.CurrentUICulture;

    /// <summary>Culture used to format numbers and dates.</summary>
    public static CultureInfo FormatCulture => _culture ?? CultureInfo.CurrentCulture;
}
