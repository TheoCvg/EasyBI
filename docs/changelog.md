# Changelog

Every version, with its breaking changes. EasyBI follows [Semantic Versioning](https://semver.org/): while in `0.x`,
breaking changes bump the minor version. The report format (`easybi/v1`) stays backward compatible: a newer version
opens older reports, and a report using a newer feature cannot be opened by an older version.

## 0.5.0-beta.1 — 2026-10-26

### Added

- **Cross-filtering**: several selections with Ctrl+click, highlighted in their visual (#41).
- **Numeric filters**: number dimensions and the measures of a visual compared with numbers (≥, <, between...) (#42).
- **Matrix** crossing rows with columns, with totals at every level (#43).
- **Conditional formatting**: rules, color scales, data bars and icons on the values of a measure (#44); chart points
  colored by them, and the zones of rules shaded (#45).
- **Donut, gauge and waterfall** visual types (#46).
- **Tooltips** customized by the end user (extra measures, template) and the developer (`EasyBIContext.TooltipOpening`) (#47).
- **Legends**: position chosen per visual, long legends paged (#48).
- **Several axis fields** shown as groups, and dimension values relabeled per visual (#50).
- **Formulas with dimensions**: `SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNT`, `DISTINCTCOUNT` and `IF`, with conditions
  (`SUM([Revenue]; [Region] = "North")`) (#53).
- **Measures declared in code editable in the application**: `QueryableSemanticModel.Formula(...)`, and any model measure
  redefined in a report, with Restore (#54).
- **Redesigned calculated measure editor**: colored formula, located problems, smart suggestions, catalog in tabs,
  operator keypad (#55).
- **Measures used in other formulas**: a calculated measure can use the report's other measures; loops are refused (#56).
- This documentation site, with a version per release.
- **Designer rules**: every change of the visual being designed passes the designer's `Rules`, which look at the
  change (`VisualChange`: `Changes(v => ...)`, `FieldsChanged`) and refuse it with a message; what they would refuse
  is shown unavailable beforehand. Also declared as behaviors: `LockVisualTypeBehavior`, `LockFieldsBehavior`,
  `ReadOnlyDesignerBehavior`, or one of the application's derived from `DesignerRuleBehavior` (#65).
- **Visual types described by their definition**: `VisualTypeDefinition` gives a type's name, gallery section, wells
  (used, number of fields, name), tooltips, format and rules, and `VisualTypeCatalog` holds EasyBI's; the designer, the
  validation and the fields added to a visual follow it. The designer refuses a second category on a pie or a donut
  (#59).
- **Custom visual types**: an application declares its own types (`EasyBIContext.AddVisualType`, or a renderer's
  `RendererCapabilities.VisualTypes`), with their wells, settings (`VisualTypeDefinition.Options`, read with
  `GetTypeOption`) and rules, and an icon by key (`{bi:VisualTypeIconKey company.type}`, a default one otherwise); it
  hides or changes EasyBI's types in `EasyBIContext.VisualTypes`. The designer refuses a measure beyond what a type takes
  (a second value on a pie), a visual tells which well misses or has too many fields, and a matrix takes four row
  fields as documented (#60).
- **Failed visuals explained**: a visual whose query or renderer throws says it could not be displayed, with Retry and
  the details of what happened (to copy into a bug report) instead of the exception's message;
  `EasyBIContext.VisualFailed` lets the application log the failure, change the message or hide the details (#73).

### Fixed

- LiveCharts2: the labels under groups turn like other category labels instead of being skipped (#51).
- Designer: the measure preview no longer cancels a query on every keystroke (#52).
- Comparisons with the previous year: a period under way (this year, the last months, a range ending later, the
  current year or month clicked) is compared with the same days a year earlier, not with the whole period; a range
  without an end no longer takes in this year's data (#62).
- Charts: an axis drawing bars starts at zero also when a line shares it (a year in bars and the year before as a line),
  and ends at zero for negative bars; OxyPlot no longer cuts negative bars (#63).
- Theming: an `EasyBIThemeMapping` that maps `ChartPaletteKey` gives charts the design system's palette instead of
  EasyBI's (#64).
- Nullable dates (`DateTime?`, `DateOnly?`, `DateTimeOffset?`) by year, quarter, month or day: EF Core can translate
  the query again (it failed with SQL Server, SQLite...), and missing dates form an empty period instead of the year 1;
  a missing date has no previous year.

### Breaking changes

- `ReportDesigner.Visual` is two-way, like `DashboardView.Document`: setting it loads a visual (null starts a new one)
  and it receives every change of the end user; `Edit` and `Clear` are gone (#65).
- `FilterContext.Selection` became `Selections` (one per visual), with `SelectionOf`, `ClearSelection(visualId)` and
  `DescribeSelections`; `CrossFilter` holds `Elements` (`SelectedElement`), its `Values` and filter-based constructor are
  gone and `Filters` is computed (#41).
- The `MarkType` enum became `VisualType`, a type identified by its `Id`, so that applications can add their own;
  EasyBI's are static properties (`VisualType.Bar`...) listed in `VisualType.BuiltIn`, and code that switched on them
  compares them instead (`type == VisualType.Bar`). Renamed with it: `VisualDefinition.Mark` → `Type`,
  `MeasureRef.Mark` and `ChartSeries.Mark` → `SeriesType`, `RendererCapabilities.Marks` → `Types`,
  `VisualPipeline.SeriesMark` and `MarksOf` → `SeriesType` and `TypesOf`, and in the designer `SelectedMark`,
  `MarkOptions`, `MarkGroups`, `SelectedMarkOption` → `SelectedType`, `TypeOptions`, `TypeGroups`, `SelectedTypeOption`,
  `MarkOptionViewModel` → `VisualTypeOptionViewModel`, `VisualTypeIcon.Mark` → `Type`, the `SeriesMark` chip property →
  `SeriesType`. `MarkGroup` became `VisualTypeGroup` and moved with `FormatTarget` to `EasyBI.Abstractions` (#59).
- **Report JSON**: a visual's `mark` became `type` and a measure's `mark` became `seriesType`. The schema stays
  `easybi/v1` while EasyBI is in beta: in a report saved by an earlier version, rename these properties, otherwise its
  visuals open as bars (#59).
- `ChartLegend` moved from `EasyBI.Core` to `EasyBI.Abstractions` and gained `Auto`, `Top` and `Left`;
  `ChartFitting.BottomLegendHeight` was removed (#48).
- `ModelField.IsEditable` is true for every measure (the model's ones can be redefined); use `IsReportMeasure` for the
  report's own measures. `MeasureContext.Set` matches a measure by model **and** name (#54).
- A formula using a measure that the report redefines now reads the report's version, the model's `Formula` measures
  included (#56).
- `FieldDescriptor` gained an optional `Formula` positional parameter: source compatible, recompile (#54).

### Report format

`easybi/v1`, backward compatible: `AnyOf` filters with `alternatives`; comparison operators (`GreaterThan`, `Range`...);
the `Matrix`, `Donut`, `Gauge` and `Waterfall` marks with an optional `gauge`; `format` on a measure; `showOnChart` on a
rule; `tooltip`, `legendPosition` and `labels` on a visual. A redefinition is a `measures` entry named like the
model's measure. Formulas may use functions and other measures.

## 0.4.0-beta.1 — 2026-10-21

### Added

- **Modernized designer**: a properties panel on the right for the selected field, filter or visual (extensible through
  `DesignerViewModel.PropertiesBuilding`), chips summed up by badges, a visual type gallery, wells that follow the visual
  type, undo / redo and keyboard shortcuts for the chips.
- **Customizable components**: slots to add, replace or hide parts of the designer, filter bar, field list, filter
  editor and dashboard; the five components are lookless controls with replaceable templates.
- **Renderer options**: renderers declare their own options, edited in the designer and saved with the visual;
  LiveCharts2 charts can scroll.
- **Designer**: sort and Top N, search in the field list and in a filter's values, a message explaining why a field
  is refused.
- **Dashboard**: item selection with the mouse and the keyboard, copy and paste with the layout (also between
  dashboards), moving and extending items with the keyboard, fewer columns on a narrow dashboard, only the visuals in
  or near view are queried.

### Breaking changes

- `ReportDesigner`, `FieldList`, `FilterBar`, `FilterEditor` and `DashboardView` derive from `Control` instead of
  `UserControl`: code relying on their former named elements uses the new properties (`ReportDesigner.ViewModel`,
  `FieldList.Dimensions`...) and routed commands (`FilterBar.AddFilterCommand`...).
- `ReportDesigner` has a new default template: the `ReportDesignerSlots.Sort` and `RendererOptions` slots are replaced
  by `Properties`; chip settings moved to the properties panel.
- `DesignerViewModel.CanDrop` refuses fields on a well the visual type does not use.

### Report format

`easybi/v1` is unchanged: renderer settings live in the existing `extensions` of a visual. v0.3 files load as they are.

## 0.3.0-beta.1 — 2026-10-19

### Added

- **Calculated measures**: `Ratio(...)` / `Calculated(g => ...)` for developers, and formulas end users write from the
  field list (`[Margin] / [Revenue]`), saved with the report.
- **Period filters**: between two dates, or relative to today (this month, last 12 months, last year), in the designer
  and the filter bar.
- **Comparisons over time**: previous year, change, % change, year to date per measure; KPI trends (▲ / ▼).
- **Data refresh and performance**: `EasyBIContext.RefreshData(model)`; leaner SQL `GROUP BY` (1.3× to 1.7× faster on
  SQLite with 5 million rows).

### Breaking changes

- `VisualPipeline.PrepareAsync` has a new optional `measures` parameter **before** the cancellation token: pass the
  token by name (`cancellationToken: token`).
- `FieldDescriptor`, `AnalyticQuery` and `ModelField` gained optional positional parameters: source compatible,
  recompile code built against v0.2.

### Report format

`easybi/v1` stays backward compatible: new optional `measures` (report) and `time` (measure) properties, new filter
operators `Between`, `Current`, `Last`, `Previous`. Files using them cannot be opened by v0.2.

## 0.2.0-beta.1 — 2026-10-19

### Added

- **Dashboard** built from a panel, a layout API and behaviors (#10), laid out with greedy items and no gaps (#14);
  visuals built by dragging fields onto it (#15).
- **Combined charts**: a chart type per measure, with a secondary value axis (#11).
- **Drag and drop** infrastructure, reusable by applications (#13).
- **Themes**: EasyBI light and dark, following WPF UI and Material Design (#17).

### Fixed

- Small LiveCharts2 and OxyPlot charts stay readable (#12).

### Breaking changes

- Dashboards are laid out with greedy items in rows and columns instead of a grid with gaps (#14).

## 0.1.0-beta.1 — 2026-10-16

First published version.

- Semantic models over `IQueryable`, query engine pushing filters, grouping, aggregates, sort and top to the provider.
- Designer, visuals, LiveCharts2 and OxyPlot connectors.
- Global filters and cross-filtering (#2).
- English and French, with runtime language switching (#4).
- NuGet packages published with Trusted Publishing (#3, #5).
