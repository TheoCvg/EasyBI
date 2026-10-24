using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// The end user's calculated measures of the open report, shared by the field lists, the designers and the visuals:
/// adding, changing or removing one raises <see cref="Changed"/>. Saved with the report
/// (<see cref="ReportDefinition.Measures"/>). A measure named like a measure of its model redefines it (see
/// <see cref="CalculatedMeasure"/>); removing it restores the model's.
/// </summary>
public sealed class MeasureContext
{
    private readonly List<CalculatedMeasure> _measures = [];

    public event EventHandler? Changed;

    public IReadOnlyList<CalculatedMeasure> Measures => _measures;

    /// <summary>A new name, unique and distinct from any field name of a model.</summary>
    public static string NewName() => "calc_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>The measure as a field of its model: a calculated measure, with no aggregation to choose.</summary>
    public static FieldDescriptor ToField(CalculatedMeasure measure) =>
        new(measure.Name, measure.Caption, FieldKind.Measure, DataType.Number, Format: measure.Format, IsCalculated: true);

    /// <summary>
    /// A measure of a model as a report redefines it: renamed and reformatted, and calculated by the report when its
    /// formula is more than the measure itself. An empty caption keeps the model's.
    /// </summary>
    public static FieldDescriptor ToField(CalculatedMeasure measure, FieldDescriptor declared)
    {
        var caption = measure.Caption.Length > 0 ? measure.Caption : declared.Caption;
        return MeasureFormula.IsOnly(measure.Formula, declared.Name)
            ? declared with { Caption = caption, Format = measure.Format }
            : ToField(measure with { Caption = caption });
    }

    /// <summary>
    /// A measure of a model as an editable definition, named like it: its formula, or the measure itself
    /// (<c>[Amount]</c>), which keeps the model's computation.
    /// </summary>
    public static CalculatedMeasure Definition(FieldDescriptor declared, string model) => new()
    {
        Name = declared.Name,
        Caption = declared.Caption,
        Model = model,
        Formula = declared.Formula ?? "[" + declared.Name.Replace("]", "]]") + "]",
        Format = declared.Format,
    };

    public IEnumerable<CalculatedMeasure> For(string model) => _measures.Where(m => m.Model == model);

    /// <summary>
    /// The measures a user can pick in a model: the model's own, as the report redefines them, then the report's
    /// calculated measures.
    /// </summary>
    public IEnumerable<FieldDescriptor> MeasuresOf(ModelMetadata metadata) =>
        metadata.Measures.Select(f => Find(metadata.Name, f.Name) is { } redefined ? ToField(redefined, f) : f)
            .Concat(For(metadata.Name).Where(m => !metadata.Fields.Any(f => f.Name == m.Name)).Select(ToField));

    /// <summary>A field of a model, as the report redefines it, or one of the report's calculated measures.</summary>
    public FieldDescriptor GetField(ModelMetadata metadata, string name)
    {
        var declared = metadata.Fields.FirstOrDefault(f => f.Name == name);
        return (Find(metadata.Name, name), declared) switch
        {
            ({ } measure, { Kind: FieldKind.Measure }) => ToField(measure, declared),
            ({ } measure, null) => ToField(measure),
            _ => declared ?? metadata.GetField(name),
        };
    }

    public CalculatedMeasure? Find(string model, string name) => _measures.FirstOrDefault(m => m.Model == model && m.Name == name);

    /// <summary>
    /// The definitions a visual needs: the calculated measures among its values, and those they use. With the model's
    /// <paramref name="metadata"/>, those its formula measures use too.
    /// </summary>
    public IReadOnlyList<CalculatedMeasure> UsedBy(VisualDefinition visual, ModelMetadata? metadata = null) =>
        Needed(visual.Values.Select(v => v.Measure), For(visual.Model), metadata);

    /// <summary>The report's measures of a model whose formula uses the measure <paramref name="name"/>.</summary>
    public IReadOnlyList<CalculatedMeasure> Using(string model, string name) =>
        For(model).Where(m => m.Name != name && MeasureFormula.References(m.Formula).Contains(name)).ToArray();

    /// <summary>
    /// The calculated measures among <paramref name="measures"/> (those of one model) that the measures named
    /// <paramref name="names"/> need: their own, and those their formulas use, through the formulas of the model
    /// (<paramref name="metadata"/>) too. In the order of <paramref name="measures"/>.
    /// </summary>
    public static IReadOnlyList<CalculatedMeasure> Needed(IEnumerable<string> names, IEnumerable<CalculatedMeasure> measures, ModelMetadata? metadata = null)
    {
        var all = measures.ToArray();
        var needed = new HashSet<CalculatedMeasure>();
        var visited = new HashSet<(string, bool)>();
        // model: the model's measure is meant (a redefinition reads it under its own name)
        void Visit(string name, bool model)
        {
            if (!visited.Add((name, model))) return;
            if (!model && all.FirstOrDefault(m => m.Name == name) is { } measure)
            {
                needed.Add(measure);
                foreach (var used in MeasureFormula.References(measure.Formula)) Visit(used, model: used == name);
            }
            else if (metadata?.Fields.FirstOrDefault(f => f.Name == name)?.Formula is { } formula)
            {
                foreach (var used in MeasureFormula.References(formula)) Visit(used, model: false);
            }
        }
        foreach (var name in names) Visit(name, model: false);
        return all.Where(needed.Contains).ToArray();
    }

    /// <summary>Adds a measure, or replaces the one with the same name in the same model.</summary>
    public void Set(CalculatedMeasure measure)
    {
        var index = _measures.FindIndex(m => m.Model == measure.Model && m.Name == measure.Name);
        if (index >= 0) _measures[index] = measure;
        else _measures.Add(measure);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the measures with this name, in any model.</summary>
    public void Remove(string name)
    {
        if (_measures.RemoveAll(m => m.Name == name) > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes a measure of a model; for a redefinition, the model's measure is restored.</summary>
    public void Remove(string model, string name)
    {
        if (_measures.RemoveAll(m => m.Model == model && m.Name == name) > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces every measure (opening a report).</summary>
    public void Load(IEnumerable<CalculatedMeasure> measures)
    {
        _measures.Clear();
        _measures.AddRange(measures);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
