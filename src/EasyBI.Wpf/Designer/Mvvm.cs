using System.ComponentModel;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace EasyBI.Wpf.Designer;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand<T>(Action<T> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute((T)parameter!);
}

/// <summary>
/// A choice shown in a list. Its label is computed, so it follows the EasyBI language:
/// the option notifies <see cref="Label"/> when <see cref="EasyBI.Core.EasyBILocalization.Culture"/> changes.
/// </summary>
public class Option<T> : ObservableObject
{
    private readonly Func<string> _label;

    public Option(T value, Func<string> label)
    {
        Value = value;
        _label = label;
        // Weak subscription: options may be shorter-lived than the shared string source
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
    }

    public T Value { get; }

    public string Label => _label();

    // What screen readers and UI Automation announce for an item of a combo box
    public override string ToString() => Label;

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Label));
}
