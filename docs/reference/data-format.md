# Data format

When a model is reached remotely, four things travel between the application and the server: a model's metadata, the
request of a visual, its data, and the distinct values of a dimension. `DataSerializer` (EasyBI.Core) writes and reads
them as plain JSON, so that the server can be written in .NET or in any other language. The transport is yours (see
[Your own data source](../guide/semantic-model.md#your-own-data-source)).

| What | Write | Read |
|---|---|---|
| Metadata | `Serialize(ModelMetadata)` | `DeserializeMetadata(json)` |
| Request of a visual | `Serialize(VisualDataRequest)` | `DeserializeRequest(json)` |
| Data of a visual | `Serialize(VisualData)` | `DeserializeData(json)` |
| Distinct values | `SerializeValues(values)` | `DeserializeValues(json, dataType)` |

`DataSerializer.Options` reads and writes the same JSON with `JsonSerializer` directly: on a stream, with ASP.NET Core's
`Results.Json`, `HttpClient`'s `ReadFromJsonAsync`...

## Metadata

```json
{
  "name": "sales",
  "caption": "Sales",
  "fields": [
    { "name": "Date", "caption": "Date", "kind": "Dimension", "dataType": "Date", "isCalculated": false },
    { "name": "Region", "caption": "Region", "kind": "Dimension", "dataType": "Text", "isCalculated": false },
    { "name": "Amount", "caption": "Revenue", "kind": "Measure", "dataType": "Number", "defaultAggregation": "Sum", "format": "#,##0 €", "isCalculated": false },
    { "name": "MarginRate", "caption": "Margin rate", "kind": "Measure", "dataType": "Number", "format": "P1", "isCalculated": true }
  ]
}
```

Captions are in one language: a server that translates them returns them in the language the application asks for (the
sample's server follows the request's `Accept-Language`).

## Request

```json
{
  "visual": {
    "model": "sales",
    "type": "Bar",
    "axis": [ { "field": "Date", "grain": "Quarter" } ],
    "legend": { "field": "Channel", "grain": "None" },
    "values": [ { "measure": "Amount" }, { "measure": "WebRevenue" } ],
    "filters": [ { "field": "Region", "operator": "In", "values": [ "North" ], "grain": "None" } ]
  },
  "measures": [ { "name": "WebRevenue", "caption": "Web revenue", "model": "sales", "formula": "SUM([Amount]; [Channel] = \"Web\")" } ],
  "hasTooltips": true,
  "today": "2026-10-27",
  "uiCulture": "en-US",
  "formatCulture": "en-US"
}
```

`visual` is a visual as in the [report format](report-format.md), with the filters of its page and the selections of
other visuals already applied. `measures` are the end user's calculated measures it uses. `today` is the user's day
(`yyyy-MM-dd`, no time zone), on which relative periods and comparisons with a year earlier are computed. The cultures
are omitted for the server's own.

## Data

```json
{
  "result": {
    "columns": [
      { "key": "Region", "caption": "Region", "role": "Dimension", "dataType": "Text" },
      { "key": "Date.Year", "caption": "Date (Year)", "role": "Dimension", "dataType": "Text" },
      { "key": "Amount", "caption": "Revenue", "role": "Measure", "dataType": "Number", "format": "#,##0 €" }
    ],
    "rows": [
      [ "North", { "grain": "Year", "key": 2023 }, 1258638.78 ],
      [ "North", { "grain": "Year", "key": 2024 }, 1385525.22 ]
    ]
  },
  "matrix": {
    "rowCaptions": [ "Region" ],
    "columnCaption": "Date (Year)",
    "rows": [ { "values": [ "North" ], "labels": [ "North" ] }, { "values": [ "South" ], "labels": [ "South" ] } ],
    "columns": [ { "values": [ { "grain": "Year", "key": 2023 } ], "labels": [ "2023" ] }, ... ],
    "measures": [ { "key": "Amount", "caption": "Revenue", "format": "#,##0 €" } ],
    "cells": [ [ [ 1258638.78 ], [ 1385525.22 ] ], [ [ 1064750.5 ], [ 1059973.01 ] ] ],
    "rowTotals": [ [ 2644164.0 ], [ 2124723.51 ] ],
    "columnTotals": [ [ 2323389.28 ], [ 2445498.23 ] ],
    "grandTotals": [ 4768887.51 ]
  }
}
```

`result` has one row per group, with one value per column. `matrix` is only there for a matrix: its cells are indexed
`cells[row][column][measure]` (see `MatrixData`).

## Values

| Value | JSON |
|---|---|
| Missing | `null` |
| Text, enum | `"North"` |
| Number | `1258.5` (`"NaN"` for a value that is not a number) |
| Boolean | `true` |
| Date | `"2024-03-10"`, or `"2024-03-10T14:30:00.0000000"` with its time |
| Period of a date grain (year, quarter, month) | `{ "grain": "Month", "key": 202403 }`: the key is `2024`, `20241` (Q1) or `202403` |

The column's `dataType` tells a date from a text: in a `Date` column, `"2024-03-10"` is read as a date. Distinct values
are a JSON array of values, read with the type of their dimension (`Metadata.GetField(field).DataType`).

## Compatibility

Like the report format, this format only grows with optional properties: a reader ignores properties it does not know
and uses defaults for the missing ones, so that an application and its server can be updated one after the other.
