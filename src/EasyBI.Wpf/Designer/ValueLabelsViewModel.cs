using System.Collections.ObjectModel;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// The texts a visual shows instead of the values of a dimension (Île-de-France shown as IDF): each value of the field,
/// with the text typed for it. Edited in the properties panel of the dimension's chip.
/// </summary>
public sealed class ValueLabelsViewModel : ObservableObject
{
    private readonly ShelfItemViewModel _chip;
    private bool _isLoading = true;

    internal ValueLabelsViewModel(ShelfItemViewModel chip, ISemanticModel model)
    {
        _chip = chip;
        _ = LoadAsync(model);
    }

    /// <summary>The values of the field, each with its text.</summary>
    public ObservableCollection<ValueLabelViewModel> Values { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private async Task LoadAsync(ISemanticModel model)
    {
        try
        {
            var field = _chip.ToFieldRef();
            foreach (var value in await model.GetDistinctValuesAsync(_chip.Field.Name))
            {
                var text = field.Labels?.GetValueOrDefault(InvariantValue.Format(value) ?? "") ?? "";
                Values.Add(new ValueLabelViewModel(value, ValueFormatter.Display(value), text, _chip.SetLabel));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>A value of a dimension and the text the visual shows instead (empty: the value itself).</summary>
public sealed class ValueLabelViewModel(object? value, string original, string text, Action<object?, string> changed) : ObservableObject
{
    private string _text = text;

    public object? Value { get; } = value;

    /// <summary>The value as shown without a label.</summary>
    public string Original { get; } = original;

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? "")) changed(Value, _text);
        }
    }
}
