# Variables

A number of customers that a slider changes, a rate in a formula, the start of a period, the top 10 suppliers: the
variables of a report are values the end user changes on the dashboard, and every visual using them follows.

| Kind | Holds | For example |
|---|---|---|
| **Parameter** | One value: a number, a text, a date or yes/no | `N = 10`, `Rate = 0.2`, `Start = 2025-01-01` |
| **Set of values** | Values of a dimension: chosen by a measure, or checked one by one | The top `N` customers by revenue |

## Where they are used

A variable is referenced by its name: `[@N]` in filters and formulas, `{N}` in titles and texts.

| Where | How | Example |
|---|---|---|
| Top N of a visual | The visual's **Top** property takes a number parameter | The `N` best customers |
| Filters | A filter's **Variable** choice: in a set, not in a set, equal to a parameter, compared with a number, since a date, the last `N` periods | `Revenue ≥ [@Threshold]` |
| Formulas | `[@Name]` stands for the value, in any calculated measure | `[Amount] * (1 + [@Rate])` |
| Titles and texts | `{Name}` shows the value, or the values of a set | `Top {N} customers` |
| Axis and legend | A dimension chip **grouped by** a set: the set's values on one side, **Others** on the other | Share of the top `N` customers |

A set chosen by a measure is computed on the data the report's filters of its model keep, then cached until a
variable, a filter or the data changes. Its count can itself be a parameter: one slider then changes the set and every
visual using it.

## On the dashboard

An empty item offers **Variable**: pick a variable, or create one. The item shows the variable's control:

| Variable | Control |
|---|---|
| Number with a minimum and a maximum | A slider (it sets the value once the user pauses) |
| Number, text | A box, set on Enter |
| Text with choices | A list |
| Date | A date picker |
| Yes / no | A check box |
| Set chosen by a measure | Its count, editable unless a parameter gives it, and its values |
| Fixed set | **Choose the values…**, checked like a filter's, and its values |

The values are saved with the report: reopening it shows the dashboard as the user left it.

## In the designer

The field list's **Variables** section lists the parameters and the sets of the model's dimensions: **+ Parameter** and
**+ Set of values** create one, ✎ and ✕ on hover edit and delete it. The calculated measure editor offers the
parameters in its **Parameters** tab and in its suggestions.

## In code

`EasyBIContext.Variables` holds the variables of the open report: a `DashboardView` loads and saves them with its
document. Set or change them yourself:

```csharp
context.Variables.Load(
[
    new ReportVariable { Name = "N", Caption = "Number of customers", Value = "10", Minimum = 3, Maximum = 25 },
    new ReportVariable
    {
        Name = "TopCustomers",
        Caption = "Top customers",
        Kind = VariableKind.Set,
        Model = "sales",
        Field = "Customer",
        Rule = new SetRule("Amount") { CountVariable = "N" },
    },
]);

context.Variables.SetValue("N", "15");    // invariant form, like filter values
```

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
    remote, receives plain values. A field grouped by a set comes with the set's values (`FieldRef.SetMembers`), and a
    source groups on them: `QueryableSemanticModel` does it inside the `IQueryable`, translated to SQL by EF Core.
