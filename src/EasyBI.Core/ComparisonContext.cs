using EasyBI.Abstractions;

namespace EasyBI.Core;

/// <summary>
/// The periods of comparison and the presentations the end user added to the open report, shared by the designers and
/// the visuals: adding, changing or removing one raises <see cref="Changed"/>. Saved with the report
/// (<see cref="ReportDefinition.ComparisonPeriods"/>, <see cref="ReportDefinition.ComparisonOutputs"/>).
/// </summary>
public sealed class ComparisonContext
{
    private readonly List<ReportComparisonPeriod> _periods = [];
    private readonly List<ReportComparisonOutput> _outputs = [];

    public event EventHandler? Changed;

    public IReadOnlyList<ReportComparisonPeriod> Periods => _periods;

    public IReadOnlyList<ReportComparisonOutput> Outputs => _outputs;

    /// <summary>A new id for an entry of the report: <see cref="TimeIds.ReportPrefix"/> and a unique part.</summary>
    public static string NewId() => TimeIds.ReportPrefix + Guid.NewGuid().ToString("N").Substring(0, 12);

    /// <summary>The application's <paramref name="catalog"/> with the entries of the report (see <see cref="TimeCatalog.With"/>).</summary>
    public TimeCatalog Apply(TimeCatalog catalog) => catalog.With(_periods, _outputs);

    /// <summary>The entries of the report that the measures of <paramref name="visual"/>, shown or in its tooltip, use.</summary>
    public (IReadOnlyList<ReportComparisonPeriod> Periods, IReadOnlyList<ReportComparisonOutput> Outputs) UsedBy(VisualDefinition visual)
    {
        var times = visual.Values.Concat(visual.Tooltip?.Values ?? []).Select(v => v.Time).OfType<MeasureTime>().Where(t => t.Period is not null).ToArray();
        return (
            _periods.Where(p => times.Any(t => t.Period == p.Id)).ToArray(),
            _outputs.Where(o => times.Any(t => t.Show == o.Id)).ToArray());
    }

    /// <summary>Adds a period, or replaces the one with the same id.</summary>
    public void Set(ReportComparisonPeriod period)
    {
        Set(_periods, period, p => p.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a presentation, or replaces the one with the same id.</summary>
    public void Set(ReportComparisonOutput output)
    {
        Set(_outputs, output, o => o.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the period or the presentation with this id: the visuals using it show empty values.</summary>
    public void Remove(string id)
    {
        if (_periods.RemoveAll(p => p.Id == id) + _outputs.RemoveAll(o => o.Id == id) > 0) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces every entry (opening a report).</summary>
    public void Load(IEnumerable<ReportComparisonPeriod> periods, IEnumerable<ReportComparisonOutput> outputs)
    {
        _periods.Clear();
        _periods.AddRange(periods);
        _outputs.Clear();
        _outputs.AddRange(outputs);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void Set<T>(List<T> items, T item, Func<T, string> id)
    {
        var index = items.FindIndex(i => id(i) == id(item));
        if (index >= 0) items[index] = item;
        else items.Add(item);
    }
}
