namespace EasyBI.Wpf.Designer;

/// <summary>
/// Names of the <see cref="Slot"/>s of a <see cref="FilterEditor"/>, the popup of a filter in the designer and in the
/// filter bar. Their content takes the <see cref="FilterItemViewModel"/> as data context; the editor is repeated for
/// each filter, so the slots take a <see cref="Slot.ContentTemplate"/>. Both are vertical.
/// </summary>
public static class FilterEditorSlots
{
    /// <summary>The values to check, for a filter on a list of values.</summary>
    public const string Values = "FilterEditor.Values";

    /// <summary>The period (between two dates, or relative to today), for a filter on a date.</summary>
    public const string Period = "FilterEditor.Period";

    /// <summary>The comparison with numbers, for a filter on a measure or (when chosen) on a number dimension.</summary>
    public const string Comparison = "FilterEditor.Comparison";
}
