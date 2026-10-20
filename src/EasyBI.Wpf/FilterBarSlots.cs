namespace EasyBI.Wpf;

/// <summary>
/// Names of the <see cref="Slot"/>s of a <see cref="FilterBar"/>. Their content takes the data context of the filter
/// bar (the application's), except <see cref="Slicer"/>.
/// </summary>
public static class FilterBarSlots
{
    /// <summary>The label, the global filters and the "add" button, at the start of the bar.</summary>
    public const string Filters = "FilterBar.Filters";

    /// <summary>The "Filters" label.</summary>
    public const string Label = "FilterBar.Label";

    /// <summary>
    /// A global filter, repeated for each one: its data context is the <see cref="SlicerViewModel"/>, so the slot
    /// takes a <see cref="Slot.ContentTemplate"/>.
    /// </summary>
    public const string Slicer = "FilterBar.Slicer";

    /// <summary>The button adding a global filter.</summary>
    public const string AddButton = "FilterBar.AddButton";

    /// <summary>The current selection (cross-filter), at the end of the bar.</summary>
    public const string Selection = "FilterBar.Selection";
}
