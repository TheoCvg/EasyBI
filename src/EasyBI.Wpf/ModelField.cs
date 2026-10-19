using EasyBI.Abstractions;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// A field of a semantic model: what a <see cref="FieldList"/> drags (to a designer shelf, to a dashboard...).
/// <paramref name="Measure"/> is the definition of a calculated measure of the report, which the user can edit.
/// </summary>
public sealed record ModelField(ISemanticModel Model, FieldDescriptor Field, CalculatedMeasure? Measure = null)
{
    public string Caption => Field.Caption;

    public bool IsMeasure => Field.Kind == FieldKind.Measure;

    public bool IsEditable => Measure is not null;
}

/// <summary>
/// Visuals made from fields dropped by the user: a first visual for a field, and the visual a field added to an
/// existing one gives. Pure functions, so that an application can reuse or replace them.
/// </summary>
public static class FieldVisuals
{
    private const int MaxAxisFields = 3;

    /// <summary>
    /// A visual showing a field: a KPI for a measure; for a dimension, the model's first measure by that dimension
    /// (a line by month for a date, bars otherwise), or a table of its values when the model has no measure.
    /// </summary>
    public static VisualDefinition Create(ISemanticModel model, FieldDescriptor field)
    {
        var visual = new VisualDefinition { Model = model.Metadata.Name };
        if (field.Kind == FieldKind.Measure)
            return visual with { Title = field.Caption, Mark = MarkType.Kpi, Values = [new MeasureRef(field.Name)] };

        var axis = AxisOf(field);
        if (model.Metadata.Measures.FirstOrDefault() is not { } measure)
            return visual with { Title = field.Caption, Mark = MarkType.Table, Axis = [axis] };
        return visual with
        {
            Title = Strings.Format(Strings.VisualTitleBy, measure.Caption, field.Caption),
            Mark = field.DataType == DataType.Date ? MarkType.Line : MarkType.Bar,
            Axis = [axis],
            Values = [new MeasureRef(measure.Name)],
        };
    }

    /// <summary>
    /// The visual with a field added, or null when it does not take it (a text or an image, another model, a field it
    /// already shows, no room left). A measure joins the values; a dimension goes on the axis, then in the legend,
    /// then on the axis again (up to three fields). A dimension dropped on a KPI breaks it down: it becomes bars.
    /// </summary>
    public static VisualDefinition? AddField(VisualDefinition visual, ISemanticModel model, FieldDescriptor field)
    {
        if (visual.Mark is MarkType.Text or MarkType.Image || visual.Model != model.Metadata.Name) return null;
        if (field.Kind == FieldKind.Measure)
        {
            if (visual.Values.Any(v => v.Measure == field.Name) || (visual.Mark == MarkType.Pie && visual.Values.Count > 0)) return null;
            return visual with { Values = [.. visual.Values, new MeasureRef(field.Name)] };
        }

        if (visual.Axis.Any(a => a.Field == field.Name) || visual.Legend?.Field == field.Name) return null;
        var axis = AxisOf(field);
        return visual.Mark switch
        {
            MarkType.Kpi => visual with { Mark = MarkType.Bar, Axis = [axis] },
            MarkType.Pie => visual.Axis.Count == 0 ? visual with { Axis = [axis] } : null,
            MarkType.Table => visual.Axis.Count < MaxAxisFields ? visual with { Axis = [.. visual.Axis, axis] } : null,
            _ when visual.Axis.Count == 0 => visual with { Axis = [axis] },
            _ when visual.Legend is null => visual with { Legend = axis },
            _ when visual.Axis.Count < MaxAxisFields => visual with { Axis = [.. visual.Axis, axis] },
            _ => null,
        };
    }

    // Dates are read by month unless the user picks another granularity
    private static FieldRef AxisOf(FieldDescriptor field) => new(field.Name, field.DataType == DataType.Date ? DateGrain.Month : DateGrain.None);
}
