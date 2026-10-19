using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// The end user's calculated measures of the open report, shared by the field lists, the designers and the visuals:
/// adding, changing or removing one raises <see cref="Changed"/>. Saved with the report
/// (<see cref="ReportDefinition.Measures"/>).
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

    public IEnumerable<CalculatedMeasure> For(string model) => _measures.Where(m => m.Model == model);

    public CalculatedMeasure? Find(string model, string name) => _measures.FirstOrDefault(m => m.Model == model && m.Name == name);

    /// <summary>The definitions a visual needs: the calculated measures among its values.</summary>
    public IReadOnlyList<CalculatedMeasure> UsedBy(VisualDefinition visual) =>
        visual.Values.Select(v => Find(visual.Model, v.Measure)).OfType<CalculatedMeasure>().Distinct().ToArray();

    /// <summary>Adds a measure, or replaces the one with the same name.</summary>
    public void Set(CalculatedMeasure measure)
    {
        var index = _measures.FindIndex(m => m.Name == measure.Name);
        if (index >= 0) _measures[index] = measure;
        else _measures.Add(measure);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string name)
    {
        if (_measures.RemoveAll(m => m.Name == name) > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces every measure (opening a report).</summary>
    public void Load(IEnumerable<CalculatedMeasure> measures)
    {
        _measures.Clear();
        _measures.AddRange(measures);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
