# Report format (`easybi/v1`)

```json
{
  "schema": "easybi/v1",
  "title": "Dashboard",
  "visuals": [
    {
      "title": "Revenue by region and channel",
      "model": "sales",
      "mark": "Bar",
      "axis": [ { "field": "Region", "grain": "None", "labels": { "Île-de-France": "IDF" } } ],
      "legend": { "field": "Channel", "grain": "None" },
      "legendPosition": "Bottom",
      "values": [ { "measure": "Amount" }, { "measure": "Count", "mark": "Line", "secondaryAxis": true } ],
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

Text and image visuals carry their content and no model:

```json
{ "mark": "Text", "text": { "text": "Sales dashboard", "fontSize": 24, "isBold": true, "alignment": "Left" } }
{ "mark": "Image", "image": { "data": "iVBORw0KGgo...", "mediaType": "image/png", "fit": "Contain" } }
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
