# Visual types

A visual is a `VisualDefinition`: a model, a type, and fields in its wells. Every type below is offered in the
designer's gallery and drawn by the connector you plug in (LiveCharts2 or OxyPlot), except the KPI, the gauge, the
table, the matrix, the text and the image, which EasyBI draws itself whatever the connector.

```csharp
new VisualDefinition
{
    Title = "Revenue by region",
    Model = "sales",
    Type = VisualType.Bar,     // (1)!
    Axis = [new("Region")],
    Legend = new("Channel"),   // (2)!
    Values = [new("Amount")],
};
```

1. The type: one of the `VisualType` below, or [one of your application's](../customization/visual-types.md).
2. Optional: splits the values into one series per channel.

## Comparison

<div class="grid cards" markdown>

-   **Bars** · `VisualType.Bar`

    ---

    ![Bars](../images/visuals/types/bar.png#only-light)
    ![Bars](../images/visuals/types/bar-dark.png#only-dark)

    Axis (up to 3 fields), Legend, Values. One bar per category and series.

-   **Stacked bars** · `VisualType.StackedBar`

    ---

    ![Stacked bars](../images/visuals/types/stacked-bar.png#only-light)
    ![Stacked bars](../images/visuals/types/stacked-bar-dark.png#only-dark)

    The series of a category piled up: the height is their total.

-   **Points** · `VisualType.Scatter`

    ---

    ![Points](../images/visuals/types/scatter.png#only-light)
    ![Points](../images/visuals/types/scatter-dark.png#only-dark)

    A point per category and series, without a line.

</div>

## Trend

<div class="grid cards" markdown>

-   **Lines** · `VisualType.Line`

    ---

    ![Lines](../images/visuals/types/line.png#only-light)
    ![Lines](../images/visuals/types/line-dark.png#only-dark)

    Best with a date on the axis: one line per series.

-   **Areas** · `VisualType.Area`

    ---

    ![Areas](../images/visuals/types/area.png#only-light)
    ![Areas](../images/visuals/types/area-dark.png#only-dark)

    Lines filled down to the axis.

</div>

!!! tip "Mix them"
    The bars, stacked bars, points, lines and areas can be mixed in one chart, one type per measure, with a second
    value axis: see [Combined charts](combined-charts.md).

## Composition

<div class="grid cards" markdown>

-   **Pie** · `VisualType.Pie`

    ---

    ![Pie](../images/visuals/types/pie.png#only-light)
    ![Pie](../images/visuals/types/pie-dark.png#only-dark)

    Category (1 field), Value (1 measure).

-   **Donut** · `VisualType.Donut`

    ---

    ![Donut](../images/visuals/types/donut.png#only-light)
    ![Donut](../images/visuals/types/donut-dark.png#only-dark)

    A ring with the total in its hole.

-   **Waterfall** · `VisualType.Waterfall`

    ---

    ![Waterfall](../images/visuals/types/waterfall.png#only-light)
    ![Waterfall](../images/visuals/types/waterfall-dark.png#only-dark)

    How each category adds to the running total, up in green, down in red, then the total.

</div>

## Indicators and tables

<div class="grid cards" markdown>

-   **KPI** · `VisualType.Kpi`

    ---

    ![KPI](../images/visuals/types/kpi.png#only-light)
    ![KPI](../images/visuals/types/kpi-dark.png#only-dark)

    Values only: one big number per measure. Shows ▲ / ▼ for [changes over time](time-comparisons.md).

-   **Gauge** · `VisualType.Gauge`

    ---

    ![Gauge](../images/visuals/types/gauge.png#only-light)
    ![Gauge](../images/visuals/types/gauge-dark.png#only-dark)

    One value on a half circle, with a scale and a target.

-   **Table** · `VisualType.Table`

    ---

    ![Table](../images/visuals/types/table.png#only-light)
    ![Table](../images/visuals/types/table-dark.png#only-dark)

    Rows (up to 3 fields), Values.

-   **Matrix** · `VisualType.Matrix`

    ---

    ![Matrix](../images/visuals/types/matrix.png#only-light)
    ![Matrix](../images/visuals/types/matrix-dark.png#only-dark)

    Rows crossed with columns, with totals. See [Matrix](matrix.md).

</div>

### Gauge settings

The scale and the target of a gauge are numbers of the visual, edited in the designer's *Visual* section:

```csharp
new VisualDefinition
{
    Model = "sales", Type = VisualType.Gauge, Values = [new("MarginRate")],
    Gauge = new GaugeSettings { Minimum = 0, Maximum = 0.5, Target = 0.35 },
};
```

| Setting | Empty means |
|---|---|
| `Minimum` | 0 |
| `Maximum` | A round number above 0, the value and the target |
| `Target` | No target mark |

The measure's [format](conditional-formatting.md) colors the value and adds its icon.

## Content

Texts and images query no model. Users add them to a [dashboard](../dashboard/index.md) and edit them in place.

<div class="grid cards" markdown>

-   **Text** · `VisualType.Text`

    ---

    ![Text](../images/visuals/types/text.png#only-light)
    ![Text](../images/visuals/types/text-dark.png#only-dark)

    ```csharp
    Text = new TextContent
    {
        Text = "Sales dashboard",
        FontSize = 24,
        IsBold = true,
        Alignment = TextAlign.Left,
    }
    ```

-   **Image** · `VisualType.Image`

    ---

    Embedded in the report, as base64:

    ```csharp
    Image = new ImageContent
    {
        Data = Convert.ToBase64String(bytes),
        MediaType = "image/png",
        Fit = ImageFit.Contain,
    }
    ```

</div>

## Common settings

| Property | |
|---|---|
| `Title` | Shown above the visual. |
| `Sort` | A field or measure, ascending or descending: `Sort = new("Amount", Descending: true)`. |
| `Top` | Keeps the first N categories after sorting. |
| `Filters` | Filters of this visual only: see [Filters](../guide/filtering.md). |
| `LegendPosition` | See [Legends](legends.md). |
| `Tooltip` | See [Tooltips](tooltips.md). |

Sort, Top N and filters run inside the query, in the database.
