using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>A color of a format: one of the theme's (<see cref="ThemeColors"/>), a custom #RRGGBB, or none when allowed.</summary>
public sealed class ColorChoiceViewModel : ObservableObject
{
    private const string Custom = "custom";
    private readonly Action _changed;
    private Option<string?> _selected;
    private string _hex = "#";

    public ColorChoiceViewModel(string? color, Action changed, bool allowsNone = true)
    {
        _changed = changed;
        Options =
        [
            .. allowsNone ? [new Option<string?>(null, () => Strings.ColorNone)] : Array.Empty<Option<string?>>(),
            new(ThemeColors.Accent, () => Strings.ColorAccent),
            new(ThemeColors.Success, () => Strings.ColorSuccess),
            new(ThemeColors.Warning, () => Strings.ColorWarning),
            new(ThemeColors.Error, () => Strings.ColorError),
            new(ThemeColors.Muted, () => Strings.ColorMuted),
            new(Custom, () => Strings.ColorCustom),
        ];
        _selected = Options.FirstOrDefault(o => o.Value == color && o.Value != Custom)
            ?? (color is null ? Options[0] : Options.Last());
        if (_selected.Value == Custom) _hex = color!;
    }

    public IReadOnlyList<Option<string?>> Options { get; }

    public Option<string?> Selected
    {
        get => _selected;
        set
        {
            if (value is null || !SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(IsCustom));
            _changed();
        }
    }

    /// <summary>The custom color, as typed (#RRGGBB).</summary>
    public string Hex
    {
        get => _hex;
        set { if (SetProperty(ref _hex, value ?? "") && IsCustom) _changed(); }
    }

    public bool IsCustom => _selected.Value == Custom;

    /// <summary>The color as a format stores it; an unreadable custom color is none.</summary>
    public string? Value => IsCustom ? (IsHex(_hex) ? _hex : null) : _selected.Value;

    private static bool IsHex(string text) =>
        text.Length is 7 or 9 && text[0] == '#' && int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
}

/// <summary>A rule of a format: a comparison with numbers, and the style of the values that match it.</summary>
public sealed class FormatRuleViewModel : ObservableObject
{
    private readonly Action _changed;
    private Option<FilterOperator> _condition;
    private string _number;
    private string _secondNumber;
    private bool _isBold;
    private bool _showOnChart;

    public FormatRuleViewModel(FormatRule rule, Action changed)
    {
        _changed = changed;
        _condition = Conditions.FirstOrDefault(c => c.Value == rule.Operator) ?? Conditions[0];
        (_number, _secondNumber) = (ValueFormatViewModel.Show(rule.Value), ValueFormatViewModel.Show(rule.Maximum));
        _isBold = rule.IsBold;
        _showOnChart = rule.ShowOnChart;
        Color = new ColorChoiceViewModel(rule.Color, changed);
        Background = new ColorChoiceViewModel(rule.Background, changed);
    }

    public static IReadOnlyList<Option<FilterOperator>> Conditions { get; } =
    [
        new(FilterOperator.GreaterThan, () => Strings.ConditionGreaterThan),
        new(FilterOperator.GreaterThanOrEqual, () => Strings.ConditionGreaterThanOrEqual),
        new(FilterOperator.LessThan, () => Strings.ConditionLessThan),
        new(FilterOperator.LessThanOrEqual, () => Strings.ConditionLessThanOrEqual),
        new(FilterOperator.Range, () => Strings.ConditionRange),
    ];

    public Option<FilterOperator> Condition
    {
        get => _condition;
        set
        {
            if (value is null || !SetProperty(ref _condition, value)) return;
            OnPropertyChanged(nameof(ShowSecondNumber));
            _changed();
        }
    }

    public string Number
    {
        get => _number;
        set { if (SetProperty(ref _number, value ?? "")) _changed(); }
    }

    public string SecondNumber
    {
        get => _secondNumber;
        set { if (SetProperty(ref _secondNumber, value ?? "")) _changed(); }
    }

    public bool ShowSecondNumber => Condition.Value == FilterOperator.Range;

    public bool IsBold
    {
        get => _isBold;
        set { if (SetProperty(ref _isBold, value)) _changed(); }
    }

    /// <summary>Shades the rule's zone on the value axis of a chart.</summary>
    public bool ShowOnChart
    {
        get => _showOnChart;
        set { if (SetProperty(ref _showOnChart, value)) _changed(); }
    }

    public ColorChoiceViewModel Color { get; }

    public ColorChoiceViewModel Background { get; }

    public FormatRule ToRule() => new()
    {
        Operator = Condition.Value,
        Value = ValueFormatViewModel.Parse(Number),
        Maximum = ShowSecondNumber ? ValueFormatViewModel.Parse(SecondNumber) : null,
        Color = Color.Value,
        Background = Background.Value,
        IsBold = IsBold,
        ShowOnChart = ShowOnChart,
    };
}

/// <summary>
/// The editor of a measure's <see cref="ValueFormat"/> in the designer's properties panel: fixed style, rules,
/// color scale, data bars and icons. Every change calls back the chip, which rebuilds the visual.
/// </summary>
public sealed class ValueFormatViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isBold;
    private bool _isItalic;
    private Option<TextAlign?> _alignment;
    private bool _hasScale;
    private bool _hasBar;
    private bool _hasIcons;
    private Option<IconKind> _iconStyle;
    private string _low;
    private string _high;

    public ValueFormatViewModel(ValueFormat? format, Action changed)
    {
        _changed = changed;
        format ??= new ValueFormat();
        Color = new ColorChoiceViewModel(format.Color, changed);
        _isBold = format.IsBold;
        _isItalic = format.IsItalic;
        _alignment = Alignments.First(a => a.Value == format.Alignment);
        foreach (var rule in format.Rules) Rules.Add(new FormatRuleViewModel(rule, changed));
        Rules.CollectionChanged += (_, _) => _changed();

        var scale = format.Scale ?? new ColorScale();
        _hasScale = format.Scale is not null;
        ScaleMinimum = new ColorChoiceViewModel(scale.Minimum, changed, allowsNone: false);
        ScaleMiddle = new ColorChoiceViewModel(scale.Middle, changed);
        ScaleMaximum = new ColorChoiceViewModel(scale.Maximum, changed, allowsNone: false);

        var bar = format.Bar ?? new DataBar();
        _hasBar = format.Bar is not null;
        BarColor = new ColorChoiceViewModel(bar.Color, changed, allowsNone: false);
        BarNegativeColor = new ColorChoiceViewModel(bar.NegativeColor, changed, allowsNone: false);

        _hasIcons = format.Icons is not null;
        _iconStyle = IconKinds.First(k => k.Value == (format.Icons?.Kind ?? Abstractions.IconKind.Arrows));
        (_low, _high) = (Show(format.Icons?.Low), Show(format.Icons?.High));

        AddRuleCommand = new RelayCommand(() => Rules.Add(new FormatRuleViewModel(new FormatRule(), _changed)), () => true);
        RemoveRuleCommand = new RelayCommand<FormatRuleViewModel>(rule => Rules.Remove(rule));
    }

    public static IReadOnlyList<Option<TextAlign?>> Alignments { get; } =
    [
        new(null, () => Strings.AlignDefault),
        new(TextAlign.Left, () => Strings.AlignLeft),
        new(TextAlign.Center, () => Strings.AlignCenter),
        new(TextAlign.Right, () => Strings.AlignRight),
    ];

    public static IReadOnlyList<Option<IconKind>> IconKinds { get; } =
    [
        new(Abstractions.IconKind.Arrows, () => Strings.IconArrows),
        new(Abstractions.IconKind.Triangles, () => Strings.IconTriangles),
        new(Abstractions.IconKind.Circles, () => Strings.IconCircles),
    ];

    public ColorChoiceViewModel Color { get; }

    public bool IsBold
    {
        get => _isBold;
        set { if (SetProperty(ref _isBold, value)) _changed(); }
    }

    public bool IsItalic
    {
        get => _isItalic;
        set { if (SetProperty(ref _isItalic, value)) _changed(); }
    }

    public Option<TextAlign?> Alignment
    {
        get => _alignment;
        set { if (value is not null && SetProperty(ref _alignment, value)) _changed(); }
    }

    public ObservableCollection<FormatRuleViewModel> Rules { get; } = [];

    public ICommand AddRuleCommand { get; }

    /// <summary>Removes the rule given as parameter.</summary>
    public ICommand RemoveRuleCommand { get; }

    public bool HasScale
    {
        get => _hasScale;
        set { if (SetProperty(ref _hasScale, value)) _changed(); }
    }

    public ColorChoiceViewModel ScaleMinimum { get; }

    public ColorChoiceViewModel ScaleMiddle { get; }

    public ColorChoiceViewModel ScaleMaximum { get; }

    public bool HasBar
    {
        get => _hasBar;
        set { if (SetProperty(ref _hasBar, value)) _changed(); }
    }

    public ColorChoiceViewModel BarColor { get; }

    public ColorChoiceViewModel BarNegativeColor { get; }

    public bool HasIcons
    {
        get => _hasIcons;
        set { if (SetProperty(ref _hasIcons, value)) _changed(); }
    }

    /// <summary>The kind of icons (arrows, triangles, circles).</summary>
    public Option<IconKind> IconStyle
    {
        get => _iconStyle;
        set { if (value is not null && SetProperty(ref _iconStyle, value)) _changed(); }
    }

    /// <summary>Values under it get the low icon; empty: a third of the way between the smallest and the largest value.</summary>
    public string Low
    {
        get => _low;
        set { if (SetProperty(ref _low, value ?? "")) _changed(); }
    }

    /// <summary>Values from it on get the high icon; empty: two thirds of the way.</summary>
    public string High
    {
        get => _high;
        set { if (SetProperty(ref _high, value ?? "")) _changed(); }
    }

    /// <summary>The format, or null when nothing is set (the measure keeps the default look).</summary>
    public ValueFormat? ToFormat()
    {
        var format = new ValueFormat
        {
            Color = Color.Value,
            IsBold = IsBold,
            IsItalic = IsItalic,
            Alignment = Alignment.Value,
            Rules = [.. Rules.Select(r => r.ToRule())],
            Scale = HasScale ? new ColorScale { Minimum = ScaleMinimum.Value!, Middle = ScaleMiddle.Value, Maximum = ScaleMaximum.Value! } : null,
            Bar = HasBar ? new DataBar { Color = BarColor.Value!, NegativeColor = BarNegativeColor.Value! } : null,
            Icons = HasIcons ? new IconSet { Kind = IconStyle.Value, Low = Parse(Low), High = Parse(High) } : null,
        };
        var isEmpty = format is { Color: null, IsBold: false, IsItalic: false, Alignment: null, Rules.Count: 0, Scale: null, Bar: null, Icons: null };
        return isEmpty ? null : format;
    }

    // Numbers are typed and shown in the format culture
    internal static double? Parse(string text) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, EasyBILocalization.FormatCulture, out var number) ? number : null;

    internal static string Show(double? number) => number?.ToString("G", EasyBILocalization.FormatCulture) ?? "";
}
