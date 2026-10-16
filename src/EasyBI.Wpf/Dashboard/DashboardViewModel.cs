using System.Collections.ObjectModel;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Designer;

namespace EasyBI.Wpf.Dashboard;

/// <summary>Where a dragged slot would land relative to the slot under the pointer.</summary>
public enum DropHint { None, Left, Right, Top, Bottom }

/// <summary>
/// Live state of a <see cref="DashboardPanel"/>. Every change goes through a <see cref="ReportLayout"/>
/// operation on the immutable report, then the row and slot view-models are synchronized by id:
/// they are reused, so visuals that did not move keep their rendering and do not query again.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private const int MaxHistory = 50;

    private readonly Dictionary<string, DashboardRowViewModel> _rows = [];
    private readonly Dictionary<string, DashboardSlotViewModel> _slots = [];
    private readonly LinkedList<ReportDefinition> _undo = new();
    private readonly Stack<ReportDefinition> _redo = new();
    private ReportDefinition _report = ReportLayout.Normalize(new ReportDefinition());

    public DashboardViewModel() => Sync();

    /// <summary>Raised after a change made through the dashboard (not after <see cref="Load"/>).</summary>
    public event EventHandler? ReportChanged;

    public ReportDefinition Report => _report;

    public ObservableCollection<DashboardRowViewModel> Rows { get; } = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Shows another report; the undo history starts over.</summary>
    public void Load(ReportDefinition report)
    {
        _report = ReportLayout.Normalize(report);
        _undo.Clear();
        _redo.Clear();
        Sync();
    }

    public void Apply(Func<ReportDefinition, ReportDefinition> operation)
    {
        // Reports are immutable: the history is simply the previous reports
        _undo.AddLast(_report);
        if (_undo.Count > MaxHistory) _undo.RemoveFirst();
        _redo.Clear();
        Show(operation(_report));
    }

    public void Undo()
    {
        if (_undo.Last is not { } previous) return;
        _undo.RemoveLast();
        _redo.Push(_report);
        Show(previous.Value);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next)) return;
        _undo.AddLast(_report);
        Show(next);
    }

    private void Show(ReportDefinition report)
    {
        _report = report;
        Sync();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        ReportChanged?.Invoke(this, EventArgs.Empty);
    }

    public DashboardSlotViewModel? FindSlot(string slotId) => _slots.GetValueOrDefault(slotId);

    private void Sync()
    {
        var layoutRows = _report.Layout!.Rows;
        Reconcile(Rows, layoutRows.Select((row, index) =>
        {
            var vm = _rows.TryGetValue(row.Id, out var existing) ? existing : _rows[row.Id] = new DashboardRowViewModel(row.Id);
            vm.Update(row, isFirst: index == 0);
            Reconcile(vm.Slots, row.Slots.Select((slot, i) =>
            {
                var slotVm = _slots.TryGetValue(slot.Id, out var s) ? s : _slots[slot.Id] = new DashboardSlotViewModel(slot.Id);
                slotVm.Update(vm, slot, ReportLayout.VisualOf(_report, slot), isFirst: i == 0, isLast: i == row.Slots.Count - 1);
                return slotVm;
            }).ToList());
            return vm;
        }).ToList());

        // Forget view-models that left the layout
        foreach (var id in _rows.Keys.Except(layoutRows.Select(r => r.Id)).ToList()) _rows.Remove(id);
        foreach (var id in _slots.Keys.Except(layoutRows.SelectMany(r => r.Slots).Select(s => s.Id)).ToList()) _slots.Remove(id);
    }

    /// <summary>Makes <paramref name="target"/> equal to <paramref name="items"/> with minimal moves, keeping item instances.</summary>
    private static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> items) where T : class
    {
        for (var i = target.Count - 1; i >= 0; i--)
            if (!items.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < items.Count; i++)
        {
            var current = target.IndexOf(items[i]);
            if (current < 0) target.Insert(i, items[i]);
            else if (current != i) target.Move(current, i);
        }
    }
}

public sealed class DashboardRowViewModel(string id) : ObservableObject
{
    private double _height = LayoutRow.DefaultHeight;
    private RowOverflow _overflow;
    private bool _isFirst;

    public string Id { get; } = id;

    public ObservableCollection<DashboardSlotViewModel> Slots { get; } = [];

    /// <summary>Row height; also set live while the user drags the row splitter.</summary>
    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, value);
    }

    public RowOverflow Overflow
    {
        get => _overflow;
        private set => SetProperty(ref _overflow, value);
    }

    public bool IsFirst
    {
        get => _isFirst;
        private set => SetProperty(ref _isFirst, value);
    }

    internal void Update(LayoutRow row, bool isFirst)
    {
        Height = row.Height;
        Overflow = row.Overflow;
        IsFirst = isFirst;
    }
}

public sealed class DashboardSlotViewModel(string id) : ObservableObject
{
    private DashboardRowViewModel? _row;
    private VisualDefinition? _visual;
    private double _width = LayoutSlot.DefaultWidth;
    private bool _isFirst;
    private bool _canResize;
    private DropHint _dropHint;

    public string Id { get; } = id;

    public DashboardRowViewModel Row => _row!;

    public VisualDefinition? Visual
    {
        get => _visual;
        private set
        {
            if (SetProperty(ref _visual, value)) OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => _visual is null;

    /// <summary>Preferred width; also set live while the user drags a slot splitter.</summary>
    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }

    public bool IsFirst
    {
        get => _isFirst;
        private set => SetProperty(ref _isFirst, value);
    }

    /// <summary>False for the last slot of a Fit row: its right edge is the row's edge.</summary>
    public bool CanResize
    {
        get => _canResize;
        private set => SetProperty(ref _canResize, value);
    }

    public DropHint DropHint
    {
        get => _dropHint;
        set => SetProperty(ref _dropHint, value);
    }

    /// <summary>Rendered width, used as the starting point when resizing.</summary>
    public double ActualWidth { get; set; }

    internal void Update(DashboardRowViewModel row, LayoutSlot slot, VisualDefinition? visual, bool isFirst, bool isLast)
    {
        if (!ReferenceEquals(_row, row))
        {
            _row = row;
            OnPropertyChanged(nameof(Row));
        }
        Visual = visual;
        Width = slot.Width;
        IsFirst = isFirst;
        CanResize = !(isLast && row.Overflow == RowOverflow.Fit);
    }
}
