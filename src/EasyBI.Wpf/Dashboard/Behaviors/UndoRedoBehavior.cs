using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace EasyBI.Wpf.Dashboard.Behaviors;

/// <summary>
/// Undo and redo of every change made through the dashboard (Ctrl+Z, Ctrl+Y, or <see cref="ApplicationCommands.Undo"/>
/// and <see cref="ApplicationCommands.Redo"/>). The history starts over when another document is loaded.
/// </summary>
public sealed class UndoRedoBehavior : DashboardBehavior, INotifyPropertyChanged
{
    private readonly LinkedList<DashboardDocument> _undo = new();
    private readonly Stack<DashboardDocument> _redo = new();
    private readonly List<CommandBinding> _bindings = [];
    private bool _restoring;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Number of changes that can be undone.</summary>
    public int MaxHistory { get; set; } = 50;

    public bool CanUndo => CanEdit && _undo.Count > 0;

    public bool CanRedo => CanEdit && _redo.Count > 0;

    protected override void OnAttach()
    {
        View.Changed += OnChanged;
        View.DocumentLoaded += OnDocumentLoaded;
        View.PreviewMouseDown += OnMouseDown;
        _bindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = CanUndo));
        _bindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = CanRedo));
        View.CommandBindings.AddRange(_bindings);
    }

    protected override void OnDetach()
    {
        View.Changed -= OnChanged;
        View.DocumentLoaded -= OnDocumentLoaded;
        View.PreviewMouseDown -= OnMouseDown;
        foreach (var binding in _bindings) View.CommandBindings.Remove(binding);
        _bindings.Clear();
    }

    /// <summary>Undoes the last change.</summary>
    public void Undo()
    {
        if (!CanUndo) return;
        var previous = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(View.Capture());
        Restore(previous);
    }

    /// <summary>Redoes the last undone change.</summary>
    public void Redo()
    {
        if (!CanRedo) return;
        _undo.AddLast(View.Capture());
        Restore(_redo.Pop());
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnHistoryChanged();
    }

    private void Restore(DashboardDocument document)
    {
        _restoring = true;
        try
        {
            View.Restore(document);
        }
        finally
        {
            _restoring = false;
        }
        OnHistoryChanged();
    }

    private void OnChanged(object? sender, DashboardChangedEventArgs e)
    {
        if (_restoring) return;
        // Documents are immutable: the history is simply the previous documents
        _undo.AddLast(e.Before);
        while (_undo.Count > Math.Max(1, MaxHistory)) _undo.RemoveFirst();
        _redo.Clear();
        OnHistoryChanged();
    }

    private void OnDocumentLoaded(object? sender, EventArgs e) => Clear();

    // Clicking a chart does not move the keyboard focus: bring it into the view, so that Ctrl+Z works.
    // Done on the press, before the click: later, it would close a menu the click just opened.
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!View.IsKeyboardFocusWithin) View.Focus();
    }

    private void OnHistoryChanged()
    {
        Notify(nameof(CanUndo));
        Notify(nameof(CanRedo));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
