# EasyBI

A **self-service BI library for WPF**. The developer declares *what can be analyzed*,
end users build their visuals by drag-and-drop, and rendering is delegated to interchangeable
**connectors** (LiveCharts2, OxyPlot, ...).

```
 Source (IQueryable) ─▶ Semantic model ─▶ AnalyticQuery ─▶ ResultSet ─▶ ChartData ─▶ IVisualRenderer
                         (developer)       (end user)       (engine)     (pivot)      (connector)
```

## Projects

| Project | Role | Dependencies |
|---|---|---|
| `EasyBI.Abstractions` | **The contract**: model, query, `ResultSet`, `VisualDefinition`, `ReportDefinition` | none |
| `EasyBI.Core` | Model builder, query engine over `IQueryable` (expression trees), pivot, filtering, JSON | Abstractions |
| `EasyBI.Wpf` | `IVisualRenderer`, `EasyBIContext`, `VisualView`, `ReportDesigner`, `FilterBar`, built-in KPI and Table renderers | Core |
| `EasyBI.Renderers.LiveCharts2` | LiveCharts2 connector (bars, lines, pies) | Wpf + LiveCharts2 |
| `EasyBI.Renderers.OxyPlot` | OxyPlot connector (bars, lines, pies) | Wpf + OxyPlot |
| `samples/EasyBI.Sample` | Demo: dashboard + designer + renderer switch + JSON save/load | |
| `tests/EasyBI.Core.Tests` | Engine, filtering and serialization tests | |
| `tests/EasyBI.Wpf.Tests` | Designer tests | |

## Installation

```
dotnet add package EasyBI.Wpf
dotnet add package EasyBI.Renderers.LiveCharts2   # and/or EasyBI.Renderers.OxyPlot
```

`EasyBI.Wpf` brings `EasyBI.Core` and `EasyBI.Abstractions`. Every public WPF type is available
under a single XAML namespace:

```xml
xmlns:bi="https://github.com/TheoCvg/EasyBI"
```

## For the WPF developer

```csharp
var model = SemanticModel.For(dbContext.Sales, "sales", "Sales")
    .Dimension(s => s.Date, "Date")
    .Dimension(s => s.Region, "Region")
    .Dimension(s => s.Product.Category, "Category")
    .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
    .Count("Number of sales");

Context = EasyBIContext.Create()
    .AddModel(model)
    .UseRenderer(new LiveCharts2Renderer())
    .UseRenderer(new OxyPlotRenderer());
```

With EF Core, prefer a context factory: each query opens its own `DbContext`, and the visuals of a
dashboard run in parallel. On a shared `IQueryable` (other than LINQ to objects), queries run one
after another, because a `DbContext` does not support concurrent operations.

```csharp
var model = SemanticModel.For(() => dbFactory.CreateDbContext(), db => db.Sales, "sales", "Sales")
    .Dimension(s => s.Region, "Region") /* ... */;
```

```xml
<bi:ReportDesigner Context="{Binding Context}" />                             <!-- design -->
<bi:VisualView Context="{Binding Context}" Definition="{Binding Visual}" />   <!-- display -->
```

## Filtering: global filters and cross-filtering

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
  the same model on the element's dimensions (axis, plus legend for a bar). Clicking the same element
  again clears the selection. The source visual is not filtered and shows the current selection.
  Periods (quarter, month...) filter through `FilterSpec.Grain`.

A connector only has to call `request.Select?.Invoke(new CategorySelection(category, series))` on
click (or `RowSelection` for a table): `Select` is null when interactivity is disabled, for example
in the designer preview.

## Dashboard layout

`DashboardPanel` shows a report as rows of visuals that end users arrange themselves:

- hovering the left or right edge of a visual shows a **+** that adds a slot to the row; the top or
  bottom edge, a **+** that adds a row. An empty slot offers **+ Add a visual**;
- the **⠿** grip moves a visual: drop it left or right of another visual, or on its top or bottom
  quarter to put it in a new row;
- dragging the gap between two visuals resizes them, and the gap below a row changes its height;
- each row (**⋮** menu) fits its width (default), scrolls horizontally or wraps;
- **✎** edits a visual, **✕** removes it; Ctrl+Z / Ctrl+Y undo and redo.

```xml
<bi:DashboardPanel Context="{Binding Context}" Report="{Binding Report}" />
```

The arrangement is stored in the report (`ReportDefinition.Layout`), so saving and reloading the
report restores the dashboard. Reports without a layout get a default grid. `IsLayoutEditable="False"`
shows a read-only dashboard. Editing opens the designer in a dialog; handle `EditVisualRequested`
and set `Handled` to use your own editor, then call `Complete(visual)`.

## Localization

EasyBI's own texts (designer, filter bar, messages, granularity and aggregation names, blank and
boolean values) ship in **English** and **French**. The language can change at runtime: designer,
filter bar and visuals update immediately.

```csharp
EasyBILocalization.Culture = CultureInfo.GetCultureInfo("fr-FR"); // null = follow CurrentUICulture
```

The same culture formats numbers and dates. Your semantic model captions can follow the language too:
call `Localize` with your own resources, and every caption becomes a resource key (a missing key falls
back to the caption itself).

```csharp
var model = SemanticModel.For(db.Sales, "sales", "Sales")
    .Dimension(s => s.Region, "Region")
    .Measure(s => s.Amount, "Revenue")
    .Localize(MyStrings.ResourceManager);
```

Your own XAML texts can follow it as well with `LocalizedStrings` and the `Localize` markup extension:

```csharp
public static LocalizedStrings Texts { get; } = new(MyStrings.ResourceManager);
```

```xml
<Button Content="{bi:Localize Save, Source={x:Static local:App.Texts}}" />
```

Visual and report titles are user content: they are stored as typed and not translated.

## Writing a connector

A connector implements a single interface. It receives **already queried and pivoted** data
(`request.Chart`: categories + series) and the raw `ResultSet` if needed:

```csharp
public sealed class MyRenderer : IVisualRenderer
{
    public string Name => "MyLib";
    public RendererCapabilities Capabilities { get; } = new(new HashSet<MarkType> { MarkType.Bar });
    public FrameworkElement Render(RenderRequest request) { /* request.Chart.Categories, request.Chart.Series */ }
}
```

`EasyBIContext.ResolveRenderer` negotiates capabilities: the active renderer if it supports the
visual type, otherwise another plugged-in renderer, otherwise a built-in one. The designer disables
the types that no renderer can draw.

## Report format (`easybi/v1`)

```json
{
  "schema": "easybi/v1",
  "title": "Dashboard",
  "visuals": [
    {
      "title": "Revenue by region and channel",
      "model": "sales",
      "mark": "Bar",
      "axis": [ { "field": "Region", "grain": "None" } ],
      "legend": { "field": "Channel", "grain": "None" },
      "values": [ { "measure": "Amount" } ],
      "filters": [ { "field": "Product.Category", "operator": "In", "values": [ "Services" ] } ]
    }
  ],
  "filters": [
    { "model": "sales", "filter": { "field": "Region", "operator": "In", "values": [ "North", null ] } }
  ]
}
```

`null` in `values` means a missing value. `EasyBIContext.Validate(report)` reports the unknown
models and fields of a loaded report.

The same file renders identically with any connector.

## Run

```
dotnet test
dotnet run --project samples/EasyBI.Sample
```

The demo starts in your system language (English or French) and has a language picker in its toolbar.

## Contributing

See [AGENTS.md](AGENTS.md) for the code style, language rules and Git workflow.

## Known limitations / roadmap

- Cross-filtering: one selection at a time; the selected element is not yet highlighted in the
  source chart (a "Sélection : ..." banner shows it).
- Designer and `FilterBar` filters: "one of" only, on non-date dimensions; no date ranges.
- `Top` applies to aggregated rows, not to categories when a legend is present.
- Grouping, sorting and `Top` are pushed down to the provider, and execution is asynchronous when the
  source implements `IAsyncEnumerable` (EF Core). EF Core's SQL translation (GroupBy on a composite
  key, sort on an aggregate, projection to `object[]`) still has to be validated against a real database.
- At most 4 grouping dimensions (axis + legend).
- No dark theme. The dashboard layout is row-based (no free-form positioning or spanning several rows).
