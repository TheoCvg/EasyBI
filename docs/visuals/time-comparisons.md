# Comparisons over time

Each measure can be seen through time: compared with another period (the previous month, the same period last year,
three months earlier...), added up since the start of the year, or both. Every list of choices comes from a catalog
your application extends with its own periods, presentations and accumulations.

![2025 compared with 2024: revenue and margin, with their % change](../images/visuals/time-kpi.png#only-light){ width="560" }
![2025 compared with 2024: revenue and margin, with their % change](../images/visuals/time-kpi-dark.png#only-dark){ width="560" }

![Revenue by month in bars, and the same months a year earlier as a line](../images/visuals/time-chart.png#only-light)
![Revenue by month in bars, and the same months a year earlier as a line](../images/visuals/time-chart-dark.png#only-dark)

## In the designer

Select a measure's chip: its **Over time** section has the settings. Drop the same measure twice to show it with and
without one, as above.

| Setting | Choices |
|---|---|
| **Add up** | No · since the start of the year, of the quarter, of the month · running total |
| **Compare with** | Nothing · the previous period · the same period last year · earlier by *n* days, weeks, months, quarters or years |
| **Show** | The compared value · the change · the change in % |
| **Favorable direction** | Higher is better · lower is better (as the model declares the measure, by default) |

**The previous period** follows the visual: each month of the axis is compared with the month before, each year with
the year before. Without a date on the axis, the period is as long as the filtered one: a month before a month, the
30 days before the last 30.

**Added up and compared**, the totals of both periods are: the revenue since January 1st compared with the revenue
since January 1st of last year, month by month.

**The favorable direction** gives the color of the changes: a KPI shows ▲ / ▼ in the measure's color when the change
goes the good way, in the error color otherwise, so that rising costs show in red.

## Periods and presentations of the report

The end user adds their own entries in the designer, saved in the report and offered to every visual of it:

- **Compare with › New named period…** creates a period *n* days, weeks, months, quarters or years earlier, with a
  name: « Two years earlier », « Last season ». The name shows in the list, on the chip and in the column's caption.
- **Show › New presentation…** creates a presentation written as a formula over `[Value]`, the value of the measure,
  and `[Compared]`, its value in the compared period (`[Valeur]` and `[Comparée]` in French):

| Presentation | Formula |
|---|---|
| Index | `[Value] / [Compared] * 100` |
| Gap | `[Compared] - [Value]` |
| Ratio, empty when the compared value is small | `IF([Compared] > 1000; [Value] / [Compared])` |

  The formula takes numbers, `+ - * /`, parentheses and `IF(condition; value; otherwise)` with comparisons, `AND`,
  `OR` and `NOT`. A missing value or a division by zero gives an empty value. It is checked as it is typed. A format,
  and **Shows a change** (a KPI then shows its trend in the color of the favorable direction) complete it.

When a period or a presentation of the report is selected, its settings appear under it: changing them changes every
visual using it. **Delete** removes it: the other visuals using it show empty values.

```csharp
var report = new ReportDefinition
{
    ComparisonPeriods = [new() { Id = "report.season", Caption = "Last season", Count = 2, Unit = TimeUnit.Quarter }],
    ComparisonOutputs = [new() { Id = "report.index", Caption = "Index", Formula = "[Value] / [Compared] * 100", Format = "N1" }],
};
// A visual references them by id
new MeasureRef("Amount") { Time = new MeasureTime { Period = "report.season", Show = "report.index" } };
```

Their ids start with `TimeIds.ReportPrefix` (`report.`), which an application does not use for its own entries. The
open report's entries are in `EasyBIContext.Comparisons` (a `ComparisonContext`), which a `DashboardView` loads and
saves with its document; a visual sends those it uses to its model (`VisualDataRequest.ComparisonPeriods` and
`ComparisonOutputs`), which computes them with its catalog (`TimeCatalog.With`), on a server as well.

## In code

```csharp
Values =
[
    new("Amount"),
    new("Amount") { Time = MeasureTime.PercentChangeFromPreviousYear },
    new("Amount") { Time = new MeasureTime { Period = TimeIds.PreviousPeriod, Show = TimeIds.Change } },
    new("Amount") { Time = new MeasureTime { Period = TimeIds.Offset, Count = 3, Unit = TimeUnit.Month } },
    new("Amount") { Time = new MeasureTime { Accumulation = TimeIds.YearToDate, Period = TimeIds.LastYear } },
    new("Cost") { Time = MeasureTime.ChangeFromPreviousYear, Favorable = Favorable.Lower },
],
Filters = [PeriodFilters.Relative("Date", FilterOperator.Current, DateGrain.Year)],   // this year
```

| `MeasureTime` | Id of |
|---|---|
| `Accumulation` | An accumulation (`TimeIds.YearToDate`, `QuarterToDate`, `MonthToDate`, `Running`); null for none |
| `Period` | The compared period (`TimeIds.PreviousPeriod`, `LastYear`, `Offset`); null for no comparison |
| `Count`, `Unit` | How far back, for `TimeIds.Offset` |
| `Show` | What the comparison shows (`TimeIds.Value`, `Change`, `PercentChange`) |

`MeasureTime.PreviousYear`, `ChangeFromPreviousYear`, `PercentChangeFromPreviousYear` and `YearToDate` are ready-made.
A model declares which way its measures are good: `.Measure(s => s.Cost, "Cost", favorable: Favorable.Lower)`.

## Your own periods, presentations and accumulations

`TimeCatalog` holds the entries. Add yours to `TimeCatalog.Default` at startup, with an id that has a prefix: the
designer offers them next to EasyBI's, and models compute them.

```csharp
TimeCatalog.Default
    // The same weekday last year: 52 weeks back
    .Add(new ComparisonPeriod("retail.week364", () => Texts.SameWeekday, _ => new TimeShift(Months: 0, Days: -364))
    {
        ShortCaption = _ => "W-52",                             // in the column's caption
    })
    // An index: 100 when the value equals the compared one
    .Add(new ComparisonOutput("acme.index", () => Texts.Index, (value, compared) => compared is { } c && c != 0 ? value / c * 100 : null)
    {
        ColumnCaption = () => "{0} index vs {1}",               // {0}: the measure, {1}: the period
        Format = "N0",
    })
    // A fiscal year starting in April
    .Add(new Accumulation("acme.fiscal", () => Texts.FiscalYearToDate, d => new DateTime(d.Month >= 4 ? d.Year : d.Year - 1, 4, 1)));

TimeCatalog.Default.RemoveAccumulation(TimeIds.Running);     // EasyBI's entries can be removed or replaced
```

- A **period** gives how far back its dates move (`TimeShift`: months, then days) from a `TimeComparisonContext`: the
  granularity of the date on the visual's axis, the days its filters keep, and the `MeasureTime` (its `Count` and
  `Unit` when `HasOffset` is true). Null leaves the comparison empty.
- A **presentation** computes a value from the value and the compared one; `IsChange` makes a KPI show its trend and
  color, `Symbol` stands for it on the designer's chips ("Δ%").
- An **accumulation** gives the start of the period a date belongs to: values add up from there.
- Labels are functions, read when shown, so that they follow the EasyBI language.

The designer offers the entries of `EasyBIContext.Time`, and a model computes with those of `QuerySemanticModel.Time`:
both are `TimeCatalog.Default` unless you give them another catalog. A model reached through a server computes with
the server's catalog: declare your entries there too. A visual using an entry the catalog does not have shows it
empty.

## How another period is computed

The same query runs again with its dates moved back (period filters, clicked periods), once for each period the
visual compares with, and its groups are matched with the first: each month with the month its dates moved back fall
in, per series. It works on any model, in `VisualPipeline.PrepareAsync`.

!!! info "A period under way is compared with the same days"
    On February 15th, *this year* is compared with January 1st to February 15th of last year, not with the whole of
    last year, since there is no data after today. The same goes for the month under way, the last 12 months, or a
    range ending later.

!!! warning "A date is needed"
    A comparison needs a date on the axis or a date filter. Without either, it is empty.
