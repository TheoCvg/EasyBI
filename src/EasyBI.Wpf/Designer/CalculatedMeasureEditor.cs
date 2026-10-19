using EasyBI.Abstractions;
using EasyBI.Core;
using EasyBI.Wpf.Resources;

namespace EasyBI.Wpf.Designer;

/// <summary>
/// Creates or changes one of the report's calculated measures: a name, a formula typed with the measures' captions
/// (<c>[Marge] / [Chiffre d'affaires]</c>) and a format. The formula is checked as it is typed, and a valid one shows
/// its total as a preview.
/// </summary>
public sealed class CalculatedMeasureEditor : ObservableObject
{
    private readonly EasyBIContext _context;
    private readonly string _name;
    private string _caption;
    private string _formula;
    private Option<string?> _format;
    private string? _error;
    private string? _preview;
    private CancellationTokenSource? _previewing;

    /// <summary>Edits <c>measure</c>, or a new measure of <c>model</c> when it is null.</summary>
    public CalculatedMeasureEditor(EasyBIContext context, ISemanticModel model, CalculatedMeasure? measure = null)
    {
        _context = context;
        Model = model;
        _name = measure?.Name ?? MeasureContext.NewName();
        IsNew = measure is null;
        FormatOptions =
        [
            new(null, () => Strings.FormatNumber),
            new("N0", () => Strings.FormatInteger),
            new("P1", () => Strings.FormatPercent),
            new("C2", () => Strings.FormatCurrency),
        ];
        _format = FormatOptions.FirstOrDefault(o => o.Value == measure?.Format) ?? FormatOptions[0];
        _caption = measure?.Caption ?? "";
        _formula = measure is null ? "" : MeasureFormula.ToDisplay(measure.Formula, model.Metadata);
        // Formulas combine the model's measures; a calculated measure of the report cannot use another one
        Insertable = model.Metadata.Measures.Select(f => "[" + f.Caption.Replace("]", "]]") + "]").ToArray();
        Check();
    }

    public ISemanticModel Model { get; }

    public bool IsNew { get; }

    public IReadOnlyList<Option<string?>> FormatOptions { get; }

    /// <summary>The model's measures as they are inserted into the formula.</summary>
    public IReadOnlyList<string> Insertable { get; }

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

    /// <summary>Adds or replaces the measure in the context.</summary>
    public bool Save()
    {
        if (Result is null) return false;
        _context.Measures.Set(Result);
        return true;
    }

    private void Check()
    {
        _previewing?.Cancel();
        Result = null;
        Preview = null;
        var caption = Caption.Trim();
        var stored = MeasureFormula.FromDisplay(Formula, Model.Metadata, out var formulaError);
        Error = caption.Length == 0 ? Strings.MeasureCaptionRequired
            : IsTaken(caption) ? Strings.Format(Strings.MeasureCaptionTaken, caption)
            : formulaError;
        if (stored is null) return;

        var measure = new CalculatedMeasure { Name = _name, Caption = caption, Model = Model.Metadata.Name, Formula = stored, Format = Format.Value };
        if (Error is null) Result = measure;
        var cancel = _previewing = new CancellationTokenSource();
        PreviewTask = ComputePreviewAsync(measure, cancel.Token);
    }

    // Another measure of the model already shows this caption: the field list and the formulas could not tell them apart
    private bool IsTaken(string caption) =>
        _context.MeasuresOf(Model).Any(f => f.Name != _name && string.Equals(f.Caption, caption, StringComparison.CurrentCultureIgnoreCase));

    private async Task ComputePreviewAsync(CalculatedMeasure measure, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Model.ExecuteAsync(new([], [new(measure.Name)], Calculations: [measure]), cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            var value = result.Rows.FirstOrDefault()?[0];
            Preview = value is null ? "—" : ValueFormatter.Format(Convert.ToDouble(value, EasyBILocalization.FormatCulture), measure.Format);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Preview = Strings.Format(Strings.Error, ex.Message);
        }
    }
}
