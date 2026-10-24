# Filtering: global filters and cross-filtering

A `FilterContext` holds the filtering state of a dashboard. Set once on the container (it is an
inherited property), it is used by the `FilterBar` and by every `VisualView`:

```xml
<DockPanel bi:VisualView.FilterContext="{Binding Filters}">
    <bi:FilterBar DockPanel.Dock="Top" Context="{Binding Context}" />
    <!-- ... the dashboard's VisualViews ... -->
</DockPanel>
```

- **Global filters** (slicers): added from the `FilterBar`, they apply to every visual of the same
  model and are saved with the report (`ReportDefinition.Filters`).
- **Cross-filtering**: clicking a bar, a point, a slice or a table row filters the other visuals of
  the same model on the element's dimensions (axis, plus legend for a bar). Ctrl+click adds an element to the
  selection of its visual, or removes it: the other visuals keep the rows of any selected element. Ctrl+click in
  another visual adds that visual's own selection (`FilterContext.Selections`, one per visual): a visual is filtered by
  the selections of all the others, "(Region = North or South) and Channel = Web". A plain click replaces every
  selection, clicking the only selected element again clears it. A visual is not filtered by its own selection: it dims
  what is left out of it and shows a summary of it, whose ✕ clears it (`ClearSelection(visualId)`); the `FilterBar`
  sums them all up and clears them all.
  Periods (quarter, month...) filter through `FilterSpec.Grain`.
- **Period filters**: a date dimension, in the `FilterBar` or on the designer's Filters shelf, is filtered on a
  period rather than on a list of values: between two dates, the current day / month / quarter / year, the last n
  (current one included: "Last 12 months") or the n previous complete ones ("Last month"). Relative periods are
  resolved when the query runs, so a saved report keeps showing the last 12 months; the date column is compared to
  the period's bounds (`date >= start AND date < end`), so a database can use an index on it.
- **Numeric filters**: a number dimension, in the `FilterBar` or on the designer's Filters shelf, is filtered on a list
  of values or compared with numbers: between two numbers (inclusive, one end may stay open), greater / less than, at
  least / at most. A **measure** dropped on the designer's Filters shelf filters the visual's groups by their
  aggregated value ("regions whose revenue ≥ 10,000"): the comparison runs after the grouping (SQL `HAVING`), before
  the sort and `Top`. Calculated measures can filter too, shown or not. Measures only filter a visual: their meaning
  depends on its grouping, so the `FilterBar` and the report's filters do not offer them.

```csharp
PeriodFilters.Relative("Date", FilterOperator.Last, DateGrain.Month, 12);     // last 12 months
PeriodFilters.Between("Date", new DateOnly(2024, 1, 1), null);                // since January 1st, 2024
PeriodFilters.Describe(filter);                                               // "Last 12 months", localized
NumericFilters.Range("Quantity", 10, null);                                   // Quantity >= 10
NumericFilters.Compare("Amount", FilterOperator.GreaterThanOrEqual, 10_000);  // on a measure: groups >= 10,000
NumericFilters.Describe(filter);                                              // "≥ 10,000", localized
```

A connector only has to call `request.Select?.Invoke(new CategorySelection(category, series))` on
click (or `RowSelection` for a table): `Select` is null when interactivity is disabled, for example
in the designer preview. When the visual is the source of the selection, `request.Highlight` lists the selected
elements: draw them as usual and the others with `SelectionFilters.DimmedOpacity`, using
`request.IsEmphasized(category, series)` (or `IsRowEmphasized(row)`). The OxyPlot connector dims bars and slices one
by one, but lines and points by series only.
