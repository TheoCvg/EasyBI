using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>The range a <see cref="MarkType.Gauge"/> draws its value on, from its settings and the value.</summary>
public static class GaugeScale
{
    /// <returns>
    /// The minimum (the setting, else 0, or the value when it is lower) and the maximum (the setting, else a round
    /// number a little above 0, the value and the target), the maximum always above the minimum.
    /// </returns>
    public static (double Minimum, double Maximum) Of(GaugeSettings? settings, double? value)
    {
        var minimum = settings?.Minimum ?? Math.Min(0, value ?? 0);
        var highest = Math.Max(0, Math.Max(value ?? 0, settings?.Target ?? 0));
        var maximum = settings?.Maximum ?? (highest > 0 ? Round(highest * 1.1) : 0);
        return (minimum, maximum > minimum ? maximum : minimum + 1);
    }

    /// <summary>Where a value falls on the scale, from 0 (the minimum) to 1 (the maximum), kept within it.</summary>
    public static double Position(double value, double minimum, double maximum) =>
        maximum <= minimum ? 0 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);

    // The next round number: 1, 2, 2.5 or 5 times a power of ten
    private static double Round(double value)
    {
        if (value <= 0) return value;
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
            if (step * power >= value) return step * power;
        return 10 * power;
    }
}
