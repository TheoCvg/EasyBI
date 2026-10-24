# Calculated measures

A measure is a row value that the end user aggregates (sum, average...). Ratios need more: the margin rate of a region
is its total margin divided by its total revenue, not the average of its sales' rates. Calculated measures are
computed from the rows of each group, inside the same query (in SQL with EF Core), and can be sorted and limited like
the others; the end user cannot change their aggregation.

```csharp
model
    .Ratio(s => s.Margin, s => s.Amount, "Margin rate", "MarginRate", format: "P1")      // Σ margin / Σ revenue
    .Ratio(s => s.Amount, s => 1, "Average basket", "AverageBasket", format: "#,##0 €")   // Σ revenue / number of sales
    .Calculated(g => g.Sum(s => s.Amount) - g.Sum(s => s.Cost), "Gross profit", "GrossProfit");
```

`Ratio` is empty when the denominator sums to zero. With `Calculated`, guard divisions yourself: some databases fail
on a division by zero.

A measure can also be declared as a formula, written as end users write theirs (see below) with the names of the
fields declared before it. It runs inside the query too, and end users can read and rewrite it:

```csharp
model.Formula("SUM([Amount]; [Channel] = \"Web\") / [Amount]", "Web share", "WebShare", format: "P1");
```

An invalid formula, or one using a field declared after it, throws an `ArgumentException`. `FieldDescriptor.Formula`
gives the formula of such a measure.

End users create their own from the field list (**+ Calculated measure**, then ✎ and ✕ on hover): a name, a format
and a formula over the model's fields, typed with their captions and checked as it is typed, with its total as a
preview. The editor colors the formula and underlines its problem, shows how to write the function the caret is in,
offers the operators as a keypad (calculation, comparison, logic) and the functions, measures and dimensions in tabs
to search and insert. Its suggestions only offer what can be written where the caret is: in the value of `SUM(`, the
measures and number dimensions; in a condition, the dimensions; after `[Region] =`, the values of Region; after a
value in a condition, AND and OR. Every item and operator has a help tooltip (a plain content, so tooltips keep the
application's style). `FormulaEditing` (`Tokenize`, `Calls`, `CallAt`, `CompletionAt`, `ContextAt`) and
`MeasureFormula.Check` (the problem and where it is written) give the same help to an application that builds its own
editor, and `CalculatedMeasureEditor` exposes it as a view-model (`Tabs`, `Search`, `OperatorGroups`, `Suggest`,
`Accept`, `Insert`, `CurrentCall`, `Problem`):

```
[Margin] / [Revenue]            ([Revenue] - [Margin]) / [Number of sales]            [Margin] * 100 / [Revenue]
SUM([Quantity] * [Unit price])  SUM([Revenue]; [Region] = "North") / [Revenue]        DISTINCTCOUNT([Customer])
COUNT([Channel] = "Web" AND [Date] >= "2024-01-01")                                   IF([Revenue] > 1000; [Margin]; 0)
```

A measure on its own is aggregated as in the field list. The aggregation functions `SUM`, `AVERAGE` (or `AVG`), `MIN`,
`MAX`, `COUNT` and `DISTINCTCOUNT` compute a value from the rows of each group, like the measures declared in code:
inside them, measures and dimensions are read row by row, and an optional last argument keeps only the rows matching
a condition (`COUNT()` counts the rows, `COUNT(condition)` those matching it; `DISTINCTCOUNT` does not count empty
values). `IF(condition; value if true; value otherwise)` works on the group or on each row; without its last value, it
is empty. Conditions compare values with `= <> < <= > >=` (texts and yes/no values with `=` and `<>` only) and combine
them with `AND`, `OR` and `NOT`; `TRUE` and `FALSE` are the yes/no values. The operators are `+ - * /` with
parentheses; numbers take a decimal point, texts and dates go in double quotes (`"North"`, `"2024-12-31"`), and the
arguments are separated by `;` or `,`. A division by zero gives an empty value. A dimension cannot be used outside a
function, nor a measure calculated by the model (`Ratio`, `Calculated`) inside one. The formula runs in the same query
as the other measures (in SQL with EF Core: `CASE WHEN`, `COUNT(DISTINCT ...)`). `MeasureFormula.Functions` lists the
functions with their usage and description in the EasyBI language. These measures live in `EasyBIContext.Measures` (one context per open report): every field list, designer
and visual of the context sees them, and a `DashboardView` loads and saves them with its document (undo does not
remove them). An application that saves reports without a dashboard puts `Context.Measures.Measures` in
`ReportDefinition.Measures` and loads them back with `Context.Measures.Load(...)`.

A formula can use the report's other measures, like those of the model: with `[Cost] = [Revenue] - [Margin]`, the
cost rate is `[Cost] / [Revenue]`. They are computed for each group, so they are used on their own, not inside a
function, and they are expanded into the same query (a visual's query carries the measures its measures use:
`MeasureContext.Needed`, `UsedBy`). A measure cannot use itself, even through others ("A measure cannot use itself:
this measure → [Cost] → this measure"), which is checked as it is typed and when a report is opened. Deleting a measure
names the measures that use it (`MeasureContext.Using`); copied visuals take the measures they use along, and the
formulas of pasted measures follow those that are renamed (`MeasureFormula.Rename`). `MeasureFormula.Validate`,
`Check` and `ToDisplay` take the report's measures and the name of the measure being written.

End users can also change the measures declared in code (✎ on any measure): the editor starts from the measure's
formula, or from the measure alone (`[Revenue]`) for one declared with `Measure`, `Ratio` or `Calculated`. Their
version is saved in the report as a calculated measure **named like the model's measure**, which redefines it for the
whole report (every visual showing it follows): a formula that is only the measure itself keeps the model's
computation and the aggregation each visual chooses, and only changes the caption and the format; any other formula
replaces the computation (`AVERAGE([Revenue])` changes the aggregation), the measure's name in it still meaning the
model's definition. Every other formula, the model's ones (`Formula`) included, reads a redefined measure as the
report defines it. ↺ in the field list, or **Restore** in the editor, removes the redefinition. `MeasureContext`
merges both (`MeasuresOf`, `GetField`, `Definition`), and pasting a visual never redefines the target's measures: a
copied redefinition is added as a calculated measure of its own.
