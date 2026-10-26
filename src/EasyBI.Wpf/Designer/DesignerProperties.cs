using System.Collections.ObjectModel;
using System.ComponentModel;
using EasyBI.Core;

namespace EasyBI.Wpf.Designer;

/// <summary>How a <see cref="DesignerProperty"/> is edited in the properties panel.</summary>
public enum PropertyKind
{
    /// <summary>A check box (a bool).</summary>
    Toggle,

    /// <summary>A list of <see cref="DesignerProperty.Choices"/>.</summary>
    Choice,

    /// <summary>A number box (a double, null when empty).</summary>
    Number,

    /// <summary>A text box (a string).</summary>
    Text,

    /// <summary>Read-only text (a string), such as a summary.</summary>
    Info,

    /// <summary>Any object, shown with the <c>DataTemplate</c> of its type (a filter shows its editor).</summary>
    Custom,
}

/// <summary>
/// A setting shown in the designer's properties panel: its value is read and written through delegates, so that a
/// property can edit anything (a chip, the visual, a renderer option). Build one with the factory methods; the panel
/// picks the editor of its <see cref="Kind"/> (one <c>DataTemplate</c> per kind, see <see cref="ReportDesigner"/>).
/// </summary>
public sealed class DesignerProperty : ObservableObject
{
    private readonly Func<string> _label;
    private readonly Func<string>? _description;
    private readonly Func<object?> _get;
    private readonly Action<object?>? _set;
    private readonly Func<bool>? _isAvailable;
    private string? _numberText;
    private object? _numberTextValue;

    public DesignerProperty(string key, PropertyKind kind, Func<string> label, Func<object?> get, Action<object?>? set,
        IReadOnlyList<Option<object?>>? choices = null, Func<bool>? isAvailable = null, Func<string>? description = null)
    {
        Key = key;
        Kind = kind;
        _label = label;
        _description = description;
        _get = get;
        _set = set;
        _isAvailable = isAvailable;
        Choices = choices ?? [];
        // Weak subscription: properties are rebuilt with the selection, the shared string source lives on
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, (_, _) => OnPropertyChanged(string.Empty), string.Empty);
    }

    public static DesignerProperty Toggle(string key, Func<string> label, Func<bool> get, Action<bool> set, Func<bool>? isAvailable = null, Func<string>? description = null) =>
        new(key, PropertyKind.Toggle, label, () => get(), v => set(v is true), null, isAvailable, description);

    public static DesignerProperty Choice<T>(string key, Func<string> label, IEnumerable<Option<T>> choices, Func<T> get, Action<T> set, Func<bool>? isAvailable = null, Func<string>? description = null) =>
        new(key, PropertyKind.Choice, label, () => get(), v => set((T)v!), choices.Select(c => new Option<object?>(c.Value, () => c.Label)).ToArray(), isAvailable, description);

    public static DesignerProperty Number(string key, Func<string> label, Func<double?> get, Action<double?> set, Func<bool>? isAvailable = null, Func<string>? description = null) =>
        new(key, PropertyKind.Number, label, () => get(), v => set(v as double?), null, isAvailable, description);

    public static DesignerProperty Text(string key, Func<string> label, Func<string> get, Action<string> set, Func<bool>? isAvailable = null, Func<string>? description = null) =>
        new(key, PropertyKind.Text, label, () => get(), v => set(v as string ?? ""), null, isAvailable, description);

    public static DesignerProperty Info(string key, Func<string> label, Func<string?> get, Func<bool>? isAvailable = null) =>
        new(key, PropertyKind.Info, label, () => get(), null, null, isAvailable);

    public static DesignerProperty Custom(string key, Func<string> label, object content, Func<bool>? isAvailable = null) =>
        new(key, PropertyKind.Custom, label, () => content, null, null, isAvailable);

    /// <summary>Identifies the property within its section (to find, replace or remove it).</summary>
    public string Key { get; }

    public PropertyKind Kind { get; }

    public string Label => _label();

    public string? Description => _description?.Invoke();

    public IReadOnlyList<Option<object?>> Choices { get; }

    /// <summary>False hides the property (an aggregation for a calculated measure...).</summary>
    public bool IsAvailable => _isAvailable?.Invoke() ?? true;

    public bool IsReadOnly => _set is null;

    public object? Value
    {
        get => _get();
        set
        {
            if (_set is null || Equals(_get(), value)) return;
            _set(value);
            Refresh();
        }
    }

    /// <summary><see cref="Value"/> of a <see cref="PropertyKind.Toggle"/>.</summary>
    public bool IsOn
    {
        get => Value is true;
        set => Value = value;
    }

    /// <summary><see cref="Value"/> of a <see cref="PropertyKind.Choice"/> as a list item (a combo box cannot select a null value).</summary>
    public Option<object?>? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => Equals(c.Value, Value));
        set
        {
            if (value is not null) Value = value.Value;
        }
    }

    /// <summary>
    /// <see cref="Value"/> of a <see cref="PropertyKind.Number"/> as typed: an empty text is null, a text that is not a
    /// number is kept without changing the value, so that typing is never interrupted.
    /// </summary>
    public string NumberText
    {
        // The text typed stays as long as the value was not changed elsewhere
        get => _numberText is not null && Equals(_numberTextValue, Value) ? _numberText : Format(Value as double?);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) Value = null;
            else if (Parse(value) is { } number) Value = number;
            _numberText = value;
            _numberTextValue = Value;
        }
    }

    /// <summary><see cref="Value"/> of a <see cref="PropertyKind.Text"/> or <see cref="PropertyKind.Info"/>.</summary>
    public string TextValue
    {
        get => Value as string ?? "";
        set => Value = value;
    }

    /// <summary>Reads the value again (the designer calls it after each change of the visual).</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private static double? Parse(string text) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, EasyBILocalization.FormatCulture, out var number) ? number : null;

    private static string Format(double? number) => number?.ToString(EasyBILocalization.FormatCulture) ?? "";
}

/// <summary>A group of properties of the properties panel, shown under a header that can be folded.</summary>
public sealed class DesignerPropertySection : ObservableObject
{
    private readonly Func<string> _header;
    private bool _isExpanded = true;

    public DesignerPropertySection(string key, Func<string> header, IEnumerable<DesignerProperty> properties)
    {
        Key = key;
        _header = header;
        Properties = [.. properties];
        PropertyChangedEventManager.AddHandler(LocalizedStrings.EasyBI, (_, _) => OnPropertyChanged(nameof(Header)), string.Empty);
    }

    /// <summary>Identifies the section (see <see cref="DesignerPropertySections"/> for the built-in ones).</summary>
    public string Key { get; }

    public string Header => _header();

    public ObservableCollection<DesignerProperty> Properties { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>The property of a key, if any.</summary>
    public DesignerProperty? this[string key] => Properties.FirstOrDefault(p => p.Key == key);
}

/// <summary>Keys of the built-in sections and properties of the properties panel.</summary>
public static class DesignerPropertySections
{
    /// <summary>The selected field: aggregation, granularity, time, series, axis, sort.</summary>
    public const string Field = "Field";

    /// <summary>The selected measure's format: fixed style and conditional formats (tables, matrices, KPIs).</summary>
    public const string Formatting = "Formatting";

    /// <summary>The selected filter: its editor.</summary>
    public const string Filter = "Filter";

    /// <summary>The selected dimension's value labels: texts the visual shows instead of its values.</summary>
    public const string ValueLabels = "ValueLabels";

    /// <summary>The visual: title, sort, Top N.</summary>
    public const string Visual = "Visual";

    /// <summary>The tooltips of a chart: shown or not, their parts, a custom text.</summary>
    public const string Tooltips = "Tooltips";

    /// <summary>The options of the renderer drawing the visual (<see cref="Abstractions.RendererCapabilities.Options"/>).</summary>
    public const string Options = "Options";
}

/// <summary>
/// The sections the properties panel is about to show for a selection, which an application (or a renderer's
/// integration) can change: add sections, add or remove properties, reorder them.
/// </summary>
public sealed class DesignerPropertiesEventArgs(DesignerViewModel designer, object? selection, IList<DesignerPropertySection> sections) : EventArgs
{
    public DesignerViewModel Designer { get; } = designer;

    /// <summary>The selected chip (<see cref="ShelfItemViewModel"/> or <see cref="FilterItemViewModel"/>), or null when the visual itself is.</summary>
    public object? Selection { get; } = selection;

    public IList<DesignerPropertySection> Sections { get; } = sections;
}
