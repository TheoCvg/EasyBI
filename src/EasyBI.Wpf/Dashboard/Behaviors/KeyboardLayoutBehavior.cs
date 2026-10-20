using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Arranges the selected items with the keyboard: Ctrl+arrows move the selected item (<see cref="DashboardCommands.MoveItem"/>),
/// Ctrl+Shift+Down/Up put it in a new band (<see cref="DashboardCommands.MoveItemToNewBand"/>), Alt+arrows extend the
/// selected items over the free room next to them (<see cref="DashboardCommands.ExtendItem"/>). Remove it to keep the
/// selection without these shortcuts; the commands stay available to templates and toolbars.
/// </summary>
public class KeyboardLayoutBehavior : DashboardBehavior
{
    protected override void OnAttach() => View.PreviewKeyDown += OnPreviewKeyDown;

    protected override void OnDetach() => View.PreviewKeyDown -= OnPreviewKeyDown;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Typing in a text box (in-place text editing, a slot's content) is not for the layout
        if (!CanEdit || e.OriginalSource is TextBoxBase || View.SelectedItems.Count == 0) return;
        // With Alt, WPF reports the key as a system key
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Side(key) is not { } side) return;
        var modifiers = Keyboard.Modifiers;
        RoutedCommand? command = e.Key == Key.System && modifiers == ModifierKeys.Alt ? DashboardCommands.ExtendItem
            : modifiers == (ModifierKeys.Control | ModifierKeys.Shift) ? DashboardCommands.MoveItemToNewBand
            : modifiers == ModifierKeys.Control ? DashboardCommands.MoveItem
            : null;
        if (command is null) return;
        if (command.CanExecute(side, View)) command.Execute(side, View);
        // Taken even when nothing moves: WPF would otherwise move the focus to another control
        e.Handled = true;
    }

    private static ItemSide? Side(Key key) => key switch
    {
        Key.Left => ItemSide.Left,
        Key.Right => ItemSide.Right,
        Key.Up => ItemSide.Above,
        Key.Down => ItemSide.Below,
        _ => null,
    };
}
