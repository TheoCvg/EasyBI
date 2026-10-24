# Getting started

## Installation

```
dotnet add package EasyBI.Wpf
dotnet add package EasyBI.Renderers.LiveCharts2   # and/or EasyBI.Renderers.OxyPlot
dotnet add package EasyBI.Themes.WpfUi            # optional: if the application uses WPF UI or Material Design
```

`EasyBI.Wpf` brings `EasyBI.Core` and `EasyBI.Abstractions`. Every public WPF type is available
under a single XAML namespace:

```xml
xmlns:bi="https://github.com/TheoCvg/EasyBI"
```

## Declaring a model

The developer declares what can be analyzed: the dimensions (analysis axes) and the measures (aggregated values) of
an `IQueryable`, then registers the model and one or more renderers in an `EasyBIContext`.

```csharp
var model = SemanticModel.For(dbContext.Sales, "sales", "Sales")
    .Dimension(s => s.Date, "Date")
    .Dimension(s => s.Region, "Region")
    .Dimension(s => s.Product.Category, "Category")
    .Measure(s => s.Amount, "Revenue", format: "#,##0 €")
    .Count("Number of sales");

Context = EasyBIContext.Create()
    .AddModel(model)
    .UseRenderer(new LiveCharts2Renderer())
    .UseRenderer(new OxyPlotRenderer());
```

With EF Core, prefer a context factory: each query opens its own `DbContext`, and the visuals of a
dashboard run in parallel. On a shared `IQueryable` (other than LINQ to objects), queries run one
after another, because a `DbContext` does not support concurrent operations.

```csharp
var model = SemanticModel.For(() => dbFactory.CreateDbContext(), db => db.Sales, "sales", "Sales")
    .Dimension(s => s.Region, "Region") /* ... */;
```

Each visual becomes one database query (filters, grouping, aggregates, sort and top in SQL): only the aggregated
rows reach the application, whatever the number of rows in the table.

## Showing the designer and a visual

```xml
<bi:ReportDesigner Context="{Binding Context}" />                             <!-- design -->
<bi:VisualView Context="{Binding Context}" Definition="{Binding Visual}" />   <!-- display -->
```

The [designer](guide/designer.md) lets end users build a visual by dragging fields; a `VisualView` shows a saved
visual. To let them arrange several visuals, use a [dashboard](dashboard/index.md).

## Refreshing the data

Visuals cache their last result and query again only when their query changes. When the data behind a model changes
(new rows, another source), tell them, from any thread:

```csharp
Context.RefreshData("sales");   // the visuals of this model; RefreshData() for all of them
```

## Running the sample

```
dotnet run --project samples/EasyBI.Sample
```

The demo starts in your system language (English or French) and has a language picker in its toolbar. Its data
picker switches the same sales between memory (20,000 rows) and a SQLite database of 1 or 5 million rows, generated
once in `%LOCALAPPDATA%\EasyBI.Sample`; the toolbar shows how long the last refresh took.
