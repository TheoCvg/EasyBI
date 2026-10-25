# The designer

The `ReportDesigner` shows a `DesignerViewModel`, its data context, which the application creates and keeps: it loads a
visual into it, reads the visual being designed, and gives it its rules.

```xml
<bi:ReportDesigner DataContext="{Binding Designer}" />
```

```csharp
public DesignerViewModel Designer { get; } = new(context);

Designer.Load(visual);              // edit a visual (Clear() starts a new one)
var designed = Designer.Visual;     // the visual being designed
```

The designer has three zones: the fields on the left, the wells and the preview in the middle, the
**properties** on the right (both side panels fold: `IsFieldPanelOpen`, `IsPropertiesPanelOpen`).

![The designer: fields on the left, wells and preview in the middle, the properties of the selected chip on the right](../images/designer.png)

- **Chips** show their field and badges summing their settings up (Σ, avg, Quarter, PY, Y2, ↓...). A click selects one
  (`DesignerViewModel.SelectedItem`) and the properties panel shows its settings in sections: aggregation, granularity,
  time calculation, chart type, secondary axis, sort for a field; the editor of a filter. Without a selection it shows
  the visual's: title, sort, Top N, its tooltips (charts), and the options of the renderer.
- **The visual type** is chosen in a gallery grouped by use (comparison, trend, composition, indicators and tables);
  types no renderer draws are greyed.
- **Wells follow the type**: a pie shows "Category" and "Value", a table "Rows", a KPI only "Values", only charts
  "Tooltip"; a well the type
  does not use refuses fields and stays shown while it still holds some. Empty wells say what to drop, and while a
  field or a chip is dragged the wells that accept it light up (`DragSource.DragStarted`).
- **Undo and redo** every change of the visual (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z, the header's buttons,
  `UndoCommand`/`RedoCommand`; 100 steps, `MaxHistory`). Loading another visual starts a new history.
- **Keyboard**: arrows go from chip to chip (Up/Down from well to well), Ctrl+Left/Right move the selected chip within
  its well, Delete removes it, Escape selects the visual again.

Everything stays replaceable: the template is lookless and made of slots (see below); the properties are descriptors
(`DesignerProperty`: toggle, choice, number, text, info or any object shown with its `DataTemplate`, grouped in
`DesignerPropertySection`s), which `DesignerViewModel.PropertiesBuilding` lets an application add, remove or reorder;
each kind of editor is a `DataTemplate` under a `DesignerPropertyTemplates` key; the icons of the types are geometries
under `VisualTypeIcons` keys.

```csharp
Designer.PropertiesBuilding += (_, e) =>
{
    if (e.Selection is ShelfItemViewModel { IsValue: true } chip)
        e.Sections.Add(new DesignerPropertySection("Notes", () => "Notes",
            [DesignerProperty.Text("Comment", () => "Comment", () => Comments.Get(chip), text => Comments.Set(chip, text))]));
};
```

## Rules: what the end user may change

Every change of the visual, whatever the action (a type picked, a field dropped or moved, a chip removed, a property
edited), passes the designer's **rules**. A rule looks at the change, `VisualChange` (the visual `Before` and
`After`), and returns null to accept it, or the message telling why it is refused: the visual stays as it was and the
message shows above the wells (`DesignerViewModel.Refusal`). The first refusal wins.

```csharp
Designer.Rules.Add(change => change.Changes(v => v.Type) ? "The type of this visual is fixed" : null);
Designer.Rules.Add(change => change.FieldsChanged ? "The fields of this visual are fixed" : null);   // their settings can change
Designer.Rules.Add(change => change.After.Values.Count > 3 ? "Three measures at most" : null);
```

`Changes(v => ...)` compares a part of the visual by value; `FieldsChanged` tells whether a field was added, removed or
moved in a well (axis, legend, values, tooltip, filters), not whether its settings changed (aggregation, format, grain,
filter values). A rule looks at the visual, not at the designer's actions: a feature added to the designer later goes
through the same rules.

What a rule would refuse is shown beforehand: the gallery greys the refused types (with the message as tooltip), a well
says it refuses a dragged field, a chip loses its remove button. A text or a number typed in the properties panel is
checked once entered: refused, it comes back to its value. Rules are called to try changes: keep them quick and without
side effect. Loading a visual, clearing the designer, undoing and redoing do not go through them.

Rules are also declared in XAML, as behaviors of the `ReportDesigner`: EasyBI gives `LockVisualTypeBehavior`,
`LockFieldsBehavior` and `ReadOnlyDesignerBehavior` (each with an optional `Message`), and an application writes its own
by deriving from `DesignerRuleBehavior`.

```xml
<bi:ReportDesigner DataContext="{Binding Designer}">
    <i:Interaction.Behaviors>
        <bi:LockVisualTypeBehavior />
        <local:ThreeMeasuresAtMost />
    </i:Interaction.Behaviors>
</bi:ReportDesigner>
```

```csharp
public sealed class ThreeMeasuresAtMost : DesignerRuleBehavior
{
    protected override string? Check(VisualChange change) => change.After.Values.Count > 3 ? "Three measures at most" : null;
}
```

Hiding a part of the designer is another matter, done with its slots: `Slots.Hide(designer, ReportDesignerSlots.VisualTypes)`
hides the type button, a rule forbids the change whatever the way.
