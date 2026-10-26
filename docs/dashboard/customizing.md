# Customizing the dashboard

The `DashboardView` owns the content and the layout; every interaction is a **behavior** you can configure, replace or
remove. Events let your application take over dialogs or veto changes; commands and styles let you rebuild its look.

## Behaviors

| Behavior | What users get |
|---|---|
| `AddButtonsBehavior` | "+" on the right and bottom edges of an item, between lines and between bands |
| `FreeSpaceBehavior` | A placeholder in each free space, to add an item there |
| `FieldDropBehavior` | Visuals made from fields dragged from a `FieldList` |
| `MoveBehavior` | The grip to drag an item, with live room-making, auto-scroll and Escape to cancel |
| `ResizeBehavior` | The corner handle |
| `ItemToolbarBehavior` | Automatic size, edit, remove; text size, bold, alignment; image fit |
| `SelectionBehavior` | Click, Ctrl+click, selection box, arrows, Escape |
| `KeyboardLayoutBehavior` | Ctrl+arrows to move, Alt+arrows to extend |
| `BandMenuBehavior` | The ⋮ menu of a band (`MenuOpening` to change it) |
| `BandScrollBehavior` | The scroll bar and Shift+wheel of Scroll bands |
| `TextEditBehavior` | Double-click a text to edit it in place |
| `UndoRedoBehavior` | Ctrl+Z / Ctrl+Y |

They are attached by default. **Declaring one replaces its default**, to configure it:

```xml
<bi:DashboardView Context="{Binding Context}">
    <i:Interaction.Behaviors>
        <bi:UndoRedoBehavior MaxHistory="20" />
        <bi:AddButtonsBehavior CanInsertBands="False" />
    </i:Interaction.Behaviors>
</bi:DashboardView>
```

To pick them all yourself, set `UseDefaultBehaviors="False"` and declare the ones you want;
`DashboardView.CreateDefaultBehaviors()` gives the default list.

??? example "Your own behavior"
    Derive from `DashboardBehavior`: it gives the view, its panel and the overlay where behaviors draw their controls.
    `DragGesture` handles press-drag-release gestures, with auto-scroll (`AutoScroll`, `ScrollsBands`).

## Events

| Event | Raised | To |
|---|---|---|
| `EditVisualRequested` | A visual is to be edited (✎, a new visual) | Open your own editor: set `Handled`, then call `Complete(visual)` when it closes |
| `ImageRequested` | An image is to be picked | Replace the file dialog: `Handled`, then `Complete(image)` |
| `TextEditRequested` | A text is to be edited | Replace the in-place editor |
| `ItemRemoving` | An item is about to be removed | Veto it: `Cancel = true` |
| `Changed` | After every change, with the documents before and after | Save, track, sync |
| `DocumentLoaded` | After `Load` | Reset your own state |
| `SelectionChanged` | The selection changed | Show the selection's properties |

```csharp
dashboard.ItemRemoving += (_, e) =>
    e.Cancel = MessageBox.Show($"Remove {e.Item.Visual?.Title}?", "", MessageBoxButton.YesNo) != MessageBoxResult.Yes;
```

## Commands

`DashboardCommands` trigger every action from your own buttons and templates (commands acting on an item take the item
containing them):

`AddVisual`, `AddText`, `AddImage`, `EditItem`, `RemoveItem`, `ResetSize`, `AddRight`, `AddBelow`, `InsertBand`,
`RemoveBand`, `Duplicate`, `MoveItem`, `MoveItemToNewBand`, `ExtendItem`.

```xml
<Button Content="Move down" Command="{x:Static bi:DashboardCommands.MoveItem}" CommandParameter="Below" />
```

The standard `ApplicationCommands` work too: `Delete`, `SelectAll`, `Copy`, `Cut`, `Paste`, `Undo`, `Redo`.

## Slots

| Slot | Content | Data context |
|---|---|---|
| `DashboardSlots.Body` | The panel, in its scroll viewer | The view's |
| `DashboardSlots.ItemContent` | What an item shows | The `DashboardItem` |
| `DashboardSlots.EmptyItemChoices` | The choices of an empty item (visual, text, image) | The `DashboardItem` |
| `DashboardSlots.ItemToolbar` | The toolbar of an item | The `DashboardItem` |

The item slots are repeated once per item, so they take a template:

```xml
<bi:Slot Name="{x:Static bi:DashboardSlots.ItemToolbar}">
    <bi:Slot.ContentTemplate>
        <DataTemplate>
            <Button Content="⤓" ToolTip="Export" Command="{Binding DataContext.ExportCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                    CommandParameter="{Binding Visual}" />
        </DataTemplate>
    </bi:Slot.ContentTemplate>
</bi:Slot>
```

See [Slots](../customization/slots.md).

## Styles and templates

- `DashboardView` is lookless: its default template shows its `Panel` in a scroll viewer, under an `AdornerDecorator`
  where behaviors draw. A template of yours shows the panel with
  `<ContentPresenter Content="{Binding Panel, RelativeSource={RelativeSource TemplatedParent}}" />`.
- `DashboardItem` is lookless too, with an implicit style; `IsSelected` (the default template draws a ring) and
  `IsInViewport` are there for templates.
- The controls behaviors draw take styles under `DashboardOverlay` keys: `PlusButtonStyleKey`, `IconButtonStyleKey`,
  `GripStyleKey`, `ResizeGripStyleKey`, `FreeSpaceStyleKey`, `DropPlaceholderStyleKey`, `DragGhostStyleKey`,
  `SelectionBoxStyleKey`.
