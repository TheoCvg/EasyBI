using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Creates or changes one of the report's calculated measures, or redefines a measure of the model in the report: a
/// name, a formula typed with the fields' captions (<c>[Marge] / [Chiffre d'affaires]</c>,
/// <c>SUM([CA]; [Région] = "Nord")</c>) and a format. The formula is checked as it is typed, and a valid one shows
/// its total as a preview. One preview query runs at a time and is never cancelled: when it ends, the latest formula
/// is computed if it changed meanwhile, so typing skips intermediate formulas instead of cancelling a query per key.
/// </summary>
public sealed class CalculatedMeasureEditor : ObservableObject
{
    private readonly EasyBIContext _context;
    private readonly string _name;
    // The model's measure being redefined, null for a calculated measure of the report
    private readonly FieldDescriptor? _declared;
    private string _caption;
    private string _formula;
    private Option<string?> _format;
    private string? _error;
    private string? _preview;
    // The formula the preview is about, and the last one computed with its total (or the error of its query)
    private string? _wanted;
    private string? _computed;
    private object? _total;
    private string? _failure;
    private bool _previewing;

    /// <summary>
    /// Edits <c>measure</c>, or a new measure of <c>model</c> when it is null. A measure named like a measure of the
    /// model (see <see cref="MeasureContext.Definition"/>) redefines it.
    /// </summary>
    public CalculatedMeasureEditor(EasyBIContext context, ISemanticModel model, CalculatedMeasure? measure = null)
    {
        _context = context;
        Model = model;
        _name = measure?.Name ?? MeasureContext.NewName();
        _declared = model.Metadata.Fields.FirstOrDefault(f => f.Kind == FieldKind.Measure && f.Name == _name);
        IsNew = measure is null;
        FormatOptions =
        [
            new(null, () => Strings.FormatNumber),
            new("N0", () => Strings.FormatInteger),
            new("P1", () => Strings.FormatPercent),
            new("C2", () => Strings.FormatCurrency),
        ];
        // A format set by the developer ("#,##0 €") is kept as an option of its own
        if (measure?.Format is { } format && FormatOptions.All(o => o.Value != format))
            FormatOptions = [.. FormatOptions, new(format, () => Strings.Format(Strings.FormatCustom, format))];
        _format = FormatOptions.FirstOrDefault(o => o.Value == measure?.Format) ?? FormatOptions[0];
        _caption = measure is null ? "" : measure.Caption.Length > 0 ? measure.Caption : _declared?.Caption ?? "";
        _formula = measure is null ? "" : MeasureFormula.ToDisplay(measure.Formula, model.Metadata);
        // Formulas use the model's fields; a calculated measure of the report cannot use another one
        static string Reference(FieldDescriptor field) => "[" + field.Caption.Replace("]", "]]") + "]";
        Insertable = model.Metadata.Measures.Select(Reference).ToArray();
        InsertableDimensions = model.Metadata.Dimensions.Select(Reference).ToArray();
        Check();
    }

    public ISemanticModel Model { get; }

    public bool IsNew { get; }

    /// <summary>Whether the measure is one of the model's, which the report redefines.</summary>
    public bool IsRedefinition => _declared is not null;

    /// <summary>Whether the report redefines the model's measure, which <see cref="Reset"/> restores.</summary>
    public bool CanReset => _declared is not null && _context.Measures.Find(Model.Metadata.Name, _name) is not null;

    /// <summary>The formula that keeps the model's computation, as the user reads it (<c>[Chiffre d'affaires]</c>).</summary>
    public string? DeclaredFormula => _declared is null ? null : MeasureFormula.ToDisplay(MeasureContext.Definition(_declared, Model.Metadata.Name).Formula, Model.Metadata);

    public IReadOnlyList<Option<string?>> FormatOptions { get; }

    /// <summary>The model's measures as they are inserted into the formula.</summary>
    public IReadOnlyList<string> Insertable { get; }

    /// <summary>The model's dimensions as they are inserted into the formula, to be read inside a function.</summary>
    public IReadOnlyList<string> InsertableDimensions { get; }

    /// <summary>The functions of the formulas, with their usage and description.</summary>
    public IReadOnlyList<FormulaFunction> Functions => MeasureFormula.Functions;

    public string Caption
    {
        get => _caption;
        set { if (SetProperty(ref _caption, value)) Check(); }
    }

    public string Formula
    {
        get => _formula;
        set { if (SetProperty(ref _formula, value)) Check(); }
    }

    public Option<string?> Format
    {
        get => _format;
        set { if (SetProperty(ref _format, value)) Check(); }
    }

    /// <summary>Why the measure cannot be saved yet; null when it can.</summary>
    public string? Error
    {
        get => _error;
        private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(CanSave)); }
    }

    public bool CanSave => Error is null;

    /// <summary>The measure's total over the whole model, formatted; null while unknown.</summary>
    public string? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    /// <summary>The preview being computed (tests wait for it).</summary>
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    /// <summary>The measure as edited, in its stored form; null while it cannot be saved.</summary>
    public CalculatedMeasure? Result { get; private set; }

    /// <summary>
    /// Adds or replaces the measure in the context. A model's measure saved as the model defines it is restored rather
    /// than redefined.
    /// </summary>
    public bool Save()
    {
        if (Result is null) return false;
        if (_declared is not null && Result == MeasureContext.Definition(_declared, Model.Metadata.Name)) Reset();
        else _context.Measures.Set(Result);
        return true;
    }

    /// <summary>Restores the model's measure: the report no longer redefines it.</summary>
    public void Reset() => _context.Measures.Remove(Model.Metadata.Name, _name);

    private void Check()
    {
        Result = null;
        var caption = Caption.Trim();
        var stored = MeasureFormula.FromDisplay(Formula, Model.Metadata, out var formulaError);
        Error = caption.Length == 0 ? Strings.MeasureCaptionRequired
            : IsTaken(caption) ? Strings.Format(Strings.MeasureCaptionTaken, caption)
            : formulaError;
        _wanted = stored;
        if (stored is not null && Error is null)
            Result = new CalculatedMeasure { Name = _name, Caption = caption, Model = Model.Metadata.Name, Formula = stored, Format = Format.Value };
        // The caption does not change the total, and the format only changes how it reads
        ShowPreview();
        if (stored is not null && stored != _computed && !_previewing) PreviewTask = ComputePreviewsAsync();
    }

    private void ShowPreview() =>
        Preview = _wanted is null || _wanted != _computed ? null
            : _failure is not null ? Strings.Format(Strings.Error, _failure)
            : _total is null ? "—"
            : ValueFormatter.Format(Convert.ToDouble(_total, EasyBILocalization.FormatCulture), Format.Value);

    // Another measure of the model already shows this caption: the field list and the formulas could not tell them apart
    private bool IsTaken(string caption) =>
        _context.MeasuresOf(Model).Any(f => f.Name != _name && string.Equals(f.Caption, caption, StringComparison.CurrentCultureIgnoreCase));

    private async Task ComputePreviewsAsync()
    {
        _previewing = true;
        try
        {
            while (_wanted is { } formula && formula != _computed)
            {
                var measure = new CalculatedMeasure { Name = _name, Model = Model.Metadata.Name, Formula = formula };
                (object? total, string? failure) = (null, null);
                try
                {
                    var result = await Model.ExecuteAsync(new([], [new(measure.Name)], Calculations: [measure]));
                    total = result.Rows.FirstOrDefault()?[0];
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
                (_computed, _total, _failure) = (formula, total, failure);
                ShowPreview();
            }
        }
        finally
        {
            _previewing = false;
        }
    }
}
