# Conditional formatting

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

A **pie** (`VisualType.Pie`) is a full disc; a **donut** (`VisualType.Donut`) is a ring with the total of its value in its
hole (`ChartCenterLabel`, which places a text in the middle of a connector's plot area).

A **gauge** (`VisualType.Gauge`) shows a single value on a half circle, drawn by the built-in `GaugeRenderer` whatever
the connector. Its scale and target are numbers of the visual (`VisualDefinition.Gauge`, a `GaugeSettings`, edited in the
designer's Visual section): `Minimum` (empty: 0), `Maximum` (empty: a round number above 0, the value and the target,
see `GaugeScale`) and `Target`, marked on the arc. The measure's format colors the value and adds its icon.

A **waterfall** (`VisualType.Waterfall`) shows how each category's value adds to the running total, up in the theme's
success color, down in its error color, and ends with a bar of the total (`Waterfall.Of` gives any connector the steps
and the chart with its "Total" category). It takes an axis and a single value; LiveCharts2 draws it with candles,
OxyPlot with interval bars. A rule of the measure's format colors the steps it matches; clicking a step cross-filters
on its category.

Besides charts, KPIs, tables and matrices, a visual can be a **text** (`VisualType.Text`, content in
`VisualDefinition.Text`) or an **image** (`VisualType.Image`, embedded as base64 in `VisualDefinition.Image`).
They query no model and are drawn by the built-in `TextRenderer` and `ImageRenderer`.
