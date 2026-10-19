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
| `EasyBI.Themes.MaterialDesign` | EasyBI follows a Material Design in XAML theme | Wpf + MaterialDesignThemes |
| `EasyBI.Themes.WpfUi` | EasyBI follows a WPF UI (lepoco) theme | Wpf + WPF-UI |
| `samples/EasyBI.Sample` | Demo: dashboard + designer + renderer switch + JSON save/load | |
| `tests/EasyBI.Core.Tests` | Engine, filtering and serialization tests | |
| `tests/EasyBI.Wpf.Tests` | Designer and dashboard tests | |

## Installation

```
dotnet add package EasyBI.Wpf
dotnet add package EasyBI.Renderers.LiveCharts2   # and/or EasyBI.Renderers.OxyPlot
dotnet add package EasyBI.Themes.WpfUi            # optional: if the application uses WPF UI or Material Design
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

**The layout** (`DashboardLayoutDefinition`) is a stack of bands, each with a mode. Its items are **greedy**:
without a constraint, they share the room their neighbours leave, so a dashboard never has a gap.

- **Fit** bands are a grid of 12 columns that fills the width. A band is a stack of lines; a line is an item, or a
  container (`LayoutNode.Row`: side by side, `LayoutNode.Column`: stacked) of items and containers. Along a
  container, constrained children keep their size (`ColumnSpan` in columns, `RowSpan` in rows) and the others share
  the rest; across it, every child takes the whole room. An item with a `RowSpan` sets the height of its line and the
  items next to it stretch, so items stacked beside a tall item share its height.
- **Wrap** bands start their items at `DashboardPanel.MinItemWidth` (or their fixed `Width` in pixels), break lines
  when the next one does not fit, then share what is left on each line. **Scroll** bands do the same on one line,
  and scroll horizontally when the items do not fit.
- A container whose children are all constrained leaves a **free space**, which the dashboard offers to fill.

```csharp
new DashboardLayoutDefinition
{
    Bands =
    [
        new BandLayout
        {
            Items =
            [
                new LayoutNode { VisualId = title.Id, RowSpan = 1 },                                // a whole line
                LayoutNode.Row(
                    new LayoutNode { VisualId = map.Id, ColumnSpan = 6, RowSpan = 6 },               // left half, 2 lines high
                    LayoutNode.Column(LayoutNode.Item(sales.Id), LayoutNode.Item(margin.Id))),       // stacked beside it
            ],
        },
        new BandLayout { Mode = BandMode.Scroll, Items = [LayoutNode.Item(a.Id), new LayoutNode { VisualId = b.Id, Width = 360 }] },
    ],
};
```

**`DashboardPanel`** shows a layout (`Layout` property); children are matched to its items by the attached
`DashboardPanel.ItemId`. The geometry itself is a pure function (`BandArrangement.Arrange`), and the panel exposes
hit tests for interactions (`EdgeAt`, `FlowIndexAt`, `FreeSpaces`, and `CaptureGeometry` to hit-test the layout from
before a drag).

**`DashboardLayout`** is the static class that changes a panel's layout while keeping it consistent: `AddNextTo`,
`MoveNextTo` (`ItemSide.Right`, `Left`, `Below`, `Above`), `InsertAt` / `MoveTo` (a new line), `Resize`,
`ResizeWidth`, `SetAuto`, `Remove`, `InsertBand`, `RemoveBand`, `SetBandMode`, plus `Update` for custom
operations, `Capture` (snapshot to restore) and `GetLayout` / `SetLayout` (to save and restore). Next to an item, a
side along its container joins it (the line's columns are shared when it has no room left), a side across it
wraps the item and the newcomer in a new container. The rules are pure functions on records in
`DashboardLayoutEngine`, usable without any UI.

**`DashboardView`** is the ready-made `UserControl`: `DashboardItem`s showing visuals in a
`DashboardPanel`, with every interaction provided by a **behavior** (Microsoft.Xaml.Behaviors):

| Behavior | Feature |
|---|---|
| `AddButtonsBehavior` | "+" on the right and bottom edges of an item, "+" between lines and between bands |
| `FreeSpaceBehavior` | fills free spaces with a placeholder that adds an item there |
| `FieldDropBehavior` | makes visuals from fields dragged from a `FieldList` (see below) |
| `MoveBehavior` | grip to drag an item: a picture follows the pointer and a placeholder shows where it lands, the others making room live; near an item's edge it goes next to it (like "+"), between two lines it becomes a line, over a free space it fills it; across bands, auto-scroll near the edges; Escape cancels |
| `ResizeBehavior` | corner handle: constrains the item (columns and rows in a grid band, pixels and rows elsewhere) |
| `ItemToolbarBehavior` | automatic size (for a resized item), edit, remove; text size, bold, alignment; image fit |
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
  dialog), `TextEditRequested`, `ItemRemoving` (veto), `Changed` (every change, with the documents before
  and after), `DocumentLoaded`;
- `DashboardCommands` (`AddVisual`, `AddText`, `AddImage`, `EditItem`, `RemoveItem`, `ResetSize`,
  `AddRight`, `AddBelow`, `InsertBand`, `RemoveBand`...) for custom templates, and public methods
  (`AddNextTo`, `AddIn` a free space, `InsertLine`, `SetAutoSize`, `SetVisual`, `InsertBand`, `BeginEdit` to group
  changes into one undo step...);
- an implicit style for `DashboardItem` (lookless control) and the `DashboardOverlay` style keys
  (`PlusButtonStyleKey`, `IconButtonStyleKey`, `GripStyleKey`, `ResizeGripStyleKey`, `FreeSpaceStyleKey`,
  `DropPlaceholderStyleKey`, `DragGhostStyleKey`) to restyle;
- `DashboardBehavior` as a base class for new interactions: it gives access to the view, the panel and the
  overlay where behaviors draw their controls; `DragGesture` handles press-drag-release gestures (with
  auto-scroll: `AutoScroll`, `ScrollsBands`).

`IsReadOnly="True"` hides the editing chrome; clicks keep cross-filtering in every mode.

**Building visuals by dragging fields.** `FieldList` shows the fields of the context's models (model picker,
dimensions, measures) and drags them as `ModelField`s. Placed next to a dashboard, it lets users build it directly:

```xml
<DockPanel>
    <bi:FieldList DockPanel.Dock="Left" Width="210" Context="{Binding Context}" />
    <bi:DashboardView Context="{Binding Context}" />
</DockPanel>
```

- dropped where an item can go (an edge, between two lines, a free space, a scrolling or wrapping band), a field
  makes a new visual: a KPI for a measure, the first measure by that dimension for a dimension (a line by month for a
  date, bars otherwise); the other items make room live around a placeholder;
- dropped on an empty item, it fills it; dropped on a visual, it is added to it (a measure to its values, a dimension
  to its axis, then its legend; a dimension breaks a KPI down into bars);
- the rules are pure functions in `FieldVisuals` (`Create`, `AddField`); `FieldDropBehavior.CreateVisual` and
  `AddField` replace them. Each drop is one undo step.

The designer's field panel is the same `FieldList` (its shelves accept the `ModelField`s of the selected model).
`DashboardDropTarget.Find`, the place an item dropped at a point goes, is shared by moves and field drops.

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

## Theming

Every EasyBI control (designer, dashboard, filter bar, field list, KPI and table renderers, chart connectors) draws
with **resources looked up dynamically**: an application restyles EasyBI by defining them, and switches theme at runtime
by replacing the dictionary that defines them. Charts follow too, without querying their data again.

### Light and dark

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <bi:EasyBIThemeDictionary Mode="Dark" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

Without it, the light theme applies. To switch at runtime, change `Mode` (or swap the dictionary):

```csharp
foreach (var theme in Application.Current.Resources.MergedDictionaries.OfType<EasyBIThemeDictionary>())
    theme.Mode = EasyBIThemeMode.Light;
```

### Overriding a few colors

Define the keys you want in your resources, after the theme dictionary; the others keep the theme's values:

```xml
<SolidColorBrush x:Key="{x:Static bi:EasyBITheme.AccentBrushKey}" Color="#7C3AED" />
<bi:ChartPalette x:Key="{x:Static bi:EasyBITheme.ChartPaletteKey}">
    <Color>#7C3AED</Color>
    <Color>#F59E0B</Color>
    <Color>#10B981</Color>
</bi:ChartPalette>
<CornerRadius x:Key="{x:Static bi:EasyBITheme.CornerRadiusKey}">4</CornerRadius>
```

The complete list, with their light and dark values, is in
[`Themes/Light.xaml`](src/EasyBI.Wpf/Themes/Light.xaml) and [`Themes/Dark.xaml`](src/EasyBI.Wpf/Themes/Dark.xaml):

| Key (`EasyBITheme.…`) | Used for |
|---|---|
| `BackgroundBrushKey` | behind the dashboard, the designer and the visual editor |
| `SurfaceBrushKey` | cards (dashboard items, designer preview), popups, toolbars, chart tooltips |
| `SurfaceAltBrushKey` | field lists, designer shelves, empty items, alternate table rows |
| `BorderBrushKey`, `BorderStrongBrushKey` | borders and separators; empty items and free spaces |
| `ForegroundBrushKey`, `MutedForegroundBrushKey`, `DisabledForegroundBrushKey` | text, secondary text (headers, hints, icons, axis labels), unavailable options |
| `AccentBrushKey`, `AccentHoverBrushKey`, `AccentForegroundBrushKey` | selection, "+" buttons, drop indicators, and the text on them |
| `AccentSoftBrushKey`, `AccentSoftBorderBrushKey` | field chips, selection chip, drop placeholders |
| `SecondarySoftBrushKey`, `SecondarySoftBorderBrushKey` | filter chips |
| `HoverBrushKey` | small buttons under the pointer |
| `ErrorBrushKey` | error messages |
| `MeasureBrushKey` | the Σ of measures in field lists |
| `GridLineBrushKey` | grid lines of charts and tables |
| `ChartPaletteKey` (`ChartPalette`) | colors of chart series, in order |
| `CornerRadiusKey` (`CornerRadius`) | rounding of cards |

Control-specific parts have their own style keys (`DashboardOverlay.PlusButtonStyleKey`, `FreeSpaceStyleKey`,
`DropPlaceholderStyleKey`, `DragSource.PreviewStyleKey`... see [Dashboard](#dashboard) and
[Drag and drop](#drag-and-drop)), and `DashboardItem` has an implicit style.

### Standard controls

Buttons, combo boxes, text boxes and check boxes inside EasyBI have **no explicit style**: they take your application's
implicit styles, so a control library (WPF UI, Material Design...) styles them too. EasyBI's own light and dark themes
color EasyBI's surfaces, text and charts, not the standard controls: with the dark theme and no control library, they
keep the system's light look.

### Following a control library

With WPF UI or Material Design in XAML, install the matching package and merge its dictionary **after** the library's:
EasyBI's surfaces, text, accent and charts then take the library's colors, light or dark, and follow its theme switches
(`ApplicationThemeManager.Apply`, `PaletteHelper.SetTheme`). Its classes are in the `bi:` XAML namespace.

```xml
<!-- dotnet add package EasyBI.Themes.WpfUi -->
<ResourceDictionary.MergedDictionaries>
    <ui:ThemesDictionary Theme="Dark" />
    <ui:ControlsDictionary />
    <bi:WpfUiTheme />
</ResourceDictionary.MergedDictionaries>

<!-- dotnet add package EasyBI.Themes.MaterialDesign -->
<ResourceDictionary.MergedDictionaries>
    <md:BundledTheme BaseTheme="Dark" PrimaryColor="DeepPurple" SecondaryColor="Lime" />
    <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml" />
    <bi:MaterialDesignTheme />
</ResourceDictionary.MergedDictionaries>
```

`WpfUiTheme.Map` and `MaterialDesignTheme.Map` list which library resource each EasyBI key takes. For another library
or an application's own design system, derive from `EasyBIThemeMapping` and map the keys you need; unmapped keys keep
EasyBI's light values, and the chart palette follows the mapped background (light or dark palette). If the library
switches theme by swapping dictionaries without notifying, call `Refresh()` on the mapping afterwards.

`ApplicationThemeManager.Apply` sets the main window's `Background` itself: put an EasyBI background on the window's root
panel rather than on the window (as the sample does).

```csharp
public sealed class MyTheme() : EasyBIThemeMapping(new Dictionary<ComponentResourceKey, object>
{
    [EasyBITheme.BackgroundBrushKey] = "MyWindowBrush",
    [EasyBITheme.AccentBrushKey] = "MyPrimaryBrush",
});
```

### Charts

`VisualView` passes the theme to renderers in `RenderRequest.Theme` (`BiTheme`): the palette, and the `Foreground`,
`MutedForeground`, `GridLine` and `Background` colors (hexadecimal, null when unknown). It is built by
`EasyBITheme.ChartTheme(element)` from the resources above. A palette or colors set on `EasyBIContext.Theme` take
precedence over the resources.

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

Colors come from `request.Theme`: series take `ColorAt(index)`, text `Foreground`, axis labels `MutedForeground`, grid
lines `GridLine`, tooltips `Background` (each one null when the theme does not set it: keep the library's default).
Draw on a transparent background, so that the chart sits on the theme's surface.

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
which references visuals by id. Sizes that are not saved are greedy. It also opens a plain `easybi/v1` report
(visuals then get a default layout).

```json
{
  "schema": "easybi-dashboard/v1",
  "report": { "schema": "easybi/v1", "visuals": [ ... ], "filters": [ ... ] },
  "layout": {
    "columns": 12,
    "rowHeight": 110,
    "bands": [
      {
        "mode": "Fit",
        "items": [
          { "visualId": "...", "rowSpan": 1 },
          { "split": "Row", "children": [ { "visualId": "...", "columnSpan": 6 }, { "visualId": "..." } ] }
        ]
      },
      { "mode": "Scroll", "items": [ { "visualId": "...", "width": 360 }, { "visualId": "..." } ] }
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
- The built-in light and dark themes do not restyle standard controls (buttons, combo boxes...): use a control
  library for those (see [Following a control library](#following-a-control-library)).
- `EasyBI.Themes.WpfUi` is checked against WPF UI's light and dark themes; its high contrast themes are not verified.
- Dashboard images are embedded in the file (2 MB by default, `DashboardView.MaxImageBytes`).
