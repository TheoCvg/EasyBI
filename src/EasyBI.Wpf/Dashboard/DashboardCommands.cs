using System.Windows.Input;

namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Commands handled by <see cref="DashboardView"/>, so that custom item templates and behaviors can trigger its
/// actions. The parameter is the target <see cref="DashboardItem"/>; when omitted, the item containing the command
/// source is used. Band commands take the band index.
/// </summary>
public static class DashboardCommands
{
    public static RoutedCommand AddVisual { get; } = new(nameof(AddVisual), typeof(DashboardCommands));
    public static RoutedCommand AddText { get; } = new(nameof(AddText), typeof(DashboardCommands));
    public static RoutedCommand AddImage { get; } = new(nameof(AddImage), typeof(DashboardCommands));

    /// <summary>Edits the content of an item: designer for a visual, in-place editing for a text, file picker for an image.</summary>
    public static RoutedCommand EditItem { get; } = new(nameof(EditItem), typeof(DashboardCommands));

    public static RoutedCommand RemoveItem { get; } = new(nameof(RemoveItem), typeof(DashboardCommands));

    /// <summary>Removes the size constraints of an item (see <see cref="DashboardView.SetAutoSize"/>).</summary>
    public static RoutedCommand ResetSize { get; } = new(nameof(ResetSize), typeof(DashboardCommands));

    public static RoutedCommand AddRight { get; } = new(nameof(AddRight), typeof(DashboardCommands));
    public static RoutedCommand AddBelow { get; } = new(nameof(AddBelow), typeof(DashboardCommands));

    /// <summary>Inserts a band holding an empty item; the parameter is the index of the new band.</summary>
    public static RoutedCommand InsertBand { get; } = new(nameof(InsertBand), typeof(DashboardCommands));

    /// <summary>Removes a band and its items; the parameter is the band index.</summary>
    public static RoutedCommand RemoveBand { get; } = new(nameof(RemoveBand), typeof(DashboardCommands));
}
