# Filters

Filters keep the rows that matter: the last 12 months, three regions, the sales over 1,000 €. They run inside the
query, in the database.

| Filter | Set by | Applies to |
|---|---|---|
| **Global filter** | The filter bar | Every visual of its model |
| **Visual filter** | The designer's *Filters* well | Its visual |
| **Selection** | A click on a chart | The other visuals: see [Cross-filtering](cross-filtering.md) |

## The filter bar

![The filter bar: Date in the last 12 months, Region in three values, and the button to add a filter](../images/filtering/filter-bar.png#only-light)
![The filter bar: Date in the last 12 months, Region in three values, and the button to add a filter](../images/filtering/filter-bar-dark.png#only-dark)

A `FilterContext` holds the filters of a dashboard. Set it once on a container (it is inherited): the filter bar edits
it, and every `VisualView` inside applies it.

```xml
<DockPanel bi:VisualView.FilterContext="{Binding Filters}"> <!-- (1)! -->
    <bi:FilterBar DockPanel.Dock="Top" Context="{Binding Context}" />
    <bi:DashboardView Context="{Binding Context}" />
</DockPanel>
```

1. `public FilterContext Filters { get; } = new();` in your view-model.

Global filters are saved with the report, in `ReportDefinition.Filters`:

```csharp
var report = dashboard.Capture().Report with { Filters = Filters.Filters.ToArray() };   // save
Filters.Load(report.Filters);                                                           // open
```

## What can be filtered

### Values

Any dimension can be filtered on a list of values, checked in a list that users can search.

```csharp
new FilterSpec("Region", FilterOperator.In, ["Nord", "Sud"])
new FilterSpec("Channel", FilterOperator.NotIn, ["Web"])
```

### Periods

A date is filtered on a **period** rather than a list of values:

| Period | `FilterSpec` |
|---|---|
| Between two dates (an end may stay open) | `PeriodFilters.Between("Date", new DateOnly(2024, 1, 1), null)` |
| The current day, month, quarter or year | `PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)` |
| The last *n*, the current one included (*last 12 months*) | `PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12)` |
| The *n* previous complete ones (*last month*) | `PeriodFilters.Relative("Date", FilterOperator.Previous, DateGrain.Month)` |

Relative periods are resolved when the query runs: a saved report keeps showing the last 12 months. The date is
compared with the period's bounds (`date >= start AND date < end`), so the database can use an index on it.

### Numbers

A number dimension is filtered on values, or compared with numbers:

```csharp
NumericFilters.Range("Quantity", 10, null);                                   // Quantity >= 10
NumericFilters.Compare("Quantity", FilterOperator.LessThan, 5);               // Quantity < 5
```

Operators: `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Range` (inclusive, an end may stay
open).

### Measures

A **measure** dropped on the designer's *Filters* well filters the visual's groups by their total: *regions whose
revenue ≥ 10,000*. The comparison runs after the grouping (a SQL `HAVING`), before the sort and Top N.

```csharp
Filters = [NumericFilters.Compare("Amount", FilterOperator.GreaterThanOrEqual, 10_000)],
```

!!! note "Only in a visual"
    The meaning of a measure filter depends on the visual's grouping, so the filter bar does not offer measures.

## Describe a filter

`PeriodFilters.Describe(filter)` and `NumericFilters.Describe(filter)` give the text the filter bar shows, in the
user's language: *Last 12 months*, *≥ 10,000*.
