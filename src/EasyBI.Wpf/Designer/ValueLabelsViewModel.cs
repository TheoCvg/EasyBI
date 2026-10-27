using System.Collections.ObjectModel;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// The texts a visual shows instead of some values of a dimension (Île-de-France shown as IDF), added one at a time:
/// the labels already given, and a value to pick among the others (a dimension can have many) with its text. Edited in
/// the properties panel of the dimension's chip.
/// </summary>
public sealed class ValueLabelsViewModel : ObservableObject
{
    private readonly ShelfItemViewModel _chip;
    private readonly List<ValueOption> _values = [];
    private ValueOption? _newValue;
    private string _newText = "";
    private bool _isLoading = true;

    internal ValueLabelsViewModel(ShelfItemViewModel chip, ISemanticModel model)
    {
        _chip = chip;
        AddCommand = new RelayCommand(Add, () => _newValue is not null && !string.IsNullOrWhiteSpace(_newText));
        RemoveCommand = new RelayCommand<ValueLabelViewModel>(Remove);
        Loaded = LoadAsync(model);
    }

    /// <summary>Completes once the values of the field are listed.</summary>
    public Task Loaded { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>The labels given so far, each editable.</summary>
    public ObservableCollection<ValueLabelViewModel> Labels { get; } = [];

    /// <summary>The values without a label, to pick the next one from.</summary>
    public ObservableCollection<ValueOption> AvailableValues { get; } = [];

    /// <summary>The value to label next.</summary>
    public ValueOption? NewValue
    {
        get => _newValue;
        set
        {
            if (SetProperty(ref _newValue, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>The text to show instead of <see cref="NewValue"/>.</summary>
    public string NewText
    {
        get => _newText;
        set
        {
            if (SetProperty(ref _newText, value ?? "")) CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Gives <see cref="NewValue"/> the text <see cref="NewText"/>.</summary>
    public ICommand AddCommand { get; }

    /// <summary>Removes a label (the parameter): the value shows as itself again.</summary>
    public ICommand RemoveCommand { get; }

    private async Task LoadAsync(ISemanticModel model)
    {
        try
        {
            var labels = _chip.Labels ?? new Dictionary<string, string>();
            foreach (var value in (await model.GetDistinctValuesAsync(new DistinctValuesRequest(_chip.Field.Name))).Values)
            {
                var option = new ValueOption(value, ValueFormatter.Display(value));
                _values.Add(option);
                if (labels.TryGetValue(InvariantValue.Format(value) ?? "", out var text))
                    Labels.Add(new ValueLabelViewModel(option, text, _chip.SetLabel));
                else
                    AvailableValues.Add(option);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Add()
    {
        if (_newValue is not { } option || string.IsNullOrWhiteSpace(_newText)) return;
        _chip.SetLabel(option.Value, _newText);
        Labels.Add(new ValueLabelViewModel(option, _newText.Trim(), _chip.SetLabel));
        AvailableValues.Remove(option);
        NewValue = null;
        NewText = "";
    }

    private void Remove(ValueLabelViewModel label)
    {
        _chip.SetLabel(label.Option.Value, null);
        Labels.Remove(label);
        // Back among the values to pick, at its place
        var index = _values.IndexOf(label.Option);
        AvailableValues.Insert(AvailableValues.Count(o => _values.IndexOf(o) < index), label.Option);
    }
}

/// <summary>A value of a dimension as a list shows it.</summary>
public sealed record ValueOption(object? Value, string Label)
{
    // An editable combo box shows and searches this text
    public override string ToString() => Label;
}

/// <summary>A label given to a value of a dimension: the value, and the text the visual shows instead.</summary>
public sealed class ValueLabelViewModel(ValueOption option, string text, Action<object?, string?> changed) : ObservableObject
{
    private string _text = text;

    public ValueOption Option { get; } = option;

    /// <summary>The value as shown without a label.</summary>
    public string Original => Option.Label;

    /// <summary>The text shown instead; a blank text shows the value itself.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? "")) changed(Option.Value, _text);
        }
    }
}
