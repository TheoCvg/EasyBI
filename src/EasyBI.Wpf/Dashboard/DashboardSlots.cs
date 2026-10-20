namespace EasyBI.Wpf.Dashboard;

/// <summary>
/// Names of the <see cref="Slot"/>s of a <see cref="DashboardView"/>. The item slots are repeated for each item and take
/// the <see cref="DashboardItem"/> as data context, so they take a <see cref="Slot.ContentTemplate"/>; commands such as
/// <see cref="DashboardCommands.RemoveItem"/> act on the item containing their source, or on the item that is their data
/// context (in the toolbar).
/// </summary>
/// <remarks>
/// The slots declared on a dashboard (or on its window) also apply to the designer opened to edit one of its visuals.
/// The band menu is customized through <see cref="Behaviors.BandMenuBehavior.MenuOpening"/>.
/// </remarks>
public static class DashboardSlots
{
    /// <summary>The scrolling area of the dashboard (vertical: before and after stay above and below it); the view's data context.</summary>
    public const string Body = "Dashboard.Body";

    /// <summary>What an item shows: its visual, text or image (vertical: a header above it, a footer below it).</summary>
    public const string ItemContent = "Dashboard.ItemContent";

    /// <summary>The choices of an empty item (add a visual, a text, an image).</summary>
    public const string EmptyItemChoices = "Dashboard.EmptyItemChoices";

    /// <summary>The toolbar shown at the top-right corner of the hovered item (<see cref="Behaviors.ItemToolbarBehavior"/>).</summary>
    public const string ItemToolbar = "Dashboard.ItemToolbar";
}
