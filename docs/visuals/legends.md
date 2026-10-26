# Legends

A chart places its legend from its size: on the right, then below, then hidden when there is no room. Users can pick
another place.

![A legend of eight products on the right of a chart, shown by pages with a pager in its corner](../images/visuals/legend-pages.png#only-light){ width="640" }
![A legend of eight products on the right of a chart, shown by pages with a pager in its corner](../images/visuals/legend-pages-dark.png#only-dark){ width="640" }

## Where

In the designer's *Visual* section, **Legend** offers *Automatic*, *Right*, *Bottom*, *Top*, *Left* and *Hidden*.

```csharp
LegendPosition = ChartLegend.Bottom,   // omitted from the JSON when Auto
```

A chosen side applies while the chart has room for it; otherwise the automatic place does.

## By pages

When a legend has more entries than its room shows, it shows them **by pages**: a small pager (◀ 2/5 ▶) appears in the
corner next to it; its buttons and the mouse wheel turn the pages. The pager is a lookless control,
`ChartLegendPager`, restyled like any other. Legends otherwise keep their charting library's look.

!!! note "OxyPlot pies"
    OxyPlot names the slices of a pie around it rather than in a legend: hiding the legend drops those names.
