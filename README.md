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

Each visual becomes one database query (filters, grouping, aggregates, sort and top in SQL): only the aggregated
rows reach the application, whatever the number of rows in the table.

Visuals cache their last result and query again only when their query changes. When the data behind a model changes
(new rows, another source), tell them, from any thread:

```csharp
Context.RefreshData("sales");   // the visuals of this model; RefreshData() for all of them
```

```xml
<bi:ReportDesigner Context="{Binding Context}" />                             <!-- design -->
<bi:VisualView Context="{Binding Context}" Definition="{Binding Visual}" />   <!-- display -->
```

### The designer

The `ReportDesigner` has three zones: the fields on the left, the wells and the preview in the middle, the
**properties** on the right (both side panels fold: `IsFieldPanelOpen`, `IsPropertiesPanelOpen`).

- **Chips** show their field and badges summing their settings up (Σ, avg, Quarter, PY, Y2, ↓...). A click selects one
  (`DesignerViewModel.SelectedItem`) and the properties panel shows its settings in sections: aggregation, granularity,
  time calculation, chart type, secondary axis, sort for a field; the editor of a filter. Without a selection it shows
  the visual's: title, sort, Top N, and the options of the renderer.
- **The visual type** is chosen in a gallery grouped by use (comparison, trend, composition, indicators and tables);
  types no renderer draws are greyed.
- **Wells follow the type**: a pie shows "Category" and "Value", a table "Rows", a KPI only "Values"; a well the type
  does not use refuses fields and stays shown while it still holds some. Empty wells say what to drop, and while a
  field or a chip is dragged the wells that accept it light up (`DragSource.DragStarted`).
- **Undo and redo** every change of the visual (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z, the header's buttons,
  `UndoCommand`/`RedoCommand`; 100 steps, `MaxHistory`). Loading another visual starts a new history.
- **Keyboard**: arrows go from chip to chip (Up/Down from well to well), Ctrl+Left/Right move the selected chip within
  its well, Delete removes it, Escape selects the visual again.

Everything stays replaceable: the template is lookless and made of slots (see below); the properties are descriptors
(`DesignerProperty`: toggle, choice, number, text, info or any object shown with its `DataTemplate`, grouped in
`DesignerPropertySection`s), which `DesignerViewModel.PropertiesBuilding` lets an application add, remove or reorder;
each kind of editor is a `DataTemplate` under a `DesignerPropertyTemplates` key; the icons of the types are geometries
under `VisualTypeIcons` keys.

```csharp
designer.ViewModel!.PropertiesBuilding += (_, e) =>
{
    if (e.Selection is ShelfItemViewModel { IsValue: true } chip)
        e.Sections.Add(new DesignerPropertySection("Notes", () => "Notes",
            [DesignerProperty.Text("Comment", () => "Comment", () => Comments.Get(chip), text => Comments.Set(chip, text))]));
};
```

### Calculated measures

A measure is a row value that the end user aggregates (sum, average...). Ratios need more: the margin rate of a region
is its total margin divided by its total revenue, not the average of its sales' rates. Calculated measures are
computed from the rows of each group, inside the same query (in SQL with EF Core), and can be sorted and limited like
the others; the end user cannot change their aggregation.

```csharp
model
    .Ratio(s => s.Margin, s => s.Amount, "Margin rate", "MarginRate", format: "P1")      // Σ margin / Σ revenue
    .Ratio(s => s.Amount, s => 1, "Average basket", "AverageBasket", format: "#,##0 €")   // Σ revenue / number of sales
    .Calculated(g => g.Sum(s => s.Amount) - g.Sum(s => s.Cost), "Gross profit", "GrossProfit");
```

`Ratio` is empty when the denominator sums to zero. With `Calculated`, guard divisions yourself: some databases fail
on a division by zero.

End users create their own from the field list (**+ Calculated measure**, then ✎ and ✕ on hover): a name, a format
and a formula over the model's measures, typed with their captions and checked as it is typed, with its total as a
preview:

```
[Margin] / [Revenue]            ([Revenue] - [Margin]) / [Number of sales]            [Margin] * 100 / [Revenue]
```

Measures in brackets are aggregated as in the field list; the operators are `+ - * /` with parentheses and numbers
with a decimal point; a division by zero gives an empty value. The formula runs in the same query as the other
measures. These measures live in `EasyBIContext.Measures` (one context per open report): every field list, designer
and visual of the context sees them, and a `DashboardView` loads and saves them with its document (undo does not
remove them). An application that saves reports without a dashboard puts `Context.Measures.Measures` in
`ReportDefinition.Measures` and loads them back with `Context.Measures.Load(...)`.

### Comparisons over time

Each measure of a visual can be seen through time (`MeasureRef.Time`, the "Calculation" list of a measure chip in the
designer): its value, its value the previous year, the change since (in value or in %), or its total since the start of
the year along the date axis.

```csharp
Values = [new("Amount"), new("Amount") { Time = TimeCalculation.PercentChangeFromPreviousYear }],
Filters = [PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)],   // this year vs last year
```

The previous year is the same query with its dates shifted back one year (period filters, clicked periods), run once
more and matched on the groups (each month with the same month a year earlier, per series). It needs a date on the axis
or a date filter, and is empty otherwise. The year-to-date total adds the values up along the date axis and starts
again each year. A KPI shows a change with ▲ / ▼ in the measures' or the error color. This runs in
`VisualPipeline.PrepareAsync` (`ISemanticModel.ExecuteWithTimeAsync`) on top of any model.

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

### Matrix

A **matrix** (`MarkType.Matrix`) crosses its axis, the **rows** (up to four dimensions), with its legend, the
**columns**, for each measure in `Values`, and adds the total of each row, of each column and of the whole. Each total
is the measure computed on all the rows it covers (an average stays an average), not a sum of cells:
`VisualPipeline.PrepareAsync` runs one query per level (`MatrixBuilder`) and hands the pivoted data to the renderer in
`RenderRequest.Matrix` (`MatrixData`). The sort, `Top` and the filters on measures choose the rows; the column totals
then only count those rows. Without columns, a matrix lists its rows and their totals with a total row. Clicking a cell
cross-filters on its row and column, a row header or total on the row, a column total on the column
(`MatrixSelection`). The built-in `MatrixRenderer` draws it in a grid styled like the table; in the designer, the
wells of a matrix are Rows, Columns and Values.

```csharp
new VisualDefinition
{
    Model = "sales",
    Mark = MarkType.Matrix,
    Axis = [new("Product.Category")],                         // rows
    Legend = new("Date", DateGrain.Year),                     // columns
    Values = [new("Amount")],
};
```

### Conditional formatting

Each measure of a table, a matrix or a KPI can have a **format** (`MeasureRef.Format`, a `ValueFormat`), edited in the
designer's properties panel when its chip is selected ("Formatting" section). It holds:

- a **fixed style**: text color, bold, italic, alignment;
- **rules**, checked in order, the first matching one applies: a comparison (`GreaterThan`... `Range`, as in numeric
  filters) and the text color, background and weight it gives;
- a **color scale**: a background going from one color to another (optionally through a middle one) between the
  smallest and the largest value shown;
- **data bars**: a bar in the cell as long as the value compared with the largest one (negative values in their own
  color);
- **icons** (arrows, triangles, circles) for high, middle and low values, split at two thresholds or, without them,
  in thirds.

Colors are the theme's (`ThemeColors`: `accent`, `success`, `warning`, `error`, `muted`), which follow light and dark
themes and control libraries, or a custom `#RRGGBB`. As backgrounds, the theme's colors are softened with the theme's
background so that values stay readable. The scale, the bars and the automatic thresholds compare a value with the
cells shown, not with the totals; a matrix leaves its totals unformatted. A format only changes the drawing: the
source is not queried again. `ValueStyler` (Core) computes the `ValueStyle` of a value for any renderer, and the
`FormattedValue` control (lookless) draws it in the built-in grids.

**In charts**, the rules and the color scale color each bar, point and slice (LiveCharts2's *conditional draw*:
`OnPointMeasured` gives each point its paint; OxyPlot colors bars and slices one by one, not the points of lines).
Stacked bars are judged as a whole and keep their legend colors: a rule or a scale compares the total of the stack,
and frames the whole stack in its color (`ChartFormatting.StackOutlines`). The designer's editor only shows what the visual's type uses
(on a chart: the rules' color and zone, and the scale; on a KPI: no scale or bars); what it hides is kept. A
rule with **"show its zone on charts"** (`FormatRule.ShowOnChart`) shades its zone of the value axis: from its value
up for "greater than", down for "less than", between its bounds for a range, labelled with the condition
(`RectangularSection` in LiveCharts2, `RectangleAnnotation` in OxyPlot). `ChartFormatting.For(request)` (Core) gives a
connector the color of each point (`PointColor`) and the zones (`Bands`); each `ChartSeries` carries the `MeasureKey`
of its measure.

```csharp
new MeasureRef("Amount")
{
    Format = new ValueFormat
    {
        Rules = [new FormatRule { Operator = FilterOperator.LessThan, Value = 1000, Color = ThemeColors.Error, IsBold = true }],
        Bar = new DataBar(),
        Icons = new IconSet { Kind = IconKind.Triangles },
    },
};
```

A **pie** (`MarkType.Pie`) is a full disc; a **donut** (`MarkType.Donut`) is a ring with the total of its value in its
hole (`ChartCenterLabel`, which places a text in the middle of a connector's plot area).

A **gauge** (`MarkType.Gauge`) shows a single value on a half circle, drawn by the built-in `GaugeRenderer` whatever
the connector. Its scale and target are numbers of the visual (`VisualDefinition.Gauge`, a `GaugeSettings`, edited in the
designer's Visual section): `Minimum` (empty: 0), `Maximum` (empty: a round number above 0, the value and the target,
see `GaugeScale`) and `Target`, marked on the arc. The measure's format colors the value and adds its icon.

A **waterfall** (`MarkType.Waterfall`) shows how each category's value adds to the running total, up in the theme's
success color, down in its error color, and ends with a bar of the total (`Waterfall.Of` gives any connector the steps
and the chart with its "Total" category). It takes an axis and a single value; LiveCharts2 draws it with candles,
OxyPlot with interval bars. A rule of the measure's format colors the steps it matches; clicking a step cross-filters
on its category.

Besides charts, KPIs, tables and matrices, a visual can be a **text** (`MarkType.Text`, content in
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
- **Column tiers**: on a narrow panel, Fit bands show fewer columns (`DashboardPanel.ColumnTiers`; by default
  1 column under 600 px and 6 under 960 px, `ColumnTier.Defaults`). Spans are scaled to the tier, a line whose items
  would get less than 3 columns each is broken into several lines (one item per line in a single column), and
  constrained items keep their proportions while filling their line. Only the display adapts: the saved layout does
  not change, and a resize in a tier is saved in the layout's columns (`DashboardPanel.ActiveColumns`, `ToLayoutSpan`).
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

**`DashboardView`** is the ready-made (lookless) control: `DashboardItem`s showing visuals in a
`DashboardPanel`, with every interaction provided by a **behavior** (Microsoft.Xaml.Behaviors):

| Behavior | Feature |
|---|---|
| `AddButtonsBehavior` | "+" on the right and bottom edges of an item, "+" between lines and between bands |
| `FreeSpaceBehavior` | fills free spaces with a placeholder that adds an item there |
| `FieldDropBehavior` | makes visuals from fields dragged from a `FieldList` (see below) |
| `MoveBehavior` | grip to drag an item: a picture follows the pointer and a placeholder shows where it lands, the others making room live; near an item's edge it goes next to it (like "+"), between two lines it becomes a line, over a free space it fills it; across bands, auto-scroll near the edges; Escape cancels |
| `ResizeBehavior` | corner handle: constrains the item (columns and rows in a grid band, pixels and rows elsewhere) |
| `ItemToolbarBehavior` | automatic size (for a resized item), edit, remove; text size, bold, alignment; image fit |
| `SelectionBehavior` | click selects an item (Ctrl+click adds or removes it, except on a bar, slice or row of a chart, where it extends the cross-filtering: `VisualView.DataSelectedEvent`), click on the background unselects, drag from the background draws a selection box; arrows go to the nearest item (Shift adds it), Escape unselects |
| `KeyboardLayoutBehavior` | Ctrl+arrows move the selected item, Ctrl+Shift+Down/Up put it in a new band, Alt+arrows extend the selected items over the free room next to them (through the commands below) |
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
- the item selection: `SelectedItems`, `SelectionChanged`, `Select(items, DashboardSelectionMode.Replace/Add/Toggle)`,
  `SelectAll`, `UnselectAll`, `RemoveSelected` and `DuplicateSelected` (one change each), also reached through
  `ApplicationCommands.Delete` (Del), `ApplicationCommands.SelectAll` (Ctrl+A) and `DashboardCommands.Duplicate`
  (Ctrl+D); `DashboardItem.IsSelected` for templates (the default one draws a ring) and the
  `DashboardOverlay.SelectionBoxStyleKey` style. A read-only dashboard has no selection;
- lazy loading: only the visuals of the items in or near the visible part of the dashboard query their data
  (`DashboardItem.IsInViewport`, bound to `VisualView.IsActive` by the default template); the others query once they
  scroll into view, a change of filters or data giving them one query then. `ViewportMargin` sets how far ahead items
  load (one viewport by default), `LoadsVisibleItemsOnly = false` loads them all (to print or export, for instance).
  `VisualView.IsActive = false` holds back the queries of any visual;
- keyboard layout: `MoveSelected(side, toNewBand)` (`DashboardCommands.MoveItem` and `MoveItemToNewBand`, Ctrl+arrows
  and Ctrl+Shift+Down/Up; `DashboardLayoutEngine.Step`): Down takes an item
  sharing its line to a new line below; alone on its line, it joins the next line, and from the last line it goes into
  the band below (created past the last one); Up the same upwards, so that Down and Up undo each other; Ctrl+Shift+Down/Up puts it in a new band below/above; Left/Right swap it with its
  neighbour. `ExtendSelected(width)` (`DashboardCommands.ExtendItem`, Alt+arrows; `DashboardLayoutEngine.Fill`) extends the selected items over the
  free room next to them without shrinking their neighbours;
- copy and paste of the selected items, possibly between dashboards: `CopySelected` / `CutSelected` / `Paste`
  (Ctrl+C, Ctrl+X, Ctrl+V through `ApplicationCommands`; `CreateCopy` and `Paste(data)` work without the clipboard).
  The clipboard holds an `easybi/v1` report (format `VisualTransfer.ClipboardFormat`) with the visuals and the
  calculated measures they use, plus their titles as text, and their layout (format
  `DashboardView.LayoutClipboardFormat`: their arrangement and sizes, `DashboardLayoutEngine.Extract`). Pasted items get
  new ids, keep their arrangement and sizes, and go after the line of the last selected item, or at the end
  (`DashboardLayoutEngine.Insert`; lines of a grid band join the anchor's grid band, other bands come as new bands);
  without the layout format, each one goes to the right of the previous one. Visuals of a model the context does not
  know are left out, and a calculated measure whose name is taken by another definition is renamed (`VisualTransfer`
  in `EasyBI.Core`);
- `DashboardCommands` (`AddVisual`, `AddText`, `AddImage`, `EditItem`, `RemoveItem`, `ResetSize`,
  `AddRight`, `AddBelow`, `InsertBand`, `RemoveBand`, `Duplicate`, `MoveItem`, `MoveItemToNewBand`, `ExtendItem`
  — the last three take an `ItemSide` or its name, as `CommandParameter="Below"`...) for custom templates, and public methods
  (`AddNextTo`, `AddIn` a free space, `InsertLine`, `SetAutoSize`, `SetVisual`, `InsertBand`, `BeginEdit` to group
  changes into one undo step...);
- a default template for `DashboardView` (its `Panel` in a scroll viewer, under an `AdornerDecorator` where behaviors
  draw: a custom template shows it with `<ContentPresenter Content="{Binding Panel, RelativeSource={RelativeSource TemplatedParent}}" />`),
  an implicit style for `DashboardItem` (lookless control) and the `DashboardOverlay` style keys
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
A search box at the top of the field list (`FieldList.Search`) and above the values of a filter (from 8 values on,
`FilterItemViewModel.Search`; checked values that are not shown stay checked) narrow the lists down: case and accents
are ignored and every word must appear (`TextMatch.Matches` in `EasyBI.Core`).

In the designer, the visual is sorted from the properties of a chip of the Axis or Values well (ascending ↑ or
descending ↓; one sorted field at a time, the sort following the field when its aggregation or grain changes). The
visual's properties recall the active sort and limit the visual to its first rows (`Top`).
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
- A handler may explain a refusal (`IDropHandler.RefusalReason`, optional): the reason is shown under the target and
  set on it as `DropTarget.Refusal` while the drag is over it.
- A scrollable target, or its scroll viewer, scrolls when the pointer nears its edges (`AutoScroller`, also usable
  on its own); `InsertionIndex` holds the pure insertion rules.
- Style keys `DragSource.PreviewStyleKey` (a `ContentControl`; `DragSource.PreviewTemplate` sets the content),
  `DropTarget.InsertionLineStyleKey` (a `Border`) and `DropTarget.RefusalStyleKey` (a `ContentControl` holding the reason).

In the designer, fields are dropped between chips, chips are reordered within a shelf, and dimensions move between
Axis and Legend keeping their granularity. A refused field says why (`DesignerViewModel.RefusalReason`): only measures
in Values, only dimensions elsewhere, a field already on the shelf, a full axis, a field of another model.

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

## Customizing the components: slots

The parts of the `ReportDesigner`, `FieldList`, `FilterBar`, `FilterEditor` and `DashboardView` are **slots**: named places
where an application adds its own content (before or after the default content), replaces it, or hides it.

```xml
<bi:ReportDesigner Context="{Binding Context}">
    <bi:Slots.Items>
        <!-- Under the preview -->
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.Preview}">
            <Button Content="Publish" Command="{Binding DataContext.PublishCommand, RelativeSource={RelativeSource AncestorType=Window}}" />
        </bi:Slot>
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.Title}" Mode="Before"><TextBlock Text="Title:" /></bi:Slot>
        <bi:Slot Name="{x:Static bi:ReportDesignerSlots.FiltersShelf}" Mode="Hide" />
    </bi:Slots.Items>
</bi:ReportDesigner>
```

```csharp
Slots.Add(designer, ReportDesignerSlots.Header, exportButton);              // after the default content
Slots.Add(designer, ReportDesignerSlots.Fields, help, SlotMode.Before);      // above the field list
Slots.Add(filterBar, FilterBarSlots.AddButton, myAddButton, SlotMode.Replace);
Slots.Hide(designer, ReportDesignerSlots.FieldPanel);
```

- **Names**: `ReportDesignerSlots` (`FieldPanel`, `Fields`, `Header`, `Title`, `VisualTypes`, `Shelves`, `AxisShelf`,
  `LegendShelf`, `ValuesShelf`, `FiltersShelf`, `Preview`, `Properties`), `FieldListSlots` (`Model`, `Search`, `Dimensions`,
  `Measures`, `AddMeasureButton`), `FilterBarSlots` (`Filters`, `Label`, `Slicer`, `AddButton`, `Selection`),
  `FilterEditorSlots` (`Values`, `Period`) and `DashboardSlots` (`Body`, `ItemContent`, `EmptyItemChoices`, `ItemToolbar`). Slots can be
  nested: `Header` contains `Title` and `VisualTypes`. The band menu of a dashboard is customized through
  `BandMenuBehavior.MenuOpening`.
- **Modes**: `After` (default), `Before`, `Replace`, `Hide`. Before and after are left and right of the default content,
  or above and below it in a vertical slot (`Fields`, `Shelves`, `Preview`); several slots with the same name are shown
  in declaration order, and the last `Replace` or `Hide` wins.
- **Data context**: the content takes the data context of the place it is shown in: the `DesignerViewModel` in the
  designer, the application's in the filter bar and the field list, the `SlicerViewModel` in `FilterBarSlots.Slicer`, the
  `FilterItemViewModel` in the filter editor (shown once per filter, so its slots take a template), the `DashboardItem`
  in the dashboard item slots (commands such as `DashboardCommands.RemoveItem` act on the item containing them).
- **Repeated slots**: an element can only be shown once. A slot shown several times (`FilterBarSlots.Slicer`, once per
  global filter, the dashboard item slots, once per item, or slots declared for several components) takes a `ContentTemplate` (`Slots.AddTemplate(...)` in
  code), applied to the data context of each place.
- **Inheritance**: `Slots.Items` is inherited, so slots declared on a window or a panel apply to every component in it;
  the nearest declaration replaces the others. The designer opened to edit a dashboard visual takes the slots of the
  dashboard.
- **Custom templates**: the components are lookless controls. Their default templates (`Themes/*.xaml` of `EasyBI.Wpf`)
  are built from `SlotHost`s (`<bi:SlotHost Slot="..." Orientation="Vertical">` with the default content inside), so a
  template of the application keeps the slots it hosts. A template works with the components' read-only properties
  and commands: `ReportDesigner.ViewModel` (the template's data context); `FieldList.ModelOptions`, `Dimensions`,
  `Measures`, `CanAddMeasure` and `AddMeasureCommand`, `EditMeasureCommand`, `DeleteMeasureCommand`;
  `FilterBar.Slicers`, `SelectionDescription` and `AddFilterCommand`, `RemoveFilterCommand` (on the slicer of its
  data context), `ClearSelectionCommand`; `DashboardView.Panel` and `DashboardCommands`.

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
| `ErrorBrushKey` | error messages, bad values of conditional formats |
| `SuccessBrushKey`, `WarningBrushKey` | good and middle values of conditional formats |
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
EasyBI's light values, while the keys listed in `Kept` (colors the system has no equivalent for, such as Material
Design's success and warning) and the chart palette follow the mapped background (EasyBI's light or dark values). If the library
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

### Renderer options

A connector offers settings of its own to the end user by declaring them in its capabilities: the designer shows them
in the "Options" section of its properties panel (`DesignerPropertySections.Options`) for the visuals they apply to, as
a check box, a list or a number box, and saves their values with the visual.

```csharp
public RendererCapabilities Capabilities { get; } = new(marks)
{
    Options =
    [
        RendererOption.Toggle("scrollable", () => Strings.Scrollable, appliesTo: v => v.Mark.IsCartesian()),
        RendererOption.Choice("curve", () => Strings.Curve, [new("straight", () => Strings.Straight), new("smooth", () => Strings.Smooth)]),
        RendererOption.Number("barWidth", () => Strings.BarWidth, 48, minimum: 4, maximum: 120),
    ],
};

public FrameworkElement Render(RenderRequest request)
{
    var scrollable = request.Visual.GetRendererOption<bool>(Name, "scrollable");   // EasyBI.Core
    ...
}
```

- Labels are functions, read when shown, so that they follow the EasyBI language (`EasyBILocalization.UICulture`).
- Values are stored in `VisualDefinition.Extensions`, one JSON object per renderer under its name
  (`"LiveCharts2": "{\"scrollable\":true}"`); a value equal to the option's default is not stored. The settings of a
  renderer are kept while another one draws the visual. `WithRendererOption` sets one in code.
- The LiveCharts2 connector offers **Scroll bar** (`LiveCharts2Renderer.ScrollableOption`) on cartesian charts: the chart
  shows the first 20 categories (`ScrollWindow`) above a small chart of all of them, where a band marks the visible
  ones; dragging on it scrolls, the mouse wheel on the chart zooms.

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
      "filters": [
        { "field": "Product.Category", "operator": "In", "values": [ "Services" ] },
        { "field": "Date", "operator": "Last", "values": [ "12" ], "grain": "Month" }
      ]
    }
  ],
  "filters": [
    { "model": "sales", "filter": { "field": "Region", "operator": "In", "values": [ "North", null ] } }
  ]
}
```

`null` in `values` means a missing value. Besides `In` and `NotIn`, a date filter's `operator` can be `Between`
(values: from and to as `yyyy-MM-dd`, inclusive, `null` for an open end), `Current`, `Last` or `Previous` (value: the
number of periods; `grain` is their unit). A number dimension or, in a visual, a measure can be compared:
`GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual` (value: the number, invariant) or `Range` (values:
minimum and maximum, inclusive, `null` for an open end). An `AnyOf` filter keeps the rows matching every filter of one of its
`alternatives` (its `field` and `values` are empty): cross-filtering builds one when several selected elements differ
on more than one dimension, `{ "field": "", "operator": "AnyOf", "values": [], "alternatives": [[...], [...]] }`.
`EasyBIContext.Validate(report)` reports the unknown
models and fields of a loaded report, and the formulas that no longer match their model.

Calculated measures created by the end user are saved with the report (the property is omitted when there are none),
and visuals reference them by name like a field:

```json
"measures": [
  { "name": "calc_3f9a1c0d2b7e", "caption": "Margin rate", "model": "sales", "formula": "[Margin] / [Amount]", "format": "P1" }
]
```

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

The demo starts in your system language (English or French) and has a language picker in its toolbar. Its data
picker switches the same sales between memory (20,000 rows) and a SQLite database of 1 or 5 million rows, generated
once in `%LOCALAPPDATA%\EasyBI.Sample`; the toolbar shows how long the last refresh took.

## Contributing

See [AGENTS.md](AGENTS.md) for the code style, language rules and Git workflow.

## Known limitations / roadmap

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
  library for those (see [Following a control library](#following-a-control-library)).
- `EasyBI.Themes.WpfUi` is checked against WPF UI's light and dark themes; its high contrast themes are not verified.
- Dashboard images are embedded in the file (2 MB by default, `DashboardView.MaxImageBytes`).
