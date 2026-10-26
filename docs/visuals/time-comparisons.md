# Comparisons over time

Each measure can be seen through time: its value the year before, the change since, or its total since the start of
the year.

![2025 compared with 2024: revenue and margin, with their % change](../images/visuals/time-kpi.png#only-light){ width="560" }
![2025 compared with 2024: revenue and margin, with their % change](../images/visuals/time-kpi-dark.png#only-dark){ width="560" }

![Revenue by month in bars, and the same months a year earlier as a line](../images/visuals/time-chart.png#only-light)
![Revenue by month in bars, and the same months a year earlier as a line](../images/visuals/time-chart-dark.png#only-dark)

## In the designer

Select a measure's chip: its **Over time** list offers the calculations. Drop the same measure twice to show it with
and without one, as above.

## In code

```csharp
Values =
[
    new("Amount"),
    new("Amount") { Time = TimeCalculation.PercentChangeFromPreviousYear },
],
Filters = [PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)],   // this year
```

| `TimeCalculation` | Shows |
|---|---|
| `PreviousYear` | The value a year earlier |
| `ChangeFromPreviousYear` | The difference with a year earlier |
| `PercentChangeFromPreviousYear` | The difference in %; a KPI adds ▲ / ▼ |
| `YearToDate` | The running total since January 1st, along the date axis |

## How the previous year is computed

The same query runs a second time with its dates shifted back one year (period filters, clicked periods), and its
groups are matched with the first: each month with the same month a year earlier, per series. It works on any model,
in `VisualPipeline.PrepareAsync`.

!!! info "A period under way is compared with the same days"
    On February 15th, *this year* is compared with January 1st to February 15th of last year, not with the whole of
    last year, since there is no data after today. The same goes for the month under way, the last 12 months, or a
    range ending later.

!!! warning "A date is needed"
    The calculation needs a date on the axis or a date filter. Without either, it is empty.
