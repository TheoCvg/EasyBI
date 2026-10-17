using EasyBI.Abstractions;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Raised when the user asks to create or edit a visual; set <see cref="Handled"/> to replace the built-in designer dialog.</summary>
public sealed class EditVisualRequestedEventArgs(DashboardItem item, Action<VisualDefinition> complete) : EventArgs
{
    public DashboardItem Item { get; } = item;

    /// <summary>The visual being edited, or null for a new one.</summary>
    public VisualDefinition? Visual { get; } = item.Visual;

    /// <summary>True when the application handles the edit: the built-in dialog is not opened.</summary>
    public bool Handled { get; set; }

    /// <summary>Shows the edited visual in the item. Can be called later, once the application's editor closes.</summary>
    public void Complete(VisualDefinition visual) => complete(visual);
}

/// <summary>Raised when the user asks to pick an image; set <see cref="Handled"/> to replace the built-in file dialog.</summary>
public sealed class ImageRequestedEventArgs(DashboardItem item, Action<ImageContent> complete) : EventArgs
{
    public DashboardItem Item { get; } = item;

    /// <summary>The image currently shown, or null.</summary>
    public ImageContent? Current { get; } = item.Visual?.Image;

    public bool Handled { get; set; }

    /// <summary>Shows the image in the item.</summary>
    public void Complete(ImageContent image) => complete(image);
}

/// <summary>Raised when the text of an item should be edited; the in-place editor (<see cref="Behaviors.TextEditBehavior"/>) handles it.</summary>
public sealed class TextEditRequestedEventArgs(DashboardItem item) : EventArgs
{
    public DashboardItem Item { get; } = item;

    public bool Handled { get; set; }
}

/// <summary>
/// Raised when an item is added next to a full-row item, which requires giving up the full row. Set
/// <see cref="Handled"/> to ask the user yourself, then call <see cref="Proceed"/> if they agree; otherwise the item
/// shows a banner (<see cref="DashboardItem.IsFullRowSplitPending"/>).
/// </summary>
public sealed class FullRowSplitRequestedEventArgs(DashboardItem item, Action proceed) : EventArgs
{
    public DashboardItem Item { get; } = item;

    public bool Handled { get; set; }

    /// <summary>Removes the full-row setting and adds the new item next to it.</summary>
    public void Proceed() => proceed();
}

/// <summary>Raised before an item (and the visual it shows) is removed; set <see cref="Cancel"/> to keep it.</summary>
public sealed class ItemRemovingEventArgs(DashboardItem item) : EventArgs
{
    public DashboardItem Item { get; } = item;

    public bool Cancel { get; set; }
}

/// <summary>A change made through the dashboard (layout or content), with the documents before and after it.</summary>
public sealed class DashboardChangedEventArgs(DashboardDocument before, DashboardDocument after) : EventArgs
{
    public DashboardDocument Before { get; } = before;

    public DashboardDocument After { get; } = after;
}
