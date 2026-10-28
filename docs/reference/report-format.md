# Report format (`easybi/v1`)

```json
{
  "schema": "easybi/v1",
  "title": "Dashboard",
  "visuals": [
    {
      "title": "Revenue by region and channel",
      "model": "sales",
      "type": "Bar",
      "axis": [ { "field": "Region", "grain": "None", "labels": { "Île-de-France": "IDF" } } ],
      "legend": { "field": "Channel", "grain": "None" },
      "legendPosition": "Bottom",
      "values": [ { "measure": "Amount" }, { "measure": "Count", "seriesType": "Line", "secondaryAxis": true } ],
      "tooltip": { "showPercent": true, "values": [ { "measure": "Quantity" } ], "template": "{category}: {value}" },
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

A visual's `type` is the id of its visual type: one of EasyBI's (`Bar`, `StackedBar`, `Line`, `Area`, `Scatter`, `Pie`,
`Donut`, `Waterfall`, `Kpi`, `Gauge`, `Table`, `Matrix`, `Text`, `Image`, `Variable`, read whatever their case) or one an application
declares. A measure's `seriesType` draws its series with another cartesian type than the visual's.

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
  { "name": "calc_3f9a1c0d2b7e", "caption": "Margin rate", "model": "sales", "formula": "[Margin] / [Amount]", "format": "P1" },
  { "name": "Amount", "caption": "Average revenue", "model": "sales", "formula": "AVERAGE([Amount])", "format": "N0" }
]
```

A measure named like a measure of its model (the second one) redefines it in the report; an empty `caption` keeps the
model's.

The report's [variables](../guide/variables.md) are saved with their current values (the property is omitted when
there are none). Values are invariant, like filter values:

```json
"variables": [
  { "name": "N", "model": "sales", "formula": "10 BETWEEN 3 AND 25", "value": "12", "format": "N0" },
  { "name": "TopCustomers", "model": "sales", "formula": "SELECT TOP [@N] [Customer] ORDER BY [Amount] DESC" }
]
```

A variable is its `formula`, stored with field names (see [the formulas of variables](../guide/variables.md#what-a-formula-gives));
`value` is the value the end user gave a parameter, omitted while it is its formula's. Visuals reference
variables by name: a filter value `"[@N]"` (a set's values with `In` and `NotIn`), `"topVariable": "N"`, a field
`{ "field": "Customer", "set": "TopCustomers" }` grouped in true (the set's values) and false (the others), `[@N]` in a
formula, `{N}` in a title or a text, and `{ "type": "Variable", "variable": "N" }` shows one on the dashboard.

Text and image visuals carry their content and no model:

```json
{ "type": "Text", "text": { "text": "Sales dashboard", "fontSize": 24, "isBold": true, "alignment": "Left" } }
{ "type": "Image", "image": { "data": "iVBORw0KGgo...", "mediaType": "image/png", "fit": "Contain" } }
```

## Dashboard file (`easybi-dashboard/v1`)

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
