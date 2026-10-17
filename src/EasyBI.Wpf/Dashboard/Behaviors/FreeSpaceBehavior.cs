using System.Windows;
using System.Windows.Controls;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Fills the free spaces of the panel (room left next to constrained items, see <see cref="DashboardFreeSpace"/>)
/// with a placeholder offering to add an item there; the new item takes that room. The placeholder is a <c>Button</c>
/// styled with <see cref="DashboardOverlay.FreeSpaceStyleKey"/>. Hidden in read-only dashboards.
/// </summary>
public sealed class FreeSpaceBehavior : DashboardBehavior
{
    private readonly List<Button> _buttons = [];

    protected override void OnAttach() => Panel.Arranged += OnArranged;

    protected override void OnDetach()
    {
        Panel.Arranged -= OnArranged;
        _buttons.Clear();
    }

    private void OnArranged(object? sender, EventArgs e)
    {
        // One placeholder per free space, created as spaces appear and hidden when there are fewer
        while (_buttons.Count < Panel.FreeSpaces.Count)
        {
            var index = _buttons.Count;
            var button = CreateButton(DashboardOverlay.FreeSpaceStyleKey, null, "AddHere", () => Add(index));
            // The new item takes the place at once
            DashboardOverlay.SetStaysUnderPointer(button, false);
            _buttons.Add(AddToOverlay(button, (_, _) => Place(index)));
        }
    }

    private Rect? Place(int index) => CanEdit && !Overlay.IsLocked && index < Panel.FreeSpaces.Count ? Panel.FreeSpaces[index].Bounds : null;

    private void Add(int index)
    {
        if (CanEdit && index < Panel.FreeSpaces.Count) View.AddIn(Panel.FreeSpaces[index]);
    }
}
