# EasyBI

A **self-service BI library for WPF**. The developer declares *what can be analyzed*, end users build their visuals
by drag-and-drop, and rendering is delegated to interchangeable **connectors** (LiveCharts2, OxyPlot, ...).

```
 Source (IQueryable) ─▶ Semantic model ─▶ AnalyticQuery ─▶ ResultSet ─▶ ChartData ─▶ IVisualRenderer
                         (developer)       (end user)       (engine)     (pivot)      (connector)
```

<div class="grid cards" markdown>

- **[Getting started](getting-started.md)**

    Install the packages, declare a model and show your first designer.

- **[Guide](guide/designer.md)**

    The designer, calculated measures, filters and cross-filtering.

- **[Visuals](visuals/matrix.md)**

    Matrix, combined charts, conditional formatting, tooltips, legends...

- **[Dashboard](dashboard/index.md)**

    Arrange visuals, let end users build them by dragging fields.

- **[Customization](customization/theming.md)**

    Themes, slots, localization and your own chart connector.

- **[Changelog](changelog.md)**

    What changed in each version, and the breaking changes.

</div>

## Projects

| Project | Role | Dependencies |
|---|---|---|
| `EasyBI.Abstractions` | **The contract**: model, query, `ResultSet`, `VisualDefinition`, `ReportDefinition` | none |
| `EasyBI.Core` | Model builder, query engine over `IQueryable` (expression trees), pivot, filtering, JSON | Abstractions |
| `EasyBI.Wpf` | `IVisualRenderer`, `EasyBIContext`, `VisualView`, `ReportDesigner`, `FilterBar`, dashboard (`DashboardPanel`, `DashboardView`, behaviors), built-in KPI, Table, Matrix, Gauge, Text and Image renderers | Core + Microsoft.Xaml.Behaviors.Wpf |
| `EasyBI.Renderers.LiveCharts2` | LiveCharts2 connector (bars, stacked bars, lines, areas, points, combined charts, pies, donuts, waterfalls) | Wpf + LiveCharts2 |
| `EasyBI.Renderers.OxyPlot` | OxyPlot connector (the same chart types) | Wpf + OxyPlot |
| `EasyBI.Themes.MaterialDesign` | EasyBI follows a Material Design in XAML theme | Wpf + MaterialDesignThemes |
| `EasyBI.Themes.WpfUi` | EasyBI follows a WPF UI (lepoco) theme | Wpf + WPF-UI |

## Versions

This site documents every published version: pick one in the selector at the top of the page. **dev** is the
development branch, not yet released. The versions published before this site show the README of their time.
