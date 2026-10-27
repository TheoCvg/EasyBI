using System.Globalization;
using System.Text;
using System.Text.Json;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// The references to variables of a report (<see cref="ReportVariable"/>): <c>[@N]</c> as the value of a filter or in a
/// formula, <c>{N}</c> in a title or a text (<c>{{</c> and <c>}}</c> write braces).
/// </summary>
public static class VariableReferences
{
    /// <summary>The value of a filter standing for a variable: <c>[@N]</c>.</summary>
    public static string Reference(string name) => "[@" + name + "]";

    /// <summary>The name of the variable a filter's value stands for (<c>[@N]</c>: N), or null for a value.</summary>
    public static string? NameOf(string? value) =>
        value is { Length: > 3 } && value.StartsWith("[@", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal)
        && ReportVariable.IsValidName(value.Substring(2, value.Length - 3))
            ? value.Substring(2, value.Length - 3)
            : null;

    /// <summary>The names of the variables a text references (<c>{N}</c>), once each.</summary>
    public static IReadOnlyList<string> InText(string? text)
    {
        var names = new List<string>();
        Interpolate(text, name =>
        {
            if (!names.Contains(name)) names.Add(name);
            return null;
        });
        return names;
    }

    /// <summary>
    /// The text with each <c>{N}</c> replaced by what <paramref name="value"/> gives for N; a null value leaves the
    /// reference as written.
    /// </summary>
    public static string Interpolate(string? text, Func<string, string?> value)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = new StringBuilder();
        for (var i = 0; i < text!.Length; i++)
        {
            var c = text[i];
            if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
            {
                result.Append(c);
                i++;
                continue;
            }
            var close = c == '{' ? text.IndexOf('}', i + 1) : -1;
            if (close > i + 1 && text.Substring(i + 1, close - i - 1) is var name && ReportVariable.IsValidName(name)
                && value(name) is { } replaced)
            {
                result.Append(replaced);
                i = close;
                continue;
            }
            result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>
    /// The variables a visual references, once each: in its filters, its <see cref="VisualDefinition.TopVariable"/>, its
    /// fields grouped by a set, its title and text, the one a <see cref="VisualType.Variable"/> shows. Not those of the
    /// formulas of its measures.
    /// </summary>
    public static IReadOnlyList<string> UsedBy(VisualDefinition visual)
    {
        IEnumerable<string> InFilter(FilterSpec filter) =>
            filter.Values.Select(NameOf).OfType<string>()
                .Concat((filter.Alternatives ?? []).SelectMany(group => group).SelectMany(InFilter));
        return visual.Filters.SelectMany(InFilter)
            .Concat(new[] { visual.TopVariable, visual.Variable }.OfType<string>())
            .Concat(visual.Axis.Append(visual.Legend).Select(f => f?.Set).OfType<string>())
            .Concat(InText(visual.Title))
            .Concat(InText(visual.Text?.Text))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// The filter keeping the rows whose <paramref name="field"/> has one of the values <paramref name="keys"/> (invariant
    /// form). For a field grouped by a set (true or false), the set's values themselves: a reference to the set, or with
    /// <paramref name="members"/>, the values the field holds (<see cref="FieldRef.SetMembers"/>).
    /// </summary>
    public static FilterSpec Matching(this FieldRef field, IReadOnlyCollection<string?> keys, bool members = false)
    {
        if (field.Set is not { } set) return new FilterSpec(field.Field, FilterOperator.In, [.. keys], field.Grain);
        var (inSet, others) = (keys.Contains(bool.TrueString), keys.Contains(bool.FalseString));
        // Both: every row; neither: none
        if (inSet == others) return inSet ? FilterSpec.AnyOf([[]]) : new FilterSpec(field.Field, FilterOperator.In, [], field.Grain);
        IReadOnlyList<string?> values = members && field.SetMembers is { } known ? known : [Reference(set)];
        return new FilterSpec(field.Field, inSet ? FilterOperator.In : FilterOperator.NotIn, values, field.Grain);
    }
}

/// <summary>Where the values of a set chosen by a rule (<see cref="SetRule"/>) come from.</summary>
/// <param name="Model">The model of a name, or null when there is none.</param>
public sealed record SetSource(Func<string, ISemanticModel?> Model)
{
    /// <summary>The report's filters of a model, which the rule's data goes through.</summary>
    public Func<string, IEnumerable<FilterSpec>> Filters { get; init; } = _ => [];

    /// <summary>The end user's calculated measures, which a rule may use.</summary>
    public IEnumerable<CalculatedMeasure> Measures { get; init; } = [];
}

/// <summary>
/// The variables of the open report (<see cref="ReportDefinition.Variables"/>), shared by the dashboard, the designers
/// and the visuals: adding, changing or removing one, or changing its value, raises <see cref="Changed"/>. It replaces
/// the references to them in a visual before it is queried (<see cref="ResolveAsync"/>) and in formulas
/// (<see cref="Resolve(IEnumerable{CalculatedMeasure})"/>), and computes the values of sets chosen by a rule, keeping
/// them until a variable changes or <see cref="Refresh"/>.
/// </summary>
public sealed class VariableContext
{
    private readonly List<ReportVariable> _variables = [];
    private readonly Dictionary<string, Task<IReadOnlyList<string?>>> _members = [];
    private readonly object _gate = new();

    public event EventHandler? Changed;

    public IReadOnlyList<ReportVariable> Variables => _variables;

    public ReportVariable? Find(string name) => _variables.FirstOrDefault(v => v.Name == name);

    /// <summary>The parameters, without the sets.</summary>
    public IEnumerable<ReportVariable> Parameters => _variables.Where(v => v.Kind == VariableKind.Parameter);

    /// <summary>The sets of values of a dimension of a model.</summary>
    public IEnumerable<ReportVariable> SetsOf(string model, string field) =>
        _variables.Where(v => v.Kind == VariableKind.Set && v.Model == model && v.Field == field);

    /// <summary>A name no variable has, from <paramref name="prefix"/> (Parameter, Parameter2...).</summary>
    public string NewName(string prefix)
    {
        var name = prefix;
        for (var i = 2; Find(name) is not null; i++) name = prefix + i.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    /// <summary>Adds a variable, or replaces the one named <paramref name="previousName"/> (its own name by default).</summary>
    public void Set(ReportVariable variable, string? previousName = null)
    {
        var index = _variables.FindIndex(v => v.Name == (previousName ?? variable.Name));
        if (index >= 0) _variables[index] = variable;
        else _variables.Add(variable);
        OnChanged();
    }

    /// <summary>Changes the value of a parameter.</summary>
    public void SetValue(string name, string? value)
    {
        if (Find(name) is { Kind: VariableKind.Parameter } parameter && parameter.Value != value) Set(parameter with { Value = value });
    }

    public void Remove(string name)
    {
        if (_variables.RemoveAll(v => v.Name == name) > 0) OnChanged();
    }

    /// <summary>Replaces every variable (opening a report).</summary>
    public void Load(IEnumerable<ReportVariable> variables)
    {
        _variables.Clear();
        _variables.AddRange(variables);
        OnChanged();
    }

    /// <summary>Forgets the values of the sets chosen by a rule, which are computed again: the data changed.</summary>
    public void Refresh()
    {
        lock (_gate) _members.Clear();
    }

    private void OnChanged()
    {
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Resolution ---

    /// <summary>The value of a parameter as a visual shows it: formatted in <see cref="EasyBILocalization.FormatCulture"/>.</summary>
    public static string Display(ReportVariable parameter)
    {
        if (parameter.Value is not { } value) return "";
        try
        {
            return parameter.DataType switch
            {
                DataType.Number => ValueFormatter.Format(double.Parse(value, CultureInfo.InvariantCulture), parameter.Format),
                DataType.Date => ((DateTime)InvariantValue.Parse(value, typeof(DateTime))!).ToString(parameter.Format ?? "d", EasyBILocalization.FormatCulture),
                DataType.Boolean => ValueFormatter.Display(bool.Parse(value)),
                _ => value,
            };
        }
        catch (FormatException)
        {
            return value;
        }
    }

    /// <summary>A value of a set as a visual shows it, read with the type of its dimension in <paramref name="metadata"/>.</summary>
    public static string DisplayMember(string? member, ReportVariable set, ModelMetadata? metadata)
    {
        var type = metadata?.Fields.FirstOrDefault(f => f.Name == set.Field)?.DataType switch
        {
            DataType.Number => typeof(double),
            DataType.Date => typeof(DateTime),
            DataType.Boolean => typeof(bool),
            _ => typeof(string),
        };
        try
        {
            return ValueFormatter.Display(InvariantValue.Parse(member, type));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException)
        {
            return member ?? ValueFormatter.Display(null);
        }
    }

    /// <summary>The number a number parameter holds, rounded down to a count; null without a value.</summary>
    private int? Count(string name)
    {
        var parameter = Require(name, VariableKind.Parameter);
        if (parameter.DataType != DataType.Number) throw new InvalidOperationException(Strings.Format(Strings.VariableNotNumber, parameter.DisplayName));
        return parameter.Value is { } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (int)Math.Max(0, Math.Floor(number))
            : null;
    }

    private ReportVariable Require(string name, VariableKind? kind = null)
    {
        var variable = Find(name) ?? throw new InvalidOperationException(Strings.Format(Strings.VariableUnknown, name));
        if (kind is { } expected && variable.Kind != expected)
            throw new InvalidOperationException(Strings.Format(expected == VariableKind.Set ? Strings.VariableNotSet : Strings.VariableNotParameter, variable.DisplayName));
        return variable;
    }

    /// <summary>
    /// The measures with the values of the parameters their formulas use in place of the references (<c>[@Rate]</c>):
    /// a number, a text or a date in quotes, TRUE or FALSE; a parameter without a value gives an empty value.
    /// </summary>
    public IReadOnlyList<CalculatedMeasure> Resolve(IEnumerable<CalculatedMeasure> measures) =>
        [.. measures.Select(m => MeasureFormula.Variables(m.Formula).Count == 0 ? m : m with { Formula = MeasureFormula.WithValues(m.Formula, Literal) })];

    private string Literal(string name)
    {
        if (Find(name) is not { Kind: VariableKind.Parameter } parameter) return VariableReferences.Reference(name);
        var value = parameter.Value;
        return parameter.DataType switch
        {
            // A division by zero is the formulas' empty value
            DataType.Number => value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? "(" + number.ToString("R", CultureInfo.InvariantCulture) + ")"
                : "(0/0)",
            DataType.Boolean => bool.TryParse(value, out var flag) && flag ? "TRUE" : "FALSE",
            DataType.Date when value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) =>
                "\"" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"",
            _ => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"",
        };
    }

    /// <summary>
    /// The values of a set, in invariant form: its list, or those its rule chooses over the data the report's filters of
    /// its model keep (except those on sets). Computed once until a variable changes or <see cref="Refresh"/>.
    /// </summary>
    public Task<IReadOnlyList<string?>> MembersAsync(string name, SetSource source, CancellationToken cancellationToken = default)
    {
        var set = Require(name, VariableKind.Set);
        if (set.Rule is not { } rule) return Task.FromResult<IReadOnlyList<string?>>(set.Members ?? []);

        var count = rule.CountVariable is { } variable ? Count(variable) : rule.Count;
        var model = source.Model(set.Model) ?? throw new InvalidOperationException($"Unknown model '{set.Model}'.");
        var filters = source.Filters(set.Model)
            .Where(f => !Names(f).Any(n => Find(n)?.Kind == VariableKind.Set))
            .Select(ResolveParameters)
            .ToArray();
        var measures = Resolve(MeasureContext.Needed([rule.Measure], source.Measures.Where(m => m.Model == set.Model), model.Metadata));
        var visual = new VisualDefinition
        {
            Id = "set:" + set.Name,
            Model = set.Model,
            Type = VisualType.Table,
            Axis = [new FieldRef(set.Field)],
            Values = [new MeasureRef(rule.Measure)],
            Filters = filters,
            Sort = new SortSpec(rule.Measure, Descending: !rule.Bottom),
            Top = count,
        };
        var key = JsonSerializer.Serialize(new { visual, measures, Culture = EasyBILocalization.FormatCulture.Name }, ReportSerializer.Options);
        Task<IReadOnlyList<string?>> task;
        lock (_gate)
        {
            if (!_members.TryGetValue(key, out task!))
            {
                // Shared by every visual using the set: none of them cancels it for the others
                _members[key] = task = Compute(model, visual, measures);
            }
        }
        return WithCancellation(task, cancellationToken);
    }

    private async Task<IReadOnlyList<string?>> Compute(ISemanticModel model, VisualDefinition visual, IReadOnlyList<CalculatedMeasure> measures)
    {
        if (visual.Top == 0) return [];
        try
        {
            var data = await model.GetDataAsync(visual.ToDataRequest(model.Metadata, measures)).ConfigureAwait(false);
            var column = data.Result.IndexOf(visual.Axis[0].Key);
            return [.. data.Result.Rows.Select(r => InvariantValue.Format(r[column]))];
        }
        catch
        {
            // Asked again next time
            lock (_gate)
            {
                foreach (var failed in _members.Where(m => m.Value.IsFaulted || m.Value.IsCanceled).Select(m => m.Key).ToArray())
                    _members.Remove(failed);
            }
            throw;
        }
    }

    private static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted) return await task.ConfigureAwait(false);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task)
                throw new OperationCanceledException(cancellationToken);
        }
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// The visual as it is queried: references to variables replaced by their values in its filters, its Top, its title
    /// and text; its fields grouped by a set get the set's values (<see cref="FieldRef.SetMembers"/>) and show "in the
    /// set" as the set's caption and the others as "Others", unless the visual labels them otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">The visual references a variable that does not exist, or of the wrong kind.</exception>
    public async Task<VisualDefinition> ResolveAsync(VisualDefinition visual, SetSource source, CancellationToken cancellationToken = default)
    {
        var used = VariableReferences.UsedBy(visual);
        if (used.Count == 0) return visual;

        // Sets referenced by a text only show their values when they exist
        var members = new Dictionary<string, IReadOnlyList<string?>>();
        var texts = VariableReferences.InText(visual.Title).Concat(VariableReferences.InText(visual.Text?.Text)).ToHashSet();
        foreach (var name in used)
        {
            var variable = texts.Contains(name) ? Find(name) : Require(name);
            if (variable?.Kind == VariableKind.Set && name != visual.Variable)
                members[name] = await MembersAsync(name, source, cancellationToken).ConfigureAwait(false);
        }

        string? Text(string name) => Find(name) switch
        {
            { Kind: VariableKind.Parameter } parameter => Display(parameter),
            { } set when members.TryGetValue(name, out var values) =>
                string.Join(", ", values.Select(v => DisplayMember(v, set, source.Model(set.Model)?.Metadata))),
            _ => null,
        };

        FilterSpec Filter(FilterSpec filter)
        {
            if (filter.Alternatives is { } alternatives)
                filter = filter with { Alternatives = [.. alternatives.Select(g => (IReadOnlyList<FilterSpec>)[.. g.Select(Filter)])] };
            if (!filter.Values.Any(v => VariableReferences.NameOf(v) is not null)) return filter;
            var values = new List<string?>();
            foreach (var value in filter.Values)
            {
                if (VariableReferences.NameOf(value) is not { } name) values.Add(value);
                else if (Require(name).Kind == VariableKind.Parameter) values.Add(Find(name)!.Value);
                else if (filter.Operator is FilterOperator.In or FilterOperator.NotIn) values.AddRange(members[name]);
                else throw new InvalidOperationException(Strings.Format(Strings.VariableNotParameter, Find(name)!.DisplayName));
            }
            return filter with { Values = values };
        }

        FieldRef Field(FieldRef field)
        {
            if (field.Set is not { } name) return field;
            var set = Require(name, VariableKind.Set);
            var labels = new Dictionary<string, string> { [bool.TrueString] = set.DisplayName, [bool.FalseString] = Strings.SetOthers };
            foreach (var label in field.Labels ?? new Dictionary<string, string>()) labels[label.Key] = label.Value;
            return field with { SetMembers = members[name], Labels = labels };
        }

        return visual with
        {
            Title = VariableReferences.Interpolate(visual.Title, Text),
            Text = visual.Text is { } text ? text with { Text = VariableReferences.Interpolate(text.Text, Text) } : null,
            Filters = [.. visual.Filters.Select(Filter)],
            Top = visual.TopVariable is { } top ? Count(top) : visual.Top,
            TopVariable = null,
            Axis = [.. visual.Axis.Select(Field)],
            Legend = visual.Legend is { } legend ? Field(legend) : null,
        };
    }

    // The values of a parameter in a filter
    private FilterSpec ResolveParameters(FilterSpec filter) => filter with
    {
        Values = [.. filter.Values.Select(v => VariableReferences.NameOf(v) is { } name && Find(name) is { Kind: VariableKind.Parameter } p ? p.Value : v)],
        Alternatives = filter.Alternatives is { } alternatives
            ? [.. alternatives.Select(g => (IReadOnlyList<FilterSpec>)[.. g.Select(ResolveParameters)])]
            : null,
    };

    private static IEnumerable<string> Names(FilterSpec filter) =>
        filter.Values.Select(VariableReferences.NameOf).OfType<string>()
            .Concat((filter.Alternatives ?? []).SelectMany(g => g).SelectMany(Names));
}
