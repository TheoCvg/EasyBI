using System.Windows.Controls;
using EasyBI.Core;

namespace EasyBI.Wpf;

/// <summary>
/// The menu of a visual is opening (a right click on it or on one of its elements, the menu key): the application adds
/// its entries to <see cref="Menu"/>, acting on what the user points at (<see cref="Interaction"/>). The menu opens
/// when it has entries, unless <see cref="Cancel"/> is set.
/// </summary>
public sealed class VisualMenuOpeningEventArgs(VisualView view, InteractionContext interaction, ContextMenu menu) : EventArgs
{
    /// <summary>The view of the visual: the menu is placed over it.</summary>
    public VisualView View { get; } = view;

    /// <summary>The visual, the element under the pointer and everything that restricts its data.</summary>
    public InteractionContext Interaction { get; } = interaction;

    /// <summary>The menu, empty at first: add <c>MenuItem</c>s and <c>Separator</c>s to its items.</summary>
    public ContextMenu Menu { get; } = menu;

    /// <summary>Set to keep the menu closed, whatever its entries.</summary>
    public bool Cancel { get; set; }
}
