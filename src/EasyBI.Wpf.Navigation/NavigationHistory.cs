namespace EasyBI.Wpf.Navigation;

/// <summary>A report on the path the user followed: what it received, and how the user left it.</summary>
/// <param name="ReportId">The id of the report, which the application's loader knows.</param>
/// <param name="Caption">What the breadcrumb shows (the report's title).</param>
/// <param name="Arguments">What the report received when it was opened.</param>
public sealed record NavigationEntry(string ReportId, string Caption, NavigationArguments Arguments)
{
    /// <summary>The report as the user left it (filters, values, layout), to restore going back; null for the current one.</summary>
    public object? State { get; init; }
}

/// <summary>
/// The path the user followed from report to report: the reports left behind, then the current one. It knows nothing
/// of WPF: the host saves the state of a report it leaves and restores it going back.
/// </summary>
public sealed class NavigationHistory
{
    private readonly List<NavigationEntry> _path = [];

    /// <summary>Raised when the path changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The reports from the first to the current one.</summary>
    public IReadOnlyList<NavigationEntry> Path => _path;

    public NavigationEntry? Current => _path.Count == 0 ? null : _path[^1];

    public bool CanGoBack => _path.Count > 1;

    /// <summary>Starts a new path with this report.</summary>
    public void Start(NavigationEntry entry)
    {
        _path.Clear();
        _path.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens <paramref name="entry"/>, leaving the current report in the state <paramref name="leftState"/>.</summary>
    public void Push(NavigationEntry entry, object? leftState)
    {
        if (_path.Count > 0) _path[^1] = _path[^1] with { State = leftState };
        _path.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Goes back to the report at <paramref name="index"/> of <see cref="Path"/>, forgetting those after it; returns it,
    /// with the state it was left in, or null when the index is not one of a previous report.
    /// </summary>
    public NavigationEntry? GoBack(int index)
    {
        if (index < 0 || index >= _path.Count - 1) return null;
        var entry = _path[index];
        _path.RemoveRange(index, _path.Count - index);
        _path.Add(entry with { State = null });
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>Goes back to the previous report (see <see cref="GoBack(int)"/>).</summary>
    public NavigationEntry? GoBack() => GoBack(_path.Count - 2);
}
