using System.Windows;

namespace EasyBI.Wpf.DragAndDrop;

/// <summary>
/// Decides what a drop target accepts and what a drop does, usually implemented by a view-model
/// (see <see cref="DropTarget.HandlerProperty"/>).
/// </summary>
public interface IDropHandler
{
    /// <summary>
    /// Called while something is dragged over the target: the effect a drop would have,
    /// <see cref="DragDropEffects.None"/> to refuse it.
    /// </summary>
    DragDropEffects DragOver(DropInfo drop);

    /// <summary>Called on a drop that <see cref="DragOver"/> accepted.</summary>
    void Drop(DropInfo drop);

    /// <summary>
    /// Called when <see cref="DragOver"/> refused a drag: why, in words for the end user (shown next to the target,
    /// see <see cref="DropTarget.RefusalProperty"/>), or null to say nothing.
    /// </summary>
    string? RefusalReason(DropInfo drop);
}

/// <summary>A drag over a drop target, or a drop on it.</summary>
public sealed record DropInfo
{
    /// <summary>The dragged item: the object a <see cref="DragSource"/> dragged, or null for other data.</summary>
    public object? Item { get; init; }

    /// <summary>The target's <see cref="DropTarget.KeyProperty"/>, telling targets sharing one handler apart.</summary>
    public object? Key { get; init; }

    /// <summary>Position before which the item goes among the target's items (their count to append; 0 for a target that is not a list).</summary>
    public int Index { get; init; }

    /// <summary>All the dragged data, for drags that do not come from a <see cref="DragSource"/>.</summary>
    public IDataObject? Data { get; init; }

    public DragDropEffects AllowedEffects { get; init; } = DragDropEffects.All;
}
