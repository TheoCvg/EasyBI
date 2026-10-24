# Legends

A chart places its legend from its size: on the right, then below, then hidden. The end user can choose another place
in the designer's Visual section (**Legend**: automatic, right, bottom, top, left or hidden), saved in
`VisualDefinition.LegendPosition` (`ChartLegend`, omitted when `Auto`). A chosen side applies while the chart has room
for it, otherwise the automatic place does.

Legends keep their library's look. When a legend has more entries than its room shows (a column on a side, a row above
or below), it shows them **by pages**: a small pager (◀ 2/5 ▶, `ChartLegendPager`, restyled like any lookless control)
appears over the chart in the corner next to the legend; its buttons and the mouse wheel turn the pages. (OxyPlot names
the slices of a pie around it instead of in a legend: a hidden legend drops those names.)
