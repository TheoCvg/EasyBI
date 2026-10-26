# Dashboard

A dashboard shows several visuals that users arrange themselves: they drag fields onto it to make visuals, move and
resize them, filter them all at once, and click a chart to filter the others.

![The sample's dashboard tab: the filter bar, the field list and the dashboard, numbered 1 to 3](../images/dashboard/overview.png#only-light)
![The sample's dashboard tab: the filter bar, the field list and the dashboard, numbered 1 to 3](../images/dashboard/overview-dark.png#only-dark)

| | Control | Role |
|---|---|---|
| **1** | `FilterBar` | Global filters and the summary of the selections: see [Filters](../guide/filtering.md). |
| **2** | `FieldList` | The fields to drag onto the dashboard to make visuals. |
| **3** | `DashboardView` | The visuals, texts and images, arranged in bands. |

## Show it

```xml
<DockPanel bi:VisualView.FilterContext="{Binding Filters}"> <!-- (1)! -->
    <bi:FilterBar DockPanel.Dock="Top" Context="{Binding Context}" />
    <bi:FieldList DockPanel.Dock="Left" Width="210" Context="{Binding Context}" />
    <bi:DashboardView Context="{Binding Context}" Document="{Binding Dashboard}" /> <!-- (2)! -->
</DockPanel>
```

1. Shared by the filter bar and every visual: global filters and [cross-filtering](../guide/cross-filtering.md).
2. Two-way: every change users make comes back into `Dashboard`, a `DashboardDocument`.

Each part is optional: a dashboard without a field list is arranged with its "+" buttons, one without a filter context
does not cross-filter.

## Save and open

A `DashboardDocument` is the report (`easybi/v1`: visuals, filters, calculated measures) and its **layout**. It is saved
as JSON, with the schema `easybi-dashboard/v1`:

=== "Bound"

    ```csharp
    public DashboardDocument Dashboard { get; set; } = new();   // bound to Document

    File.WriteAllText(path, DashboardSerializer.Serialize(Dashboard));
    Dashboard = DashboardSerializer.Deserialize(File.ReadAllText(path));
    ```

=== "Code-behind"

    ```csharp
    File.WriteAllText(path, DashboardSerializer.Serialize(dashboard.Capture()));
    dashboard.Load(DashboardSerializer.Deserialize(File.ReadAllText(path)));   // (1)!
    ```

    1. A new undo history starts. `DocumentLoaded` is raised.

!!! tip "Plain reports open too"
    `DashboardSerializer.Deserialize` also reads an `easybi/v1` report without a layout: its visuals get a default one.
    The layout is a WPF matter and never goes into the report itself.

## Read-only

```xml
<bi:DashboardView Context="{Binding Context}" Document="{Binding Dashboard}" IsReadOnly="True" />
```

No editing chrome, no editing command, no selection. Clicks keep cross-filtering. `IsReadOnly` is inherited, so item
templates can use it.

## Performance

Only the visuals in or near the visible part of the dashboard query their data; the others query when they scroll into
view. A change of filters or data costs them one query then.

| Property | |
|---|---|
| `ViewportMargin` | How far ahead items load, in viewport sizes (1 by default: a screen above and below). |
| `LoadsVisibleItemsOnly` | `false` loads every visual, to print or export the whole dashboard. |

## Next

<div class="grid cards" markdown>

- **[What users do](using.md)**

    Build visuals from fields, move, resize, select, copy and paste, undo.

- **[Layout](layout.md)**

    Bands, columns and lines; what the layout looks like in code.

- **[Customizing](customizing.md)**

    Behaviors, events, commands, styles and slots.

</div>
