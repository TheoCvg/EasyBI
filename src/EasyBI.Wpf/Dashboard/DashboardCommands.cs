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

    /// <summary>Shows a variable of the report in an item (parameter: the item).</summary>
    public static RoutedCommand AddVariable { get; } = new(nameof(AddVariable), typeof(DashboardCommands));

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

    /// <summary>
    /// Duplicates the selected items (<see cref="DashboardView.SelectedItems"/>), each next to its original (Ctrl+D).
    /// Removing them and selecting them all use <see cref="ApplicationCommands.Delete"/> and <see cref="ApplicationCommands.SelectAll"/>.
    /// </summary>
    public static RoutedCommand Duplicate { get; } = new(nameof(Duplicate), typeof(DashboardCommands),
        [new KeyGesture(Key.D, ModifierKeys.Control)]);

    /// <summary>
    /// Moves the selected item one step (<see cref="DashboardView.MoveSelected"/>); the parameter is the direction, an
    /// <see cref="ItemSide"/> or its name ("Below"). Ctrl+arrows through <see cref="Behaviors.KeyboardLayoutBehavior"/>.
    /// </summary>
    public static RoutedCommand MoveItem { get; } = new(nameof(MoveItem), typeof(DashboardCommands));

    /// <summary>Moves the selected item to a new band below or above its band (Ctrl+Shift+Down/Up); the parameter is <see cref="ItemSide.Below"/> or <see cref="ItemSide.Above"/>.</summary>
    public static RoutedCommand MoveItemToNewBand { get; } = new(nameof(MoveItemToNewBand), typeof(DashboardCommands));

    /// <summary>
    /// Extends the selected items over the free room next to them (<see cref="DashboardView.ExtendSelected"/>): in width
    /// for <see cref="ItemSide.Left"/> or <see cref="ItemSide.Right"/>, in height for <see cref="ItemSide.Above"/> or
    /// <see cref="ItemSide.Below"/> (Alt+arrows).
    /// </summary>
    public static RoutedCommand ExtendItem { get; } = new(nameof(ExtendItem), typeof(DashboardCommands));

    /// <summary>The direction given to <see cref="MoveItem"/>, <see cref="MoveItemToNewBand"/> or <see cref="ExtendItem"/>.</summary>
    public static ItemSide? SideOf(object? parameter) => parameter switch
    {
        ItemSide side => side,
        string name when Enum.TryParse<ItemSide>(name, ignoreCase: true, out var side) => side,
        _ => null,
    };
}
