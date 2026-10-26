# The designer

The `ReportDesigner` lets end users build a visual by themselves: they drag fields onto wells, pick a visual type and
see the result live.

![The designer: fields, header, wells, preview and properties, numbered 1 to 5](../../images/designer/overview.png#only-light)
![The designer: fields, header, wells, preview and properties, numbered 1 to 5](../../images/designer/overview-dark.png#only-dark)

| | Zone | What the end user does there |
|---|---|---|
| **1** | Fields | Picks the model, searches a field, drags it (or double-clicks it). Creates calculated measures. |
| **2** | Header | Folds the field panel, undoes and redoes, names the visual, opens the [gallery of types](building-a-visual.md#pick-a-visual-type), folds the properties panel. |
| **3** | Wells | Drops fields: axis, legend, values, tooltip, filters. Their names and limits follow the type. |
| **4** | Preview | Sees the visual as it will show, with the real data. |
| **5** | Properties | Sets the selected chip (aggregation, grain, format...), or the visual when no chip is selected. |

## Show it

The designer needs an `EasyBIContext` (your models and renderers). Bind the visual being designed to keep what the end
user builds: `Visual` is two-way.

=== "XAML"

    ```xml
    <bi:ReportDesigner Context="{Binding Context}"
                       Visual="{Binding EditedVisual}" /> <!-- (1)! -->
    ```

    1. Two-way by default: every change of the end user comes back into `EditedVisual`.

=== "Code-behind"

    ```csharp
    designer.Context = context;
    designer.Visual = visual;            // (1)!
    // ...
    var designed = designer.Visual;      // (2)!
    ```

    1. Loads the visual, with a new history of changes. `null` starts a new visual.
    2. What the end user built, after every change.

No view-model is needed. The sample's **XAML only** tab shows a designer declared without any code.

!!! tip "Show the result elsewhere"
    `Visual` is a plain `VisualDefinition`: bind it to a `VisualView` to show the visual being designed somewhere else,
    or save it as JSON.

    ```xml
    <bi:VisualView Context="{Binding Context}" Definition="{Binding Visual, ElementName=Designer}" />
    ```

## Save what the end user built

`Visual` is ready to store: add it to a `ReportDefinition`, or serialize it on its own.

```csharp
if (VisualValidator.Validate(designer.Visual!, context.VisualTypes) is { } problem)
{
    MessageBox.Show(problem);            // (1)!
    return;
}
report = report with { Visuals = [.. report.Visuals, designer.Visual!] };
designer.Visual = null;                  // (2)!
```

1. A visual can be incomplete while it is designed: a pie without a category, a chart without a value...
2. Starts the next visual.

## Drive it from your buttons

`Undo` and `Redo` are WPF commands of the control: point a button at it.

```xml
<Button Content="Undo" Command="Undo" CommandTarget="{Binding ElementName=Designer}" />
<Button Content="Redo" Command="Redo" CommandTarget="{Binding ElementName=Designer}" />
```

| Property | |
|---|---|
| `Context` | The models and renderers the designer offers. Changing it starts a new designer state. |
| `Visual` | The visual being designed (two-way). `null`: a new visual. |
| `Rules` | What the end user may change: see [Rules](rules.md). |
| `IsFieldPanelOpen`, `IsPropertiesPanelOpen` | Whether the side panels are shown (two-way). Selecting a chip opens the properties. |
| `ViewModel` | The designer's state (`DesignerViewModel`, read-only): selection, undo history, properties panel. |

??? info "The view-model, for advanced use"
    The control creates a `DesignerViewModel` for its `Context` and gives it to its template as data context, so the
    content of its [slots](customizing.md#add-your-own-buttons) binds to it too. Reach it through `designer.ViewModel`
    for what the control does not expose: `SelectedItem`, `CanUndo`, `Refusal`, `PropertiesBuilding`...

## Next

<div class="grid cards" markdown>

- **[Building a visual](building-a-visual.md)**

    What end users do: the gallery, the wells, the chips, the keyboard.

- **[Rules](rules.md)**

    Lock the type, the fields, or anything of the visual.

- **[Customizing](customizing.md)**

    Your own buttons, properties, editors and icons.

</div>
