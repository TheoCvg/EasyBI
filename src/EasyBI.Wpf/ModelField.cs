using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf;

/// <summary>
/// A field of a semantic model: what a <see cref="FieldList"/> drags (to a designer shelf, to a dashboard...).
/// <paramref name="Measure"/> is the report's definition of a measure: one of its calculated measures, or its
/// redefinition of a measure of the model.
/// </summary>
public sealed record ModelField(ISemanticModel Model, FieldDescriptor Field, CalculatedMeasure? Measure = null)
{
    public string Caption => Field.Caption;

    public bool IsMeasure => Field.Kind == FieldKind.Measure;

    /// <summary>Whether the user can edit the measure: any measure, the model's ones being redefined in the report.</summary>
    public bool IsEditable => IsMeasure;

    /// <summary>A calculated measure created in the report (ƒx), which the user can delete.</summary>
    public bool IsReportMeasure => Measure is not null && !IsDeclared;

    /// <summary>A measure of the model that the report redefines, which the user can restore.</summary>
    public bool IsRedefined => Measure is not null && IsDeclared;

    /// <summary>What the editor starts from: the report's definition, or the model's measure as declared.</summary>
    public CalculatedMeasure? Definition =>
        Measure ?? (IsMeasure ? MeasureContext.Definition(Model.Metadata.GetField(Field.Name), Model.Metadata.Name) : null);

    private bool IsDeclared => Model.Metadata.Fields.Any(f => f.Name == Field.Name);
}

/// <summary>
/// A variable of the report in a <see cref="FieldList"/>, with a <paramref name="Summary"/> of what it holds: what a
/// field list drags for a set of values, which the designer takes like a dimension.
/// </summary>
public sealed record VariableItem(ReportVariable Variable, string Summary)
{
    public string Caption => Variable.Name;

    public VariableKind Kind => VariableFormula.Definition(Variable).Kind;

    public bool IsSet => Kind == VariableKind.Set;

    /// <summary>@ for a parameter, ƒ for a computed value, { } for a set.</summary>
    public string Glyph => Kind switch { VariableKind.Set => "{ }", VariableKind.Computed => "ƒ", _ => "@" };
}

/// <summary>
/// Visuals made from fields dropped by the user: a first visual for a field, and the visual a field added to an
/// existing one gives. Pure functions, so that an application can reuse or replace them.
/// </summary>
public static class FieldVisuals
{
    /// <summary>
    /// A visual showing a field: a KPI for a measure; for a dimension, the model's first measure by that dimension
    /// (a line by month for a date, bars otherwise), or a table of its values when the model has no measure.
    /// </summary>
    public static VisualDefinition Create(ISemanticModel model, FieldDescriptor field)
    {
        var visual = new VisualDefinition { Model = model.Metadata.Name };
        if (field.Kind == FieldKind.Measure)
            return visual with { Title = field.Caption, Type = VisualType.Kpi, Values = [new MeasureRef(field.Name)] };

        var axis = AxisOf(field);
        if (model.Metadata.Measures.FirstOrDefault() is not { } measure)
            return visual with { Title = field.Caption, Type = VisualType.Table, Axis = [axis] };
        return visual with
        {
            Title = Strings.Format(Strings.VisualTitleBy, measure.Caption, field.Caption),
            Type = field.DataType == DataType.Date ? VisualType.Line : VisualType.Bar,
            Axis = [axis],
            Values = [new MeasureRef(measure.Name)],
        };
    }

    /// <summary>
    /// The visual with a field added, or null when it does not take it (a text or an image, another model, a field it
    /// already shows, no room left in the wells of its type in <paramref name="types"/>, EasyBI's types when null). A
    /// measure joins the values; a dimension goes on the axis, then in the legend, then on the axis again. A dimension
    /// dropped on a KPI breaks it down: it becomes bars.
    /// </summary>
    public static VisualDefinition? AddField(VisualDefinition visual, ISemanticModel model, FieldDescriptor field, VisualTypeCatalog? types = null)
    {
        if (visual.Type.IsContent() || visual.Model != model.Metadata.Name) return null;
        var definition = (types ?? VisualTypeCatalog.Default).Get(visual.Type);
        if (field.Kind == FieldKind.Measure)
        {
            if (!HasRoom(definition.Values, visual.Values.Count) || visual.Values.Any(v => v.Measure == field.Name)) return null;
            return visual with { Values = [.. visual.Values, new MeasureRef(field.Name)] };
        }

        if (visual.Axis.Any(a => a.Field == field.Name) || visual.Legend?.Field == field.Name) return null;
        return WithAxis(visual, definition, AxisOf(field));
    }

    /// <summary>A visual showing a variable: a parameter's control, a computed value or a set's values.</summary>
    public static VisualDefinition Create(ReportVariable variable) => new() { Type = VisualType.Variable, Variable = variable.Name };

    /// <summary>
    /// The visual with a set of values added, or null when it does not take it (not a set, another model, no room). On
    /// a visual showing the set's dimension, the set filters it on its values; otherwise the dimension joins the visual
    /// as a field does, grouped into the set's values and the others.
    /// </summary>
    public static VisualDefinition? AddVariable(VisualDefinition visual, ReportVariable variable, VisualTypeCatalog? types = null)
    {
        if (visual.Type.IsContent() || visual.Model != variable.Model || VariableFormula.Definition(variable) is not SetDefinition set) return null;
        if (visual.Axis.Any(a => a.Field == set.Field) || visual.Legend?.Field == set.Field)
        {
            var values = new[] { "[@" + variable.Name + "]" };
            if (visual.Filters.Any(f => f.Field == set.Field && f.Values.SequenceEqual(values))) return null;
            return visual with { Filters = [.. visual.Filters, new FilterSpec(set.Field, FilterOperator.In, values)] };
        }
        return WithAxis(visual, (types ?? VisualTypeCatalog.Default).Get(visual.Type), new FieldRef(set.Field) { Set = variable.Name });
    }

    private static VisualDefinition? WithAxis(VisualDefinition visual, VisualTypeDefinition definition, FieldRef axis)
    {
        if (visual.Type == VisualType.Kpi || visual.Type == VisualType.Gauge) return visual with { Type = VisualType.Bar, Axis = [axis], Gauge = null };
        if (definition.Axis.IsUsed && visual.Axis.Count == 0) return visual with { Axis = [axis] };
        if (definition.Legend.IsUsed && visual.Legend is null) return visual with { Legend = axis };
        return HasRoom(definition.Axis, visual.Axis.Count) ? visual with { Axis = [.. visual.Axis, axis] } : null;
    }

    private static bool HasRoom(VisualWell well, int count) => well.IsUsed && count < (well.MaxFields ?? int.MaxValue);

    // Dates are read by month unless the user picks another granularity
    private static FieldRef AxisOf(FieldDescriptor field) => new(field.Name, field.DataType == DataType.Date ? DateGrain.Month : DateGrain.None);
}
