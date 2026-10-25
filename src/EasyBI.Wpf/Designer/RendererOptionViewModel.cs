using System.ComponentModel;
using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// A <see cref="RendererOption"/> of the visual being designed, edited with a check box, a list or a number box
/// depending on its kind. Changing its value reports it to the designer, which saves it with the visual.
/// </summary>
public sealed class RendererOptionViewModel : ObservableObject
{
    private readonly Action<RendererOptionViewModel> _changed;
    private object? _value;

    public RendererOptionViewModel(string renderer, RendererOption option, object? value, Action<RendererOptionViewModel> changed)
    {
        Renderer = renderer;
        Option = option;
        _value = value;
        _changed = changed;
        Choices = option.Choices.Select(c => new Option<string>(c.Value, c.Label)).ToArray();
        // Weak subscription: options are rebuilt with the visual, the shared string source lives on
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, OnLanguageChanged, string.Empty);
    }

    /// <summary>Where the value is saved: the name of the renderer that declares the option, or the id of the visual type.</summary>
    public string Renderer { get; }

    public RendererOption Option { get; }

    public string Label => Option.Label();

    public string? Description => Option.Description?.Invoke();

    public bool IsToggle => Option.Kind == RendererOptionKind.Toggle;

    public bool IsChoice => Option.Kind == RendererOptionKind.Choice;

    public bool IsNumber => Option.Kind == RendererOptionKind.Number;

    /// <summary>The current value: a bool, a choice's value or a double, by kind.</summary>
    public object? Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value)) return;
            Sync(value);
            _changed(this);
        }
    }

    public bool IsOn
    {
        get => _value as bool? ?? false;
        set => Value = value;
    }

    public IReadOnlyList<Option<string>> Choices { get; }

    public Option<string>? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => c.Value == _value as string);
        set => Value = value?.Value;
    }

    /// <summary>The number as typed, in the EasyBI format culture; text that is not a number is ignored.</summary>
    public string NumberText
    {
        get => Convert.ToDouble(_value ?? 0d, CultureInfo.InvariantCulture).ToString(EasyBILocalization.FormatCulture);
        set
        {
            if (!double.TryParse(value, NumberStyles.Float, EasyBILocalization.FormatCulture, out var number))
            {
                OnPropertyChanged();
                return;
            }
            Value = Math.Clamp(number, Option.Minimum ?? double.MinValue, Option.Maximum ?? double.MaxValue);
            OnPropertyChanged(); // shows the clamped value
        }
    }

    /// <summary>Shows <paramref name="value"/> without reporting a change (the visual already has it).</summary>
    internal void Sync(object? value)
    {
        _value = value;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(NumberText));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(NumberText));
    }
}
