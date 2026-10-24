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
/// <para>
/// It also helps typing: a <see cref="Catalog"/> of functions and fields to search and insert, the values of the
/// selected dimension, <see cref="Suggestions"/> for what is being typed, the function at the caret
/// (<see cref="CurrentCall"/>) and where the formula's problem is written (<see cref="Problem"/>).
/// </para>
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
    private FormulaProblem? _problem;
    private readonly IReadOnlyList<FormulaItem> _items;
    private string _search = "";
    private IReadOnlyList<FormulaItem> _catalog;
    private FormulaItem? _selectedItem;
    private IReadOnlyList<FormulaItem> _values = [];
    private int _caretIndex;
    private FormulaCall? _currentCall;
    private FormulaCompletion? _completion;
    private IReadOnlyList<FormulaItem> _suggestions = [];

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
        _items = [.. MeasureFormula.Functions.Select(FormulaItem.For), .. model.Metadata.Measures.Select(FormulaItem.For), .. model.Metadata.Dimensions.Select(FormulaItem.For)];
        _catalog = _items;
        Operators = FormulaItem.Operators();
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

    /// <summary>The operators of the toolbar.</summary>
    public IReadOnlyList<FormulaItem> Operators { get; }

    /// <summary>Text searched in the <see cref="Catalog"/> (see <see cref="TextMatch"/>).</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value)) return;
            Catalog = _items.Where(i => TextMatch.Matches(i.Caption, value)).ToArray();
        }
    }

    /// <summary>The functions, measures and dimensions matching the <see cref="Search"/>, in this order.</summary>
    public IReadOnlyList<FormulaItem> Catalog
    {
        get => _catalog;
        private set => SetProperty(ref _catalog, value);
    }

    /// <summary>The item of the catalog whose details are shown; for a dimension, its <see cref="Values"/> are loaded.</summary>
    public FormulaItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            Values = [];
            ValuesTask = value is { Kind: FormulaItemKind.Dimension, Field: { } field } ? LoadValuesAsync(field) : Task.CompletedTask;
        }
    }

    /// <summary>The first values of the selected dimension, to insert in a condition.</summary>
    public IReadOnlyList<FormulaItem> Values
    {
        get => _values;
        private set => SetProperty(ref _values, value);
    }

    /// <summary>The values being loaded (tests wait for it).</summary>
    public Task ValuesTask { get; private set; } = Task.CompletedTask;

    /// <summary>Where the caret is in the <see cref="Formula"/>, which gives the <see cref="CurrentCall"/>.</summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            if (SetProperty(ref _caretIndex, value)) CurrentCall = FormulaEditing.CallAt(Formula, value);
        }
    }

    /// <summary>The function whose arguments the caret is in, to show how to write it; null outside a function.</summary>
    public FormulaCall? CurrentCall
    {
        get => _currentCall;
        private set => SetProperty(ref _currentCall, value);
    }

    /// <summary>Where the formula's problem is written, to point it out; null while the formula is valid or empty.</summary>
    public FormulaProblem? Problem
    {
        get => _problem;
        private set => SetProperty(ref _problem, value);
    }

    /// <summary>What <see cref="Suggest"/> found being typed, which an accepted suggestion replaces.</summary>
    public FormulaCompletion? Completion
    {
        get => _completion;
        private set => SetProperty(ref _completion, value);
    }

    /// <summary>The functions, words and fields that complete what is being typed; empty when there is nothing to suggest.</summary>
    public IReadOnlyList<FormulaItem> Suggestions
    {
        get => _suggestions;
        private set => SetProperty(ref _suggestions, value);
    }

    /// <summary>
    /// Inserts an item at <paramref name="start"/>, replacing <paramref name="length"/> characters; a function or
    /// parentheses go around them.
    /// </summary>
    /// <returns>Where the caret goes: after the item, or between its parentheses when nothing was selected.</returns>
    public int Insert(FormulaItem item, int start, int length)
    {
        start = Math.Clamp(start, 0, Formula.Length);
        length = Math.Clamp(length, 0, Formula.Length - start);
        var selected = Formula.Substring(start, length);
        var wraps = item.Text.EndsWith("()", StringComparison.Ordinal);
        var inserted = wraps ? item.Text[..^1] + selected + ")" : item.Text;
        Formula = Formula[..start] + inserted + Formula[(start + length)..];
        CloseSuggestions();
        CaretIndex = wraps && selected.Length == 0 ? start + inserted.Length - 1 : start + inserted.Length;
        return CaretIndex;
    }

    /// <summary>
    /// Finds what is being typed at <paramref name="caretIndex"/> and the <see cref="Suggestions"/> completing it: the
    /// fields after <c>[</c>, otherwise the functions and words starting like it, or else the fields containing it.
    /// With <paramref name="all"/> (Ctrl+Space), everything is suggested when nothing is being typed.
    /// </summary>
    public void Suggest(int caretIndex, bool all = false)
    {
        var completion = FormulaEditing.CompletionAt(Formula, caretIndex);
        if (completion is null && all) completion = new FormulaCompletion(Math.Clamp(caretIndex, 0, Formula.Length), 0, "", IsField: false);
        var prefix = completion?.Prefix ?? "";
        bool Starts(string caption) => caption.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);
        IEnumerable<FormulaItem> Fields() => _items.Where(i => i.Kind != FormulaItemKind.Function && TextMatch.Matches(i.Caption, prefix))
            .OrderBy(i => Starts(i.Caption) ? 0 : 1);
        IReadOnlyList<FormulaItem> found = completion switch
        {
            null => [],
            { IsField: true } => Fields().ToArray(),
            _ => [.. _items.Where(i => i.Kind == FormulaItemKind.Function && Starts(i.Caption)),
                  .. FormulaEditing.Keywords.Where(Starts).Select(FormulaItem.ForKeyword)],
        };
        if (completion is { IsField: false } && (found.Count == 0 || prefix.Length == 0)) found = [.. found, .. Fields()];
        // A word typed in full needs no suggestion
        if (found.Count == 1 && completion is { IsField: false } && string.Equals(found[0].Caption, prefix, StringComparison.OrdinalIgnoreCase))
            found = [];
        Completion = found.Count > 0 ? completion : null;
        Suggestions = found;
    }

    /// <summary>Replaces what is being typed with a suggestion.</summary>
    /// <returns>Where the caret goes.</returns>
    public int Accept(FormulaItem suggestion)
    {
        if (Completion is not { } completion) return CaretIndex;
        // A word typed by itself is followed by a space
        var item = suggestion.Kind == FormulaItemKind.Keyword ? suggestion with { Text = suggestion.Text.Trim() + " " } : suggestion;
        return Insert(item, completion.Start, completion.Length);
    }

    public void CloseSuggestions()
    {
        Completion = null;
        Suggestions = [];
    }

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
        var problem = MeasureFormula.Check(Formula, Model.Metadata, out var stored);
        // Nothing to point out in an empty formula
        Problem = Formula.Trim().Length == 0 ? null : problem;
        Error = caption.Length == 0 ? Strings.MeasureCaptionRequired
            : IsTaken(caption) ? Strings.Format(Strings.MeasureCaptionTaken, caption)
            : problem?.Message;
        CurrentCall = FormulaEditing.CallAt(Formula, Math.Min(CaretIndex, Formula.Length));
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

    private async Task LoadValuesAsync(FieldDescriptor field)
    {
        try
        {
            var values = await Model.GetDistinctValuesAsync(field.Name);
            if (SelectedItem?.Field != field) return;
            Values = values.OfType<object>().Take(MaxValues).Select(FormulaItem.ForValue).ToArray();
        }
        catch (Exception)
        {
            // Values only help typing: without them, the user types the value
        }
    }

    private const int MaxValues = 50;

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
