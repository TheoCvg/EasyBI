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
/// It also helps typing: <see cref="Tabs"/> of functions, measures and dimensions to search and insert, the
/// <see cref="OperatorGroups"/>, <see cref="Suggestions"/> of what can be written where the caret is (the values of
/// a dimension after <c>[Région] =</c>...), the function at the caret (<see cref="CurrentCall"/>) and where the
/// formula's problem is written (<see cref="Problem"/>).
/// </para>
/// </summary>
public sealed class CalculatedMeasureEditor : ObservableObject
{
    private readonly EasyBIContext _context;
    private readonly string _name;
    // The model's measure being redefined, null for a calculated measure of the report
    private readonly FieldDescriptor? _declared;
    // The report's other measures of the model, which the formula may use
    private readonly IReadOnlyList<CalculatedMeasure> _others;
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
    private FormulaCatalogTab _selectedTab;
    // The values of the dimensions compared in the formula, loaded once
    private readonly Dictionary<string, IReadOnlyList<FormulaItem>> _values = [];
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
        _others = context.Measures.For(model.Metadata.Name).Where(m => m.Name != _name).ToArray();
        _formula = measure is null ? "" : MeasureFormula.ToDisplay(measure.Formula, model.Metadata, _others, _name);
        // Formulas use the model's fields and the report's other measures; under its own name, a redefinition reads the
        // model's measure
        static string Reference(FieldDescriptor field) => "[" + field.Caption.Replace("]", "]]") + "]";
        var measures = model.Metadata.Measures
            .Select(f => _others.FirstOrDefault(m => m.Name == f.Name) is { } redefined ? MeasureContext.ToField(redefined, f) : f)
            .Select(FormulaItem.For)
            .Concat(_others.Where(m => !model.Metadata.Fields.Any(f => f.Name == m.Name))
                .Select(m => FormulaItem.For(MeasureContext.ToField(m)) with { IsReportMeasure = true, Description = Strings.FormulaReportMeasureHelp, Example = null }))
            .ToArray();
        Insertable = measures.Select(m => m.Text).ToArray();
        InsertableDimensions = model.Metadata.Dimensions.Select(Reference).ToArray();
        _items = [.. MeasureFormula.Functions.Select(FormulaItem.For), .. measures, .. model.Metadata.Dimensions.Select(FormulaItem.For)];
        _catalog = _items;
        Tabs =
        [
            new(FormulaItemKind.Function, Strings.FormulaTabFunctions, "ƒ", _items.Where(i => i.Kind == FormulaItemKind.Function).ToArray()),
            new(FormulaItemKind.Measure, Strings.FormulaTabMeasures, "Σ", _items.Where(i => i.Kind == FormulaItemKind.Measure).ToArray()),
            new(FormulaItemKind.Dimension, Strings.FormulaTabDimensions, "Aa", _items.Where(i => i.Kind == FormulaItemKind.Dimension).ToArray()),
        ];
        _selectedTab = Tabs[0];
        OperatorGroups = FormulaItem.OperatorGroups();
        Check();
    }

    public ISemanticModel Model { get; }

    public bool IsNew { get; }

    /// <summary>Whether the measure is one of the model's, which the report redefines.</summary>
    public bool IsRedefinition => _declared is not null;

    /// <summary>Whether the report redefines the model's measure, which <see cref="Reset"/> restores.</summary>
    public bool CanReset => _declared is not null && _context.Measures.Find(Model.Metadata.Name, _name) is not null;

    /// <summary>The formula that keeps the model's computation, as the user reads it (<c>[Chiffre d'affaires]</c>).</summary>
    public string? DeclaredFormula => _declared is null ? null : MeasureFormula.ToDisplay(MeasureContext.Definition(_declared, Model.Metadata.Name).Formula, Model.Metadata, _others, _name);

    public IReadOnlyList<Option<string?>> FormatOptions { get; }

    /// <summary>The model's measures, then the report's other measures, as they are inserted into the formula.</summary>
    public IReadOnlyList<string> Insertable { get; }

    /// <summary>The model's dimensions as they are inserted into the formula, to be read inside a function.</summary>
    public IReadOnlyList<string> InsertableDimensions { get; }

    /// <summary>The functions of the formulas, with their usage and description.</summary>
    public IReadOnlyList<FormulaFunction> Functions => MeasureFormula.Functions;

    /// <summary>The operators of the keypad, by group.</summary>
    public IReadOnlyList<FormulaOperatorGroup> OperatorGroups { get; }

    /// <summary>The functions, the measures and the dimensions, each in its tab, matching the <see cref="Search"/>.</summary>
    public IReadOnlyList<FormulaCatalogTab> Tabs { get; }

    /// <summary>The open tab; a search that leaves it empty opens the first tab with results.</summary>
    public FormulaCatalogTab SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    /// <summary>Text searched in the <see cref="Tabs"/> (see <see cref="TextMatch"/>).</summary>
    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value)) return;
            foreach (var tab in Tabs) tab.Filter(value);
            Catalog = _items.Where(i => TextMatch.Matches(i.Caption, value)).ToArray();
            if (SelectedTab.Items.Count == 0 && Tabs.FirstOrDefault(t => t.Items.Count > 0) is { } found) SelectedTab = found;
        }
    }

    /// <summary>Every function, measure and dimension matching the <see cref="Search"/>, in this order.</summary>
    public IReadOnlyList<FormulaItem> Catalog
    {
        get => _catalog;
        private set => SetProperty(ref _catalog, value);
    }

    /// <summary>The item selected in the open tab, which Enter inserts.</summary>
    public FormulaItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

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

    /// <summary>The values being loaded for a suggestion (tests wait for it).</summary>
    public Task SuggestionsTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Finds the <see cref="Suggestions"/> for <paramref name="caretIndex"/>: only what can be written there (see
    /// <see cref="FormulaEditing.ContextAt"/>), starting like what is typed. In a function's value, the measures and
    /// the number dimensions; in a condition, the dimensions; after <c>[Région] =</c>, the values of Région; after a
    /// value in a condition, AND and OR. They show as a name is typed, after <c>(</c> or <c>;</c> and after a
    /// comparison; with <paramref name="all"/> (Ctrl+Space), anywhere.
    /// </summary>
    public void Suggest(int caretIndex, bool all = false)
    {
        caretIndex = Math.Clamp(caretIndex, 0, Formula.Length);
        var context = FormulaEditing.ContextAt(Formula, caretIndex);
        var opened = Formula[..context.Start].TrimEnd() is { Length: > 0 } before && before[^1] is '(' or ';' or ',';
        if (!all && !context.IsField && context.Prefix.Length == 0 && context.Slot != FormulaSlot.ComparedValue && !opened)
        {
            CloseSuggestions();
            return;
        }

        IReadOnlyList<FormulaItem> found;
        if (context.Slot == FormulaSlot.ComparedValue && !context.IsField)
        {
            if (FieldNamed(context.ComparedField) is not { } field) found = [];
            else if (_values.TryGetValue(field.Name, out var values)) found = values;
            else
            {
                // Suggested again once loaded, if the formula has not changed meanwhile
                var formula = Formula;
                found = [];
                SuggestionsTask = LoadValuesAsync(field, () => { if (Formula == formula) Suggest(caretIndex, all); });
            }
            found = found.Where(v => v.Caption.StartsWith(context.Prefix, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        }
        else
        {
            found = Matching(Allowed(context), context.Prefix);
        }
        // A word typed in full needs no suggestion
        if (found.Count == 1 && !context.IsField && string.Equals(found[0].Caption, context.Prefix, StringComparison.OrdinalIgnoreCase))
            found = [];
        Completion = found.Count > 0 ? new FormulaCompletion(context.Start, context.Length, context.Prefix, context.IsField) : null;
        Suggestions = found;
    }

    // What can be written at a place, in the order it is offered
    private IEnumerable<FormulaItem> Allowed(FormulaContext context)
    {
        bool Of(FormulaItem item, FormulaItemKind kind) => item.Kind == kind;
        bool RowMeasure(FormulaItem item) => Of(item, FormulaItemKind.Measure) && item.Field?.IsCalculated != true;
        var items = context.Slot switch
        {
            FormulaSlot.Value => _items.Where(i => Of(i, FormulaItemKind.Function) || Of(i, FormulaItemKind.Measure)),
            FormulaSlot.RowValue => [.. _items.Where(RowMeasure), .. _items.Where(i => Of(i, FormulaItemKind.Dimension) && i.Field?.DataType == DataType.Number),
                .. _items.Where(i => i.Caption == "IF")],
            FormulaSlot.Condition => [.. _items.Where(i => Of(i, FormulaItemKind.Dimension)), .. _items.Where(RowMeasure), FormulaItem.ForKeyword("NOT")],
            FormulaSlot.DistinctField => [.. _items.Where(i => Of(i, FormulaItemKind.Dimension)), .. _items.Where(RowMeasure)],
            FormulaSlot.AfterOperand when context.InCondition => [FormulaItem.ForKeyword("AND"), FormulaItem.ForKeyword("OR")],
            _ => [],
        };
        // After "[", only fields
        return context.IsField ? items.Where(i => i.Field is not null) : items;
    }

    // Starting like what is typed; a field may also contain it, after those starting like it
    private static IReadOnlyList<FormulaItem> Matching(IEnumerable<FormulaItem> items, string prefix)
    {
        bool Starts(FormulaItem item) => item.Caption.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase);
        return items.Where(i => Starts(i) || (i.Field is not null && TextMatch.Matches(i.Caption, prefix)))
            .OrderBy(i => Starts(i) ? 0 : 1)
            .ToArray();
    }

    private FieldDescriptor? FieldNamed(string? caption) =>
        caption is null ? null
        : Model.Metadata.Fields.FirstOrDefault(f => string.Equals(f.Caption, caption, StringComparison.CurrentCultureIgnoreCase))
          ?? Model.Metadata.Fields.FirstOrDefault(f => f.Name == caption);

    /// <summary>Replaces what is being typed with a suggestion.</summary>
    /// <returns>Where the caret goes.</returns>
    public int Accept(FormulaItem suggestion)
    {
        if (Completion is not { } completion) return CaretIndex;
        // A word typed by itself is followed by a space, and preceded by one after a value
        var item = suggestion;
        if (suggestion.Kind == FormulaItemKind.Keyword)
        {
            var spaced = completion.Start > 0 && !char.IsWhiteSpace(Formula[completion.Start - 1]) && Formula[completion.Start - 1] != '(';
            item = suggestion with { Text = (spaced ? " " : "") + suggestion.Text.Trim() + " " };
        }
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
        var problem = MeasureFormula.Check(Formula, Model.Metadata, out var stored, _others, _name);
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

    private async Task LoadValuesAsync(FieldDescriptor field, Action loaded)
    {
        try
        {
            var values = await Model.GetDistinctValuesAsync(field.Name);
            _values[field.Name] = values.OfType<object>().Take(MaxValues).Select(FormulaItem.ForValue).ToArray();
            loaded();
        }
        catch (Exception)
        {
            // Values only help typing: without them, the user types the value
            _values[field.Name] = [];
        }
    }

    private const int MaxValues = 100;

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
                    // With the measures it uses
                    var calculations = MeasureContext.Needed([measure.Name], [.. _others, measure], Model.Metadata);
                    var result = await Model.ExecuteAsync(new([], [new(measure.Name)], Calculations: calculations));
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
