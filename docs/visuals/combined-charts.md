# Combined charts

The cartesian chart types (`Bar`, `StackedBar`, `Line`, `Area`, `Scatter`) can be mixed in one chart, **one per
measure**: `MeasureRef.Mark` overrides the visual's type for that measure's series (with a legend, every series of
the measure keeps its type), and `MeasureRef.SecondaryAxis` draws it against a second value axis on the right. In
the designer, each measure of a chart has a type selector and a "Y2" toggle. These settings only change the drawing:
the query is the same, so changing them does not query the source again.

```csharp
new VisualDefinition
{
    Model = "sales",
    Mark = MarkType.Bar,                                      // default type of the measures
    Axis = [new("Date", DateGrain.Quarter)],
    Values =
    [
        new("Amount"),                                        // bars
        new("Margin") { Mark = MarkType.Line },               // line
        new("Count") { Mark = MarkType.Scatter, SecondaryAxis = true }, // points, right axis
    ],
};
```
