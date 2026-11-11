# Acting on what the user points at

A right click on a visual, or on one of its elements (a bar, a slice, a row, a cell), opens its menu. EasyBI leaves
the menu empty: your application adds entries that act on the data the user points at, such as opening another report
on the same filters, showing the rows behind a bar or exporting them. Without entries, no menu opens.

```csharp
context.VisualMenuOpening += (_, e) =>
{
    var point = e.Interaction;                          // what the user points at
    if (point.Element is null) return;                  // the visual as a whole: nothing to add here

    var details = new MenuItem { Header = "Show the sales" };
    details.Click += (_, _) => ShowSales(point.DataFilters);
    e.Menu.Items.Add(details);
};
```

## What the user points at

`InteractionContext` (EasyBI.Core) holds:

| Property | |
|---|---|
| `Visual` | The visual as the report defines it |
| `Selection` | The element as the renderer reported it (`CategorySelection`, `RowSelection`, `MatrixSelection`); null for the visual as a whole |
| `Element` | The dimensions identifying the element and their values: a bar of *North* in the *Web* series is `Region = North`, `Channel = Web` |
| `Measure` | The measure of the element (of its series), or the visual's only one |
| `Filters` | Every filter restricting the visual: its own, the report's filters, the selections made in the other visuals, with the values of the variables they use |
| `ElementFilters` | The filters selecting the element |
| `DataFilters` | `Filters` then `ElementFilters`: the data of the element |
| `Parameters` | The value of each parameter of the report |

`VisualView.InteractionFor(selection)` gives it for any element of a visual, without a menu (null: the visual as a
whole). Outside WPF, `InteractionContext.Create(visual, request, selection, filters, variables)` builds it from what
a renderer drew.

## Opening a report with imposed values

The report you open from a menu can receive filters and values of parameters for a while, without them being saved
with it:

```csharp
filters.Impose([new ReportFilter("sales", point.ElementFilters[0])]);    // shown and applied like the others
context.Variables.Impose(new Dictionary<string, string?> { ["Year"] = "2025" });

report = report with { Filters = filters.SavedFilters };     // what the user had, not what was imposed
```

- `FilterContext.Impose` replaces the filters on the same fields; `SavedFilters` gives the filters to save, with
  those the imposed ones replaced. `VariableContext.Impose` does the same for parameters (`SavedVariables`), which a
  `DashboardView` saves.
- A filter or a value the user changes after it was imposed becomes theirs, and is saved.
- `ClearImposed()` puts back what was replaced; `Load` forgets the imposed values.
- `IsImposed(...)` tells which are imposed, to mark them.

## In a connector

A renderer reports a right click through `RenderRequest.OpenMenu`, with the element under the pointer (null for
none). EasyBI's renderers do it; one that does not still gets the menu of the visual as a whole.

```csharp
if (request.OpenMenu is { } openMenu)
    chart.MouseRightButtonUp += (_, e) =>
    {
        openMenu(ElementAt(e.GetPosition(chart)));         // CategorySelection, RowSelection... or null
        e.Handled = true;
    };
```

With OxyPlot, the right button opens the menu instead of panning the chart.
