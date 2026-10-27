using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Edits a variable of the report (<see cref="EasyBIContext.Variables"/>): a parameter (its type, value, bounds, choices
/// and format) or a set of values of a dimension (a fixed list checked like a filter's, or the first or last values by
/// a measure). The name is chosen when the variable is created: visuals and formulas reference it.
/// <see cref="VariableWindow"/> shows it.
/// </summary>
public sealed class VariableEditor : ObservableObject
{
    private readonly EasyBIContext _context;
    private readonly string? _previousName;
    private Option<VariableKind> _kind;
    private string _name;
    private string _caption;
    private Option<DataType> _dataType;
    private string _value = "";
    private DateTime? _dateValue;
    private bool _booleanValue;
    private string _minimum = "";
    private string _maximum = "";
    private string _step = "";
    private string _format;
    private string _choices;
    private Option<ISemanticModel>? _model;
    private Option<string>? _field;
    private bool _isRule;
    private Option<string>? _measure;
    private bool _bottom;
    private string _count;
    private Option<string?> _countVariable;
    private FilterItemViewModel? _members;
    private IReadOnlyList<Option<string>> _fields = [];
    private IReadOnlyList<Option<string>> _measures = [];

    /// <param name="context">Where the variable is saved, and the models of a set.</param>
    /// <param name="variable">The variable to edit, or null for a new one.</param>
    /// <param name="kind">The kind of a new variable.</param>
    /// <param name="model">The model of a new set (the first one by default).</param>
    public VariableEditor(EasyBIContext context, ReportVariable? variable = null, VariableKind kind = VariableKind.Parameter, ISemanticModel? model = null)
    {
        _context = context;
        _previousName = variable?.Name;
        Kinds =
        [
            new(VariableKind.Parameter, () => Strings.VariableKindParameter),
            new(VariableKind.Set, () => Strings.VariableKindSet),
        ];
        DataTypes =
        [
            new(DataType.Number, () => Strings.DataTypeNumber),
            new(DataType.Text, () => Strings.DataTypeText),
            new(DataType.Date, () => Strings.DataTypeDate),
            new(DataType.Boolean, () => Strings.DataTypeBoolean),
        ];
        Models = [.. context.Models.Select(m => new Option<ISemanticModel>(m, () => m.Metadata.Caption))];
        Orders =
        [
            new(false, () => Strings.SetOrderTop),
            new(true, () => Strings.SetOrderBottom),
        ];
        CountVariables =
        [
            new(null, () => Strings.SetCountFixed),
            .. context.Variables.Parameters.Where(p => p.DataType == DataType.Number && p.Name != variable?.Name)
                .Select(p => new Option<string?>(p.Name, () => p.DisplayName)),
        ];

        var v = variable ?? new ReportVariable { Kind = kind, Name = context.Variables.NewName(kind == VariableKind.Set ? "Set" : "Parameter") };
        _kind = Kinds.First(k => k.Value == v.Kind);
        _name = v.Name;
        _caption = v.Caption;
        _dataType = DataTypes.FirstOrDefault(t => t.Value == v.DataType) ?? DataTypes[0];
        _format = v.Format ?? "";
        _choices = string.Join(Environment.NewLine, v.Choices ?? []);
        _minimum = Show(v.Minimum);
        _maximum = Show(v.Maximum);
        _step = Show(v.Step);
        switch (v.DataType)
        {
            case DataType.Date: _dateValue = DateTime.TryParse(v.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : null; break;
            case DataType.Boolean: _booleanValue = v.Value == bool.TrueString; break;
            case DataType.Number: _value = double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Show(number) : ""; break;
            default: _value = v.Value ?? ""; break;
        }
        _isRule = v.Rule is not null || variable is null;
        _bottom = v.Rule?.Bottom ?? false;
        _count = (v.Rule?.Count ?? 10).ToString(EasyBILocalization.FormatCulture);
        _countVariable = CountVariables.FirstOrDefault(c => c.Value == v.Rule?.CountVariable) ?? CountVariables[0];
        _model = Models.FirstOrDefault(m => m.Value.Metadata.Name == v.Model) ?? Models.FirstOrDefault(m => m.Value == model) ?? Models.FirstOrDefault();
        LoadModel(v.Field, v.Rule?.Measure, v.Members);
    }

    public bool IsNew => _previousName is null;

    public IReadOnlyList<Option<VariableKind>> Kinds { get; }

    public Option<VariableKind> Kind
    {
        get => _kind;
        set
        {
            if (value is null || !SetProperty(ref _kind, value)) return;
            OnPropertyChanged(nameof(IsParameter));
            OnPropertyChanged(nameof(IsSet));
            Changed();
        }
    }

    public bool IsParameter => Kind.Value == VariableKind.Parameter;

    public bool IsSet => Kind.Value == VariableKind.Set;

    /// <summary>What references the variable: <c>[@Name]</c> in filters and formulas, <c>{Name}</c> in texts.</summary>
    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value?.Trim() ?? "")) Changed(); }
    }

    public string Caption
    {
        get => _caption;
        set { if (SetProperty(ref _caption, value ?? "")) Changed(); }
    }

    /// <summary>How the variable is referenced, for the end user.</summary>
    public string ReferenceHint => Strings.Format(Strings.VariableReferenceHint, VariableReferences.Reference(Name), "{" + Name + "}");

    // --- Parameter ---

    public IReadOnlyList<Option<DataType>> DataTypes { get; }

    public Option<DataType> ParameterType
    {
        get => _dataType;
        set
        {
            if (value is null || !SetProperty(ref _dataType, value)) return;
            OnPropertyChanged(nameof(IsNumber));
            OnPropertyChanged(nameof(IsText));
            OnPropertyChanged(nameof(IsDate));
            OnPropertyChanged(nameof(IsBoolean));
            OnPropertyChanged(nameof(HasTextValue));
            OnPropertyChanged(nameof(HasFormat));
            Changed();
        }
    }

    public bool IsNumber => ParameterType.Value == DataType.Number;

    public bool IsText => ParameterType.Value == DataType.Text;

    public bool IsDate => ParameterType.Value == DataType.Date;

    public bool IsBoolean => ParameterType.Value == DataType.Boolean;

    /// <summary>A number or a text: typed in a box.</summary>
    public bool HasTextValue => IsNumber || IsText;

    public bool HasFormat => IsNumber || IsDate;

    /// <summary>The value of a number (in <see cref="EasyBILocalization.FormatCulture"/>) or of a text, as typed.</summary>
    public string Value
    {
        get => _value;
        set { if (SetProperty(ref _value, value ?? "")) Changed(); }
    }

    public DateTime? DateValue
    {
        get => _dateValue;
        set { if (SetProperty(ref _dateValue, value)) Changed(); }
    }

    public bool BooleanValue
    {
        get => _booleanValue;
        set { if (SetProperty(ref _booleanValue, value)) Changed(); }
    }

    /// <summary>With <see cref="Maximum"/>, the dashboard shows a slider.</summary>
    public string Minimum
    {
        get => _minimum;
        set { if (SetProperty(ref _minimum, value ?? "")) Changed(); }
    }

    public string Maximum
    {
        get => _maximum;
        set { if (SetProperty(ref _maximum, value ?? "")) Changed(); }
    }

    public string Step
    {
        get => _step;
        set { if (SetProperty(ref _step, value ?? "")) Changed(); }
    }

    /// <summary>.NET format of the value where it is shown ("N0", "P1", "d"...); empty for the default.</summary>
    public string Format
    {
        get => _format;
        set { if (SetProperty(ref _format, value ?? "")) Changed(); }
    }

    /// <summary>The values a text can take, one per line; empty for any text.</summary>
    public string Choices
    {
        get => _choices;
        set { if (SetProperty(ref _choices, value ?? "")) Changed(); }
    }

    // --- Set ---

    public IReadOnlyList<Option<ISemanticModel>> Models { get; }

    public Option<ISemanticModel>? Model
    {
        get => _model;
        set
        {
            if (value is null || !SetProperty(ref _model, value)) return;
            LoadModel(null, null, null);
            Changed();
        }
    }

    /// <summary>The dimensions of the <see cref="Model"/>.</summary>
    public IReadOnlyList<Option<string>> Fields
    {
        get => _fields;
        private set => SetProperty(ref _fields, value);
    }

    public Option<string>? Field
    {
        get => _field;
        set
        {
            if (value is null || !SetProperty(ref _field, value)) return;
            LoadMembers(null);
            Changed();
        }
    }

    /// <summary>The measures of the <see cref="Model"/>, the report's included, which a rule orders the values by.</summary>
    public IReadOnlyList<Option<string>> Measures
    {
        get => _measures;
        private set => SetProperty(ref _measures, value);
    }

    /// <summary>The values are chosen by a measure (true) or checked one by one (false).</summary>
    public bool IsRule
    {
        get => _isRule;
        set
        {
            if (!SetProperty(ref _isRule, value)) return;
            OnPropertyChanged(nameof(IsFixed));
            Changed();
        }
    }

    public bool IsFixed
    {
        get => !IsRule;
        set => IsRule = !value;
    }

    public Option<string>? Measure
    {
        get => _measure;
        set { if (value is not null && SetProperty(ref _measure, value)) Changed(); }
    }

    public IReadOnlyList<Option<bool>> Orders { get; }

    /// <summary>The first values (highest measure) or the last ones.</summary>
    public Option<bool> Order
    {
        get => Orders.First(o => o.Value == _bottom);
        set
        {
            if (value is null || !SetProperty(ref _bottom, value.Value)) return;
            OnPropertyChanged();
            Changed();
        }
    }

    /// <summary>How many values a rule keeps, as typed.</summary>
    public string Count
    {
        get => _count;
        set { if (SetProperty(ref _count, value ?? "")) Changed(); }
    }

    /// <summary>A fixed count, or the number parameters that can give it.</summary>
    public IReadOnlyList<Option<string?>> CountVariables { get; }

    public Option<string?> CountVariable
    {
        get => _countVariable;
        set
        {
            if (value is null || !SetProperty(ref _countVariable, value)) return;
            OnPropertyChanged(nameof(HasFixedCount));
            Changed();
        }
    }

    public bool HasFixedCount => CountVariable.Value is null;

    /// <summary>The values of a fixed set, checked like a filter's.</summary>
    public FilterItemViewModel? Members
    {
        get => _members;
        private set => SetProperty(ref _members, value);
    }

    // --- Saving ---

    /// <summary>What prevents saving the variable, worded for the end user; null when it can be saved.</summary>
    public string? Error
    {
        get
        {
            if (!ReportVariable.IsValidName(Name)) return Strings.VariableInvalidName;
            if (Name != _previousName && _context.Variables.Find(Name) is not null) return Strings.Format(Strings.VariableNameTaken, Name);
            if (IsParameter)
            {
                if (IsNumber && Value.Trim().Length > 0 && Parse(Value) is null) return Strings.VariableInvalidNumber;
                if (IsNumber && new[] { Minimum, Maximum, Step }.Any(t => t.Trim().Length > 0 && Parse(t) is null)) return Strings.VariableInvalidNumber;
                return null;
            }
            if (Model is null || Field is null) return Strings.VariableChooseField;
            if (IsRule && Measure is null) return Strings.VariableChooseMeasure;
            if (IsRule && HasFixedCount && (!int.TryParse(Count, NumberStyles.Integer, EasyBILocalization.FormatCulture, out var count) || count < 0))
                return Strings.VariableInvalidCount;
            return null;
        }
    }

    public bool CanSave => Error is null;

    /// <summary>The variable as edited; null while <see cref="Error"/> prevents it.</summary>
    public ReportVariable? ToVariable()
    {
        if (!CanSave) return null;
        var variable = new ReportVariable { Name = Name, Caption = Caption.Trim(), Kind = Kind.Value };
        if (IsParameter)
        {
            return variable with
            {
                DataType = ParameterType.Value,
                Value = ParameterType.Value switch
                {
                    DataType.Number => Parse(Value)?.ToString("R", CultureInfo.InvariantCulture),
                    DataType.Date => DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DataType.Boolean => BooleanValue ? bool.TrueString : bool.FalseString,
                    _ => Value,
                },
                Minimum = IsNumber ? Parse(Minimum) : null,
                Maximum = IsNumber ? Parse(Maximum) : null,
                Step = IsNumber ? Parse(Step) : null,
                Format = HasFormat && Format.Trim().Length > 0 ? Format.Trim() : null,
                Choices = IsText && Choices.Split('\n').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray() is { Length: > 0 } choices ? choices : null,
            };
        }
        return variable with
        {
            Model = Model!.Value.Metadata.Name,
            Field = Field!.Value,
            Rule = IsRule
                ? new SetRule(Measure!.Value)
                {
                    Count = HasFixedCount ? int.Parse(Count, NumberStyles.Integer, EasyBILocalization.FormatCulture) : 10,
                    CountVariable = CountVariable.Value,
                    Bottom = _bottom,
                }
                : null,
            Members = IsRule ? null : Members is { IsLoading: false } members ? VariableView.CheckedValues(members) : _context.Variables.Find(_previousName ?? "")?.Members ?? [],
        };
    }

    /// <summary>Adds or replaces the variable in the context; returns it, or null when it cannot be saved.</summary>
    public ReportVariable? Save()
    {
        if (ToVariable() is not { } variable) return null;
        _context.Variables.Set(variable, _previousName);
        return variable;
    }

    private void LoadModel(string? field, string? measure, IReadOnlyList<string?>? members)
    {
        var metadata = Model?.Value.Metadata;
        Fields = metadata is null ? [] : [.. metadata.Dimensions.Select(f => new Option<string>(f.Name, () => f.Caption))];
        Measures = Model is null ? [] : [.. _context.MeasuresOf(Model.Value).Select(f => new Option<string>(f.Name, () => f.Caption))];
        _field = Fields.FirstOrDefault(f => f.Value == field) ?? Fields.FirstOrDefault();
        _measure = Measures.FirstOrDefault(m => m.Value == measure) ?? Measures.FirstOrDefault();
        OnPropertyChanged(nameof(Field));
        OnPropertyChanged(nameof(Measure));
        LoadMembers(members);
    }

    private void LoadMembers(IReadOnlyList<string?>? members) =>
        Members = Model is { } model && Field is { } field
            ? new FilterItemViewModel(model.Value.Metadata.GetField(field.Value), model.Value, Changed, members ?? [])
            : null;

    private void Changed()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ReferenceHint));
    }

    private static double? Parse(string text) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, EasyBILocalization.FormatCulture, out var number) ? number : null;

    private static string Show(double? number) => number?.ToString("G", EasyBILocalization.FormatCulture) ?? "";
}
