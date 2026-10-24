# EasyBI

[![CI](https://github.com/TheoCvg/EasyBI/actions/workflows/ci.yml/badge.svg)](https://github.com/TheoCvg/EasyBI/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/EasyBI.Wpf.svg)](https://www.nuget.org/packages/EasyBI.Wpf)
[![Documentation](https://img.shields.io/badge/docs-theocvg.github.io%2FEasyBI-blue)](https://theocvg.github.io/EasyBI/)

A **self-service BI library for WPF**. The developer declares *what can be analyzed*, end users build their visuals
and dashboards by drag-and-drop, and rendering is delegated to interchangeable **connectors** (LiveCharts2, OxyPlot).

```
 Source (IQueryable) ─▶ Semantic model ─▶ AnalyticQuery ─▶ ResultSet ─▶ ChartData ─▶ IVisualRenderer
                         (developer)       (end user)       (engine)     (pivot)      (connector)
```

**[Read the documentation](https://theocvg.github.io/EasyBI/)**

## Features

- **Designer**: fields dragged onto wells, a gallery of visual types (bars, lines, pies, donuts, waterfalls, KPIs,
  gauges, tables, matrices), a properties panel, undo / redo.
- **Dashboards** that end users arrange and fill by dragging fields, with global filters and cross-filtering.
- **Calculated measures** written by end users (`SUM([Revenue]; [Region] = "North") / [Revenue]`), with a formula
  editor that suggests what can be written.
- **Comparisons over time**, conditional formatting, custom tooltips, legends, value labels.
- **One query per visual**, pushed down to the provider: with EF Core, grouping, aggregates, sort and top run in SQL.
- **Themes**: light and dark, following WPF UI or Material Design; English and French, switched at runtime.
- **Open**: lookless controls with slots, your own chart connector, a JSON report format.

## Get started

```
dotnet add package EasyBI.Wpf
dotnet add package EasyBI.Renderers.LiveCharts2   # and/or EasyBI.Renderers.OxyPlot
```

Declare what can be analyzed, then show the designer:

```csharp
var model = SemanticModel.For(dbContext.Sales, "sales", "Sales")
    .Dimension(s => s.Date, "Date")
    .Dimension(s => s.Region, "Region")
    .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
    .Count("Number of sales");

Context = EasyBIContext.Create()
    .AddModel(model)
    .UseRenderer(new LiveCharts2Renderer());
```

```xml
<Window xmlns:bi="https://github.com/TheoCvg/EasyBI">
    <bi:ReportDesigner Context="{Binding Context}" />
</Window>
```

Then follow the [getting started guide](https://theocvg.github.io/EasyBI/latest/getting-started/), or run the demo:

```
dotnet run --project samples/EasyBI.Sample
```

## Packages

| Package | Role |
|---|---|
| `EasyBI.Wpf` | The designer, visuals, dashboard and filter bar (brings `EasyBI.Core` and `EasyBI.Abstractions`) |
| `EasyBI.Renderers.LiveCharts2` | Charts drawn with LiveCharts2 |
| `EasyBI.Renderers.OxyPlot` | Charts drawn with OxyPlot |
| `EasyBI.Themes.WpfUi` | EasyBI follows a WPF UI theme |
| `EasyBI.Themes.MaterialDesign` | EasyBI follows a Material Design in XAML theme |

## Contributing

See [AGENTS.md](https://github.com/TheoCvg/EasyBI/blob/develop/AGENTS.md) for the code style, language rules and Git
workflow, and the [changelog](https://theocvg.github.io/EasyBI/latest/changelog/) for what changed in each version.
The documentation lives in the [`docs`](https://github.com/TheoCvg/EasyBI/tree/develop/docs) folder.

## License

[MIT](https://github.com/TheoCvg/EasyBI/blob/develop/LICENSE)
