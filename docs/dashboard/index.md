# Dashboard

Arranging visuals is a WPF concern: the report (`easybi/v1`) never contains a layout. The dashboard is
built in three layers, each usable on its own.

**The layout** (`DashboardLayoutDefinition`) is a stack of bands, each with a mode. Its items are **greedy**:
without a constraint, they share the room their neighbours leave, so a dashboard never has a gap.

- **Fit** bands are a grid of 12 columns that fills the width. A band is a stack of lines; a line is an item, or a
  container (`LayoutNode.Row`: side by side, `LayoutNode.Column`: stacked) of items and containers. Along a
  container, constrained children keep their size (`ColumnSpan` in columns, `RowSpan` in rows) and the others share
  the rest; across it, every child takes the whole room. An item with a `RowSpan` sets the height of its line and the
  items next to it stretch, so items stacked beside a tall item share its height.
- **Column tiers**: on a narrow panel, Fit bands show fewer columns (`DashboardPanel.ColumnTiers`; by default
  1 column under 600 px and 6 under 960 px, `ColumnTier.Defaults`). Spans are scaled to the tier, a line whose items
  would get less than 3 columns each is broken into several lines (one item per line in a single column), and
  constrained items keep their proportions while filling their line. Only the display adapts: the saved layout does
  not change, and a resize in a tier is saved in the layout's columns (`DashboardPanel.ActiveColumns`, `ToLayoutSpan`).
- **Wrap** bands start their items at `DashboardPanel.MinItemWidth` (or their fixed `Width` in pixels), break lines
  when the next one does not fit, then share what is left on each line. **Scroll** bands do the same on one line,
  and scroll horizontally when the items do not fit.
- A container whose children are all constrained leaves a **free space**, which the dashboard offers to fill.

```csharp
new DashboardLayoutDefinition
{
    Bands =
    [
        new BandLayout
        {
            Items =
            [
                new LayoutNode { VisualId = title.Id, RowSpan = 1 },                                // a whole line
                LayoutNode.Row(
                    new LayoutNode { VisualId = map.Id, ColumnSpan = 6, RowSpan = 6 },               // left half, 2 lines high
                    LayoutNode.Column(LayoutNode.Item(sales.Id), LayoutNode.Item(margin.Id))),       // stacked beside it
            ],
        },
        new BandLayout { Mode = BandMode.Scroll, Items = [LayoutNode.Item(a.Id), new LayoutNode { VisualId = b.Id, Width = 360 }] },
    ],
};
```

**`DashboardPanel`** shows a layout (`Layout` property); children are matched to its items by the attached
`DashboardPanel.ItemId`. The geometry itself is a pure function (`BandArrangement.Arrange`), and the panel exposes
hit tests for interactions (`EdgeAt`, `FlowIndexAt`, `FreeSpaces`, and `CaptureGeometry` to hit-test the layout from
before a drag).

**`DashboardLayout`** is the static class that changes a panel's layout while keeping it consistent: `AddNextTo`,
`MoveNextTo` (`ItemSide.Right`, `Left`, `Below`, `Above`), `InsertAt` / `MoveTo` (a new line), `Resize`,
`ResizeWidth`, `SetAuto`, `Remove`, `InsertBand`, `RemoveBand`, `SetBandMode`, plus `Update` for custom
operations, `Capture` (snapshot to restore) and `GetLayout` / `SetLayout` (to save and restore). Next to an item, a
side along its container joins it (the line's columns are shared when it has no room left), a side across it
wraps the item and the newcomer in a new container. The rules are pure functions on records in
`DashboardLayoutEngine`, usable without any UI.

**`DashboardView`** is the ready-made (lookless) control: `DashboardItem`s showing visuals in a
`DashboardPanel`, with every interaction provided by a **behavior** (Microsoft.Xaml.Behaviors):

| Behavior | Feature |
|---|---|
| `AddButtonsBehavior` | "+" on the right and bottom edges of an item, "+" between lines and between bands |
| `FreeSpaceBehavior` | fills free spaces with a placeholder that adds an item there |
| `FieldDropBehavior` | makes visuals from fields dragged from a `FieldList` (see below) |
| `MoveBehavior` | grip to drag an item: a picture follows the pointer and a placeholder shows where it lands, the others making room live; near an item's edge it goes next to it (like "+"), between two lines it becomes a line, over a free space it fills it; across bands, auto-scroll near the edges; Escape cancels |
| `ResizeBehavior` | corner handle: constrains the item (columns and rows in a grid band, pixels and rows elsewhere) |
| `ItemToolbarBehavior` | automatic size (for a resized item), edit, remove; text size, bold, alignment; image fit |
| `SelectionBehavior` | click selects an item (Ctrl+click adds or removes it, except on a bar, slice or row of a chart, where it extends the cross-filtering: `VisualView.DataSelectedEvent`), click on the background unselects, drag from the background draws a selection box; arrows go to the nearest item (Shift adds it), Escape unselects |
| `KeyboardLayoutBehavior` | Ctrl+arrows move the selected item, Ctrl+Shift+Down/Up put it in a new band, Alt+arrows extend the selected items over the free room next to them (through the commands below) |
| `BandMenuBehavior` | ⋮ menu of a band: Fit / Scroll / Wrap, insert, remove (`MenuOpening` to customize it) |
| `BandScrollBehavior` | scroll bar and Shift+wheel for scrolling bands |
| `TextEditBehavior` | double-click a text to edit it in place |
| `UndoRedoBehavior` | Ctrl+Z / Ctrl+Y (`ApplicationCommands.Undo` / `Redo`) |

```xml
<bi:DashboardView x:Name="Dashboard" Context="{Binding Context}" Document="{Binding Dashboard}">
    <i:Interaction.Behaviors>
        <!-- Declaring a behavior replaces its default; set UseDefaultBehaviors="False" to pick them all -->
        <bi:UndoRedoBehavior MaxHistory="100" />
    </i:Interaction.Behaviors>
</bi:DashboardView>
```

Hooks for applications:

- events `EditVisualRequested` (replace the built-in designer dialog), `ImageRequested` (replace the file
  dialog), `TextEditRequested`, `ItemRemoving` (veto), `Changed` (every change, with the documents before
  and after), `DocumentLoaded`;
- the item selection: `SelectedItems`, `SelectionChanged`, `Select(items, DashboardSelectionMode.Replace/Add/Toggle)`,
  `SelectAll`, `UnselectAll`, `RemoveSelected` and `DuplicateSelected` (one change each), also reached through
  `ApplicationCommands.Delete` (Del), `ApplicationCommands.SelectAll` (Ctrl+A) and `DashboardCommands.Duplicate`
  (Ctrl+D); `DashboardItem.IsSelected` for templates (the default one draws a ring) and the
  `DashboardOverlay.SelectionBoxStyleKey` style. A read-only dashboard has no selection;
- lazy loading: only the visuals of the items in or near the visible part of the dashboard query their data
  (`DashboardItem.IsInViewport`, bound to `VisualView.IsActive` by the default template); the others query once they
  scroll into view, a change of filters or data giving them one query then. `ViewportMargin` sets how far ahead items
  load (one viewport by default), `LoadsVisibleItemsOnly = false` loads them all (to print or export, for instance).
  `VisualView.IsActive = false` holds back the queries of any visual;
- keyboard layout: `MoveSelected(side, toNewBand)` (`DashboardCommands.MoveItem` and `MoveItemToNewBand`, Ctrl+arrows
  and Ctrl+Shift+Down/Up; `DashboardLayoutEngine.Step`): Down takes an item
  sharing its line to a new line below; alone on its line, it joins the next line, and from the last line it goes into
  the band below (created past the last one); Up the same upwards, so that Down and Up undo each other; Ctrl+Shift+Down/Up puts it in a new band below/above; Left/Right swap it with its
  neighbour. `ExtendSelected(width)` (`DashboardCommands.ExtendItem`, Alt+arrows; `DashboardLayoutEngine.Fill`) extends the selected items over the
  free room next to them without shrinking their neighbours;
- copy and paste of the selected items, possibly between dashboards: `CopySelected` / `CutSelected` / `Paste`
  (Ctrl+C, Ctrl+X, Ctrl+V through `ApplicationCommands`; `CreateCopy` and `Paste(data)` work without the clipboard).
  The clipboard holds an `easybi/v1` report (format `VisualTransfer.ClipboardFormat`) with the visuals and the
  calculated measures they use, plus their titles as text, and their layout (format
  `DashboardView.LayoutClipboardFormat`: their arrangement and sizes, `DashboardLayoutEngine.Extract`). Pasted items get
  new ids, keep their arrangement and sizes, and go after the line of the last selected item, or at the end
  (`DashboardLayoutEngine.Insert`; lines of a grid band join the anchor's grid band, other bands come as new bands);
  without the layout format, each one goes to the right of the previous one. Visuals of a model the context does not
  know are left out, and a calculated measure whose name is taken by another definition is renamed (`VisualTransfer`
  in `EasyBI.Core`);
- `DashboardCommands` (`AddVisual`, `AddText`, `AddImage`, `EditItem`, `RemoveItem`, `ResetSize`,
  `AddRight`, `AddBelow`, `InsertBand`, `RemoveBand`, `Duplicate`, `MoveItem`, `MoveItemToNewBand`, `ExtendItem`
  — the last three take an `ItemSide` or its name, as `CommandParameter="Below"`...) for custom templates, and public methods
  (`AddNextTo`, `AddIn` a free space, `InsertLine`, `SetAutoSize`, `SetVisual`, `InsertBand`, `BeginEdit` to group
  changes into one undo step...);
- a default template for `DashboardView` (its `Panel` in a scroll viewer, under an `AdornerDecorator` where behaviors
  draw: a custom template shows it with `<ContentPresenter Content="{Binding Panel, RelativeSource={RelativeSource TemplatedParent}}" />`),
  an implicit style for `DashboardItem` (lookless control) and the `DashboardOverlay` style keys
  (`PlusButtonStyleKey`, `IconButtonStyleKey`, `GripStyleKey`, `ResizeGripStyleKey`, `FreeSpaceStyleKey`,
  `DropPlaceholderStyleKey`, `DragGhostStyleKey`) to restyle;
- `DashboardBehavior` as a base class for new interactions: it gives access to the view, the panel and the
  overlay where behaviors draw their controls; `DragGesture` handles press-drag-release gestures (with
  auto-scroll: `AutoScroll`, `ScrollsBands`).

`IsReadOnly="True"` hides the editing chrome; clicks keep cross-filtering in every mode.

**Building visuals by dragging fields.** `FieldList` shows the fields of the context's models (model picker,
dimensions, measures) and drags them as `ModelField`s. Placed next to a dashboard, it lets users build it directly:

```xml
<DockPanel>
    <bi:FieldList DockPanel.Dock="Left" Width="210" Context="{Binding Context}" />
    <bi:DashboardView Context="{Binding Context}" />
</DockPanel>
```

- dropped where an item can go (an edge, between two lines, a free space, a scrolling or wrapping band), a field
  makes a new visual: a KPI for a measure, the first measure by that dimension for a dimension (a line by month for a
  date, bars otherwise); the other items make room live around a placeholder;
- dropped on an empty item, it fills it; dropped on a visual, it is added to it (a measure to its values, a dimension
  to its axis, then its legend; a dimension breaks a KPI down into bars);
- the rules are pure functions in `FieldVisuals` (`Create`, `AddField`); `FieldDropBehavior.CreateVisual` and
  `AddField` replace them. Each drop is one undo step.

The designer's field panel is the same `FieldList` (its shelves accept the `ModelField`s of the selected model).
A search box at the top of the field list (`FieldList.Search`) and above the values of a filter (from 8 values on,
`FilterItemViewModel.Search`; checked values that are not shown stay checked) narrow the lists down: case and accents
are ignored and every word must appear (`TextMatch.Matches` in `EasyBI.Core`).

In the designer, the visual is sorted from the properties of a chip of the Axis or Values well (ascending ↑ or
descending ↓; one sorted field at a time, the sort following the field when its aggregation or grain changes). The
visual's properties recall the active sort and limit the visual to its first rows (`Top`).
`DashboardDropTarget.Find`, the place an item dropped at a point goes, is shared by moves and field drops.
