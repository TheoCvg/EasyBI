# Changelog

Every version, with its breaking changes. EasyBI follows [Semantic Versioning](https://semver.org/): while in `0.x`,
breaking changes bump the minor version. The report format (`easybi/v1`) stays backward compatible: a newer version
opens older reports, and a report using a newer feature cannot be opened by an older version.

## Unreleased

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

### Fixed

- LiveCharts2: the labels under groups turn like other category labels instead of being skipped (#51).
- Designer: the measure preview no longer cancels a query on every keystroke (#52).

### Breaking changes

- `FilterContext.Selection` became `Selections` (one per visual), with `SelectionOf`, `ClearSelection(visualId)` and
  `DescribeSelections`; `CrossFilter` holds `Elements` (`SelectedElement`), its `Values` and filter-based constructor are
  gone and `Filters` is computed (#41).
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
