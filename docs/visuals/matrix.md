# Matrix

A **matrix** (`MarkType.Matrix`) crosses its axis, the **rows** (up to four dimensions), with its legend, the
**columns**, for each measure in `Values`, and adds the total of each row, of each column and of the whole. Each total
is the measure computed on all the rows it covers (an average stays an average), not a sum of cells:
`VisualPipeline.PrepareAsync` runs one query per level (`MatrixBuilder`) and hands the pivoted data to the renderer in
`RenderRequest.Matrix` (`MatrixData`). The sort, `Top` and the filters on measures choose the rows; the column totals
then only count those rows. Without columns, a matrix lists its rows and their totals with a total row. Clicking a cell
cross-filters on its row and column, a row header or total on the row, a column total on the column
(`MatrixSelection`). The built-in `MatrixRenderer` draws it in a grid styled like the table; in the designer, the
wells of a matrix are Rows, Columns and Values.

```csharp
new VisualDefinition
{
    Model = "sales",
    Mark = MarkType.Matrix,
    Axis = [new("Product.Category")],                         // rows
    Legend = new("Date", DateGrain.Year),                     // columns
    Values = [new("Amount")],
};
```
