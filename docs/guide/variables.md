# Variables

A number of customers that a slider changes, a rate in a formula, the top 10 suppliers, the average basket: the
variables of a report are **written as formulas**, in the same editor as the calculated measures, and every visual
using them follows when they change.

```sql
N            = 10 BETWEEN 3 AND 25                       -- a slider from 3 to 25
Rate         = 0.2
Channel      = "Web" IN ("Web"; "Store")                 -- a list
Start        = "2025-01-01"                              -- a date
Average      = AVERAGE([Revenue])                        -- computed once
Best         = MAX([Revenue] BY [Customer])              -- the revenue of the best customer
TopCustomers = SELECT TOP [@N] [Customer] ORDER BY [Revenue] DESC
NorthBuyers  = SELECT [Customer] WHERE [Region] = "North" AND [Revenue] > [@Average]
Mine         = SELECT [Customer] IN ("Smith"; "Jones")
```

## What a formula gives

| Formula | Variable | On the dashboard |
|---|---|---|
| A value: `10`, `"Web"`, `"2025-01-01"`, `TRUE` | A **parameter** (a text written yyyy-MM-dd is a date) | A box, a date picker, a check box |
| `10 BETWEEN 3 AND 25`, optionally `STEP 0.5` | A number parameter between two bounds | A slider |
| `"Web" IN ("Web"; "Store")` | A parameter chosen from a list | A list |
| `SELECT ...` | A **set**: values of a dimension | Its values |
| Any other formula: `AVERAGE([Revenue])`, `[@Rate] * 100` | A **computed value**, a number computed once | The value |

### Sets

```sql
SELECT [TOP n] [Dimension] [IN (values)] [WHERE conditions] [ORDER BY expression [ASC | DESC]]
```

- `TOP` keeps the first values, as many as a number or a number variable (`TOP [@N]`), in the order of `ORDER BY`.
- `IN` lists values; alone, the set is that list.
- `WHERE` takes conditions joined by `AND`, like a formula's: those on **dimensions** keep rows (`[Region] = "North"`,
  `[Date] >= "2025-01-01"`, with `OR` and `NOT`), those on **measures** keep values (`[Revenue] > 1000`,
  `SUM([Revenue]; [Channel] = "Web") > 100`), computed on the rows kept, as SQL's `WHERE` and `HAVING`.
- `ORDER BY` takes any expression of the measures' language; without it, the values come in their order.

### Values by a dimension

In a computed value, `SUM`, `AVERAGE`, `MIN` and `MAX` take a value **by** a dimension: the value is computed for each
of the dimension's values, then aggregated. `MAX([Revenue])` is the largest row; `MAX([Revenue] BY [Customer])` is the
revenue of the best customer, `AVERAGE([Revenue] BY [Month])` the average month. They combine with the rest of the
formula: `MAX([Revenue] BY [Customer]) / SUM([Revenue])` is the share of the best customer. `BY` is only written in a
variable: a measure's value depends on each group of a visual.

The value of each of the dimension's values is queried in the `IQueryable`; their aggregation is done on the values
returned, one for each of the dimension's values.

A set and a computed value are computed on the data the report's filters of their model keep, then cached until a
variable, a filter or the data changes. Everything stays in the `IQueryable`: EF Core translates it to SQL.

## Where they are used

| Where | How | Example |
|---|---|---|
| Formulas | `[@Name]` stands for a parameter's or a computed value, in calculated measures and other variables | `[Amount] * (1 + [@Rate])` |
| Titles and texts | `{Name}` shows the value, or the values of a set | `Top {N} customers` |
| Top N of a visual | The visual's **Top** property takes a number variable | The `N` best customers |
| Filters | A set dragged onto **Filters** keeps its values, or leaves them out | The top customers only |
| Axis and legend | A set dragged onto them groups its dimension: the set's values on one side, **Others** on the other | Share of the top `N` customers |

A variable does not use itself, even through others: *A variable cannot use itself: [@A] → [@B] → [@A]*.

## In the designer

The field list's **Variables** section lists the report's parameters, and the computed values and sets of its model:
**+ Variable** opens the formula editor, ✎ and ✕ on hover edit and delete one. The editor colors the formula, points
out its problem, suggests what comes next (after `SELECT`, the dimensions; after the dimension, `IN`, `WHERE`,
`ORDER BY`...), lists the words in a tab, and previews the value or the first values of a set. Renaming a variable
renames it in the formulas using it.

Sets are dragged like dimensions onto **Axis**, **Legend** and **Filters**; a set filter has a check box to leave the
set's values out instead.

On a dashboard, a variable dragged from the field list onto a free place, or onto an empty item, shows it: a
parameter's control, a computed value, a set's values. A set dragged onto a visual filters it on its values when the
visual shows its dimension, and otherwise adds its dimension grouped into the set's values and the others
(`FieldVisuals.AddVariable`).

## On the dashboard

An empty item offers **Variable**: pick a variable, or write one. The item shows a parameter's control, a computed
value or the values of a set. The values the end user gives are saved with the report: reopening it shows the
dashboard as the user left it.

## In code

`EasyBIContext.Variables` holds the variables of the open report: a `DashboardView` loads and saves them with its
document. Set or change them yourself:

```csharp
context.Variables.Load(
[
    new ReportVariable { Name = "N", Model = "sales", Formula = "10 BETWEEN 3 AND 25", Format = "N0" },
    new ReportVariable { Name = "TopCustomers", Model = "sales", Formula = "SELECT TOP [@N] [Customer] ORDER BY [Amount] DESC" },
]);

context.Variables.SetValue("N", "15");    // invariant form, like filter values; null goes back to the formula's
```

Formulas are stored with field names, like the measures'. `VariableFormula.Read` tells what a formula defines
(`ParameterDefinition`, `ComputedDefinition`, `SetDefinition`), `VariableFormula.Check` and `Validate` check one.

```csharp
var top = new VisualDefinition
{
    Title = "Top {N} customers",
    Model = "sales",
    Type = VisualType.Bar,
    Axis = [new("Customer")],
    Values = [new("Amount")],
    Sort = new("Amount", Descending: true),
    TopVariable = "N",
};
var share = top with { Type = VisualType.Donut, Axis = [new("Customer") { Set = "TopCustomers" }], TopVariable = null, Sort = null };
var slider = new VisualDefinition { Type = VisualType.Variable, Variable = "N" };
```

A `VariableView` shows the control of a variable anywhere in your application (a toolbar...), with the same
`Context`.

!!! info "Models never see variables"
    EasyBI replaces the references before asking a model for data (`VariableContext.ResolveAsync`): a source, local or
    remote, receives plain values and plain queries. A set is queried as a table of its dimension; a field grouped by a
    set comes with the set's values (`FieldRef.SetMembers`), and a source groups on them: `QueryableSemanticModel` does
    it inside the `IQueryable`, translated to SQL by EF Core.
