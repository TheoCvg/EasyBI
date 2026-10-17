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
| `EasyBI.Wpf` | `IVisualRenderer`, `EasyBIContext`, `VisualView`, `ReportDesigner`, `FilterBar`, dashboard (`DashboardPanel`, `DashboardView`, behaviors), built-in KPI, Table, Text and Image renderers | Core + Microsoft.Xaml.Behaviors.Wpf |
| `EasyBI.Renderers.LiveCharts2` | LiveCharts2 connector (bars, stacked bars, lines, areas, points, combined charts, pies) | Wpf + LiveCharts2 |
| `EasyBI.Renderers.OxyPlot` | OxyPlot connector (bars, stacked bars, lines, areas, points, combined charts, pies) | Wpf + OxyPlot |
| `samples/EasyBI.Sample` | Demo: dashboard + designer + renderer switch + JSON save/load | |
| `tests/EasyBI.Core.Tests` | Engine, filtering and serialization tests | |
| `tests/EasyBI.Wpf.Tests` | Designer and dashboard tests | |

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

### Combined charts

The cartesian chart types (`Bar`, `StackedBar`, `Line`, `Area`, `Scatter`) can be mixed in one chart, **one per
measure**: `MeasureRef.Mark` overrides the visual's type for that measure's series (with a legend, every series of
the measure keeps its type), and `MeasureRef.SecondaryAxis` draws it against a second value axis on the right. In
the designer, each measure of a chart has a type selector and a "Y2" toggle. These settings only change the drawing:
the query is the same, so changing them does not query the source again.

```csharp
new VisualDefinition
{
    Model = "sales",
    Mark = MarkType.Bar,                                      // default type of the measures
    Axis = [new("Date", DateGrain.Quarter)],
    Values =
    [
        new("Amount"),                                        // bars
        new("Margin") { Mark = MarkType.Line },               // line
        new("Count") { Mark = MarkType.Scatter, SecondaryAxis = true }, // points, right axis
    ],
};
```

Besides charts, KPIs and tables, a visual can be a **text** (`MarkType.Text`, content in
`VisualDefinition.Text`) or an **image** (`MarkType.Image`, embedded as base64 in `VisualDefinition.Image`).
They query no model and are drawn by the built-in `TextRenderer` and `ImageRenderer`.

## Dashboard

Arranging visuals is a WPF concern: the report (`easybi/v1`) never contains a layout. The dashboard is
built in three layers, each usable on its own.

**`DashboardPanel`** is a `Panel`, like a `Canvas` or a `Grid`: a stack of bands, each with a mode, and
children placed by attached properties.

```xml
<bi:DashboardPanel Columns="12" RowHeight="110">
    <bi:DashboardPanel.Bands>
        <bi:DashboardBand Mode="Fit" />    <!-- 12-column grid, items span rows and columns -->
        <bi:DashboardBand Mode="Scroll" /> <!-- fixed-width items, horizontal scrolling -->
        <bi:DashboardBand Mode="Wrap" />   <!-- fixed-width items, wrapping -->
    </bi:DashboardPanel.Bands>
    <Border bi:DashboardPanel.Band="0" bi:DashboardPanel.ColumnSpan="8" bi:DashboardPanel.RowSpan="2" />
    <Border bi:DashboardPanel.Band="1" bi:DashboardPanel.ItemWidth="360" />
    <Border bi:DashboardPanel.Band="0" bi:DashboardPanel.IsFullRow="True" />
</bi:DashboardPanel>
```

**`DashboardLayout`** is the static class that moves children while keeping the layout consistent (no
overlap in a grid band, items pushed down then floated up, emptied bands removed): `Move`, `MoveTo`,
`MoveNextTo`, `Resize`, `ResizeWidth`, `SetFullRow`, `AddNextTo`, `SplitFullRow`, `Remove`, `InsertBand`,
`RemoveBand`, `SetBandMode`, plus `Update` for custom operations, `Capture` (snapshot to restore) and
`GetLayout` / `SetLayout` (to save and restore). The rules themselves are pure functions on records in
`DashboardLayoutEngine`, usable without any UI.

`AddNextTo` and `MoveNextTo` place an item next to another one (`ItemSide.Right`, `Left`, `Below`, `Above`). In a
grid band, on the right or left the item joins the anchor's line: the line's items move aside, and share the columns
equally when the line is too narrow (`[A][B]` + X between them gives `[A][X][B]` on 4 columns each); below or above,
it takes the anchor's columns and what is under it moves down. A full-row anchor has no sides. `DashboardPanel.EdgeAt`
finds the item edge under a point (`SideAt` for the rule).

**`DashboardView`** is the ready-made `UserControl`: `DashboardItem`s showing visuals in a
`DashboardPanel`, with every interaction provided by a **behavior** (Microsoft.Xaml.Behaviors):

| Behavior | Feature |
|---|---|
| `AddButtonsBehavior` | "+" on the right and bottom edges of an item, "+" between bands |
| `MoveBehavior` | grip to drag an item: a picture follows the pointer and a placeholder shows where it lands, the others making room live; near an item's edge it goes next to it (like "+"), elsewhere on the cell under it; across bands, auto-scroll near the edges; Escape cancels |
| `ResizeBehavior` | corner handle: cells in a grid band, pixels and rows elsewhere |
| `ItemToolbarBehavior` | full row, edit, remove; text size, bold, alignment; image fit |
| `BandMenuBehavior` | ⋮ menu of a band: Fit / Scroll / Wrap, insert, remove (`MenuOpening` to customize it) |
| `BandScrollBehavior` | scroll bar and Shift+wheel for scrolling bands |
| `TextEditBehavior` | double-click a text to edit it in place |
| `UndoRedoBehavior` | Ctrl+Z / Ctrl+Y (`ApplicationCommands.Undo` / `Redo`) |

```xml
<bi:DashboardView x:Name="Dashboard" Context="{Binding Context}" Document="{Binding Dashboard}">
    <i:Interaction.Behaviors>
        <!-- Declaring a behavior replaces its default; set UseDefaultBehaviors="False" to pick them all -->
        <bi:UndoRedoBehavior MaxHistory="100" />
    </i:Interaction.Behaviors>
</bi:DashboardView>
```

Hooks for applications:

- events `EditVisualRequested` (replace the built-in designer dialog), `ImageRequested` (replace the file
  dialog), `FullRowSplitRequested` (ask yourself before giving up a full row; by default the item shows a
  banner), `TextEditRequested`, `ItemRemoving` (veto), `Changed` (every change, with the documents before
  and after), `DocumentLoaded`;
- `DashboardCommands` (`AddVisual`, `AddText`, `AddImage`, `EditItem`, `RemoveItem`, `ToggleFullRow`,
  `AddRight`, `AddBelow`, `InsertBand`, `RemoveBand`...) for custom templates, and public methods
  (`AddNextTo`, `SetVisual`, `InsertBand`, `BeginEdit` to group changes into one undo step...);
- an implicit style for `DashboardItem` (lookless control) and the `DashboardOverlay` style keys
  (`PlusButtonStyleKey`, `IconButtonStyleKey`, `GripStyleKey`, `ResizeGripStyleKey`, `DropPlaceholderStyleKey`,
  `DragGhostStyleKey`) to restyle;
- `DashboardBehavior` as a base class for new interactions: it gives access to the view, the panel and the
  overlay where behaviors draw their controls; `DragGesture` handles press-drag-release gestures (with
  auto-scroll: `AutoScroll`, `ScrollsBands`).

`IsReadOnly="True"` hides the editing chrome; clicks keep cross-filtering in every mode.

### Drag and drop

The designer's drag and drop is built on reusable attached properties (namespace `EasyBI.Wpf.DragAndDrop`,
XAML prefix `bi:`), usable on any `ItemsControl` of an application. Being attached properties, they can be set
from a style:

```xml
<!-- Items can be dragged: a preview follows the pointer, a moved item is dimmed -->
<ListBox ItemsSource="{Binding Fields}" bi:DragSource.IsEnabled="True" bi:DragSource.AllowedEffects="Copy" />

<!-- Drops are decided by an IDropHandler (usually the view-model); an insertion line shows where the item goes -->
<ItemsControl ItemsSource="{Binding Columns}" bi:DropTarget.Handler="{Binding}" bi:DropTarget.Key="Columns" />
```

```csharp
public sealed class MyViewModel : IDropHandler
{
    public DragDropEffects DragOver(DropInfo drop) => drop.Item is Field ? DragDropEffects.Copy : DragDropEffects.None;
    public void Drop(DropInfo drop) => Columns.Insert(drop.Index, ((Field)drop.Item!).ToColumn());
}
```

- `DropInfo` gives the dragged `Item`, the target's `Key` (one handler for several targets), the insertion
  `Index` among the target's items and the raw `Data` for drags coming from elsewhere.
- `DropTarget.IsDragOver` is true while an accepted drag is over the target (style triggers);
  `DropTarget.Orientation` switches the insertion rules to a vertical stack.
- A scrollable target, or its scroll viewer, scrolls when the pointer nears its edges (`AutoScroller`, also usable
  on its own); `InsertionIndex` holds the pure insertion rules.
- Style keys `DragSource.PreviewStyleKey` (a `ContentControl`; `DragSource.PreviewTemplate` sets the content) and
  `DropTarget.InsertionLineStyleKey` (a `Border`).

In the designer, fields are dropped between chips, chips are reordered within a shelf, and dimensions move between
Axis and Legend keeping their granularity.

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

Each `ChartSeries` comes with its resolved `Mark` and `SecondaryAxis`: a connector draws every series with its own
type, so one chart can mix bars, lines, areas and points.

`EasyBIContext.ResolveRenderer` negotiates capabilities: the active renderer if it supports every type the
visual uses (the visual's and its measures'), otherwise another plugged-in renderer, otherwise a built-in one. The designer disables
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
      "values": [ { "measure": "Amount" }, { "measure": "Count", "mark": "Line", "secondaryAxis": true } ],
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

Text and image visuals carry their content and no model:

```json
{ "mark": "Text", "text": { "text": "Sales dashboard", "fontSize": 24, "isBold": true, "alignment": "Left" } }
{ "mark": "Image", "image": { "data": "iVBORw0KGgo...", "mediaType": "image/png", "fit": "Contain" } }
```

### Dashboard file (`easybi-dashboard/v1`)

`DashboardSerializer` saves what a `DashboardView` shows: the unchanged report next to the WPF layout,
which references visuals by id. It also opens a plain `easybi/v1` report (visuals then get a default layout).

```json
{
  "schema": "easybi-dashboard/v1",
  "report": { "schema": "easybi/v1", "visuals": [ ... ], "filters": [ ... ] },
  "layout": {
    "columns": 12,
    "rowHeight": 110,
    "bands": [
      { "mode": "Fit", "items": [ { "visualId": "...", "column": 0, "row": 0, "columnSpan": 8, "rowSpan": 2, "isFullRow": false } ] },
      { "mode": "Scroll", "items": [ { "visualId": "...", "width": 360, "rowSpan": 3 } ] }
    ]
  }
}
```

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
- No dark theme.
- Dashboard images are embedded in the file (2 MB by default, `DashboardView.MaxImageBytes`).
