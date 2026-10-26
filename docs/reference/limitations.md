# Known limitations

- A filter on a measure compares the measure as the model aggregates it, whatever aggregation the visual shows.
- Relative periods ("last 12 months") are resolved in the local time zone when a visual queries; an open dashboard does
  not query again at midnight.
- `Top` applies to aggregated rows, not to categories when a legend is present.
- Grouping, sorting and `Top` are pushed down to the provider, and execution is asynchronous when the
  source implements `IAsyncEnumerable` (EF Core). The SQL translation was checked manually with EF Core 8 on SQLite
  (every query shape, including date granularities, navigation properties and sort on an aggregate); there is no
  automated test against a database. SQLite cannot sum `decimal` columns (EF Core refuses the query).
- At most 4 grouping dimensions (axis + legend).
- The built-in light and dark themes do not restyle standard controls (buttons, combo boxes...): use a control
  library for those (see [Following a control library](../customization/theming.md#following-a-control-library)).
- `EasyBI.Themes.WpfUi` is checked against WPF UI's light and dark themes; its high contrast themes are not verified.
- Dashboard images are embedded in the file (2 MB by default, `DashboardView.MaxImageBytes`).
