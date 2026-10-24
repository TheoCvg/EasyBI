# The designer

The `ReportDesigner` has three zones: the fields on the left, the wells and the preview in the middle, the
**properties** on the right (both side panels fold: `IsFieldPanelOpen`, `IsPropertiesPanelOpen`).

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
designer.ViewModel!.PropertiesBuilding += (_, e) =>
{
    if (e.Selection is ShelfItemViewModel { IsValue: true } chip)
        e.Sections.Add(new DesignerPropertySection("Notes", () => "Notes",
            [DesignerProperty.Text("Comment", () => "Comment", () => Comments.Get(chip), text => Comments.Set(chip, text))]));
};
```
