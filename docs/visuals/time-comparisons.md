# Comparisons over time

Each measure of a visual can be seen through time (`MeasureRef.Time`, the "Calculation" list of a measure chip in the
designer): its value, its value the previous year, the change since (in value or in %), or its total since the start of
the year along the date axis.

```csharp
Values = [new("Amount"), new("Amount") { Time = TimeCalculation.PercentChangeFromPreviousYear }],
Filters = [PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)],   // this year vs last year
```

The previous year is the same query with its dates shifted back one year (period filters, clicked periods), run once
more and matched on the groups (each month with the same month a year earlier, per series). A period under way (this
year, the last 12 months, a range ending later, the current year or month clicked in a chart) is compared with the
same days a year earlier: on 15 February, this year is compared with 1 January to 15 February of last year, and the
month under way with the same days of its month, since there is no data after today. It needs a date on the axis
or a date filter, and is empty otherwise. The year-to-date total adds the values up along the date axis and starts
again each year. A KPI shows a change with ▲ / ▼ in the measures' or the error color. This runs in
`VisualPipeline.PrepareAsync` (`ISemanticModel.ExecuteWithTimeAsync`) on top of any model.
