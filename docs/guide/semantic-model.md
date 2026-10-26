# Semantic model

The semantic model is what you, the developer, declare: which fields of your data users can analyze, under which
names. Users never see your classes or your tables, only the model.

<div class="grid" markdown>

```csharp
SemanticModel.For(sales, "sales", "Sales")
  .Dimension(s => s.Date, "Date")
  .Dimension(s => s.Region, "Region")
  .Dimension(s => s.Channel, "Channel")
  .Dimension(s => s.Product.Category,
             "Category")
  .Dimension(s => s.Product.Name,
             "Product")
  .Measure(s => s.Amount, "Revenue",
           format: "#,##0 €")
  .Measure(s => s.Margin, "Margin",
           format: "#,##0 €")
  .Measure(s => s.Quantity, "Quantity")
  .Count("Number of sales")
  .Ratio(s => s.Margin, s => s.Amount,
         "Margin rate", "MarginRate",
         format: "P1");
```

![The fields of the sample's model in the designer: dimensions, then measures](../images/semantic-model/fields.png#only-light){ width="230" }
![The fields of the sample's model in the designer: dimensions, then measures](../images/semantic-model/fields-dark.png#only-dark){ width="230" }

</div>

## Dimensions

A dimension is what users **group or filter by**: a region, a channel, a date, a product category.

```csharp
.Dimension(s => s.Region, "Region")
.Dimension(s => s.Product.Category, "Category")          // (1)!
.Dimension(s => s.Customer.Country, "Country", name: "Country") // (2)!
```

1. Navigation properties work: with EF Core, they become joins in SQL.
2. The name is the member path by default (`Customer.Country`); give your own to keep reports stable when the class changes.

What users can do with a dimension depends on its type:

| Type | Examples | Users can |
|---|---|---|
| Text | `string`, `enum` | Group, filter on values, search |
| Number | `int`, `decimal`, `double` | Group, filter on values or ranges (≥, <, between) |
| Date | `DateTime`, `DateOnly`, `DateTimeOffset` | Group by year, quarter, month or day; filter on periods (this year, last 12 months, between) |
| Boolean | `bool` | Group, filter on yes / no |

## Measures

A measure is what users **add up**: a revenue, a quantity, a number of sales.

```csharp
.Measure(s => s.Amount, "Revenue", format: "#,##0 €")              // summed by default
.Measure(s => s.UnitPrice, "Unit price", Aggregation.Average, "C2") // (1)!
.Count("Number of sales")                                          // (2)!
```

1. The default aggregation; users can switch to `Sum`, `Average`, `Min`, `Max` or `Count` in the designer.
2. Counts rows. Its name is `Count` unless you give another.

**Formats** are .NET format strings, applied in the user's culture: `N0` (`12,345`), `#,##0 €` (`12,345 €`), `C2`
(currency of the culture), `P1` (`12.3%`). Users can override them per visual, with [conditional
formatting](../visuals/conditional-formatting.md) too.

### Calculated measures

A ratio cannot be summed: the margin rate of a region is its total margin divided by its total revenue, not the
average of its sales' rates. Calculated measures are computed per group, inside the query:

| Method | For | Example |
|---|---|---|
| `Ratio` | A sum divided by a sum | Margin rate, average basket |
| `Calculated` | Any aggregate expression | `g => g.Sum(s => s.Amount) - g.Sum(s => s.Cost)` |
| `Formula` | A formula users can read and rewrite | `SUM([Amount]; [Channel] = "Web") / [Amount]` |

Users create their own too. See [Calculated measures](calculated-measures.md).

## Names and captions

Every model and field has two labels:

| | Seen by | Example | Must |
|---|---|---|---|
| **Name** | Reports (JSON), your code | `sales`, `Amount`, `Product.Category` | Stay stable: saved reports refer to it |
| **Caption** | Users | `Sales`, `Revenue`, `Category` | Read well; can follow the language |

!!! warning "Renaming a property breaks saved reports"
    A field's name defaults to its member path. Before renaming a property of your class, give the field its old name
    explicitly (`name: "Amount"`), or saved reports will show the field as missing (`EasyBIContext.Validate` lists such
    problems).

Captions can follow the user's language: call `.Localize(MyStrings.ResourceManager)` and every caption becomes a
resource key. See [Localization](../customization/localization.md).

## Data sources

=== "In memory"

    ```csharp
    SemanticModel.For(sales, "sales", "Sales")   // any IEnumerable<T>
    ```

    Queries run in LINQ to objects, in parallel.

=== "EF Core (recommended)"

    ```csharp
    SemanticModel.For(() => dbFactory.CreateDbContext(), db => db.Sales, "sales", "Sales")
    ```

    Each query opens its own `DbContext` (from an `IDbContextFactory`) and disposes it: the visuals of a dashboard query
    in parallel.

=== "Shared IQueryable"

    ```csharp
    SemanticModel.For(dbContext.Sales, "sales", "Sales")
    ```

    One `DbContext` for every query: they run one after another, since a `DbContext` does not support concurrent
    operations.

Every visual becomes **one query**, with its filters, grouping, aggregates, sort and Top N inside the `IQueryable`. With
EF Core, that is one SQL statement: only the aggregated rows reach the application, whatever the size of the table.

??? example "The SQL of a visual"
    *Revenue by region, top 3*, on SQL Server, is roughly:

    ```sql
    SELECT TOP(3) [s].[Region], SUM([s].[Amount])
    FROM [Sales] AS [s]
    GROUP BY [s].[Region]
    ORDER BY SUM([s].[Amount]) DESC
    ```

    Keep dimension and measure expressions translatable by your provider: a method call EF Core cannot translate fails
    when the visual queries.

## Several models

Add as many models as you need; the designer lets users pick one, and each visual uses a single model.

```csharp
EasyBIContext.Create()
    .AddModel(salesModel)
    .AddModel(stockModel)
    .UseRenderer(new LiveCharts2Renderer());
```

## When the data changes

Visuals cache their last result and query again only when their own query changes. When the rows change (an import,
another data source), tell them, from any thread:

```csharp
context.RefreshData("sales");   // the visuals of this model
context.RefreshData();          // every visual
```

## Your own model

`SemanticModel.For` covers any `IQueryable`. For another kind of source (a web API, an OLAP cube...), implement
`ISemanticModel`: its `Metadata` (the fields), `ExecuteAsync` (an `AnalyticQuery` in, a `ResultSet` out) and
`GetDistinctValuesAsync` (the values filters offer).
