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

/// <summary>Where the values of the computed variables and of the sets come from.</summary>
/// <param name="Model">The model of a name, or null when there is none.</param>
public sealed record SetSource(Func<string, ISemanticModel?> Model)
{
    /// <summary>The report's filters of a model, which the data of the variables go through.</summary>
    public Func<string, IEnumerable<FilterSpec>> Filters { get; init; } = _ => [];

    /// <summary>The end user's calculated measures, which a variable may use.</summary>
    public IEnumerable<CalculatedMeasure> Measures { get; init; } = [];
}

/// <summary>
/// The variables of the open report (<see cref="ReportDefinition.Variables"/>), shared by the dashboard, the designers
/// and the visuals: adding, changing or removing one, or changing its value, raises <see cref="Changed"/>. It replaces
/// the references to them in a visual before it is queried (<see cref="ResolveAsync(VisualDefinition, SetSource, CancellationToken)"/>)
/// and in formulas (<see cref="ResolveAsync(IEnumerable{CalculatedMeasure}, SetSource, CancellationToken)"/>), and
/// computes the values of the computed variables and of the sets, keeping them until a variable changes or
/// <see cref="Refresh"/>.
/// </summary>
public sealed class VariableContext
{
    private readonly List<ReportVariable> _variables = [];
    private readonly Dictionary<string, Task<IReadOnlyList<string?>>> _computed = [];
    private readonly object _gate = new();

    // The names of the measures a set or a computed value is queried with
    private const string OrderMeasure = "__variable_order";
    private const string ConditionMeasure = "__variable_condition";
    private const string RowsMeasure = "__variable_rows";
    private const string ValueMeasure = "__variable_value";
    private const string GroupMeasure = "__variable_group";

    public event EventHandler? Changed;

    public IReadOnlyList<ReportVariable> Variables => _variables;

    public ReportVariable? Find(string name) => _variables.FirstOrDefault(v => v.Name == name);

    /// <summary>What a variable holds; null when there is none of that name.</summary>
    public VariableDefinition? Definition(string name) => Find(name) is { } variable ? VariableFormula.Definition(variable) : null;

    /// <summary>The parameters and the computed values: the variables a formula can use.</summary>
    public IEnumerable<ReportVariable> Values => _variables.Where(v => VariableFormula.Definition(v).Kind != VariableKind.Set);

    /// <summary>The sets of values of the dimensions of a model.</summary>
    public IEnumerable<ReportVariable> SetsOf(string model) =>
        _variables.Where(v => v.Model == model && VariableFormula.Definition(v).Kind == VariableKind.Set);

    /// <summary>A name no variable has, from <paramref name="prefix"/> (N, N2...).</summary>
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

    /// <summary>Changes the value of a parameter; null gives it back the value of its formula.</summary>
    public void SetValue(string name, string? value)
    {
        if (Find(name) is not { } parameter || VariableFormula.Definition(parameter) is not ParameterDefinition definition) return;
        if (value == definition.Value) value = null;
        if (parameter.Value != value) Set(parameter with { Value = value });
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

    /// <summary>Forgets the computed values and the values of the sets, which are computed again: the data changed.</summary>
    public void Refresh()
    {
        lock (_gate) _computed.Clear();
    }

    private void OnChanged()
    {
        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- Values

    /// <summary>A parameter's value in invariant form: the end user's, or its formula's.</summary>
    public static string? ValueOf(ReportVariable parameter) =>
        parameter.Value ?? (VariableFormula.Definition(parameter) as ParameterDefinition)?.Value;

    /// <summary>A value of a variable as a visual shows it: formatted in <see cref="EasyBILocalization.FormatCulture"/>.</summary>
    public static string Display(string? value, DataType type, string? format = null)
    {
        if (value is null) return "";
        try
        {
            return type switch
            {
                DataType.Number => ValueFormatter.Format(double.Parse(value, CultureInfo.InvariantCulture), format),
                DataType.Date => ((DateTime)InvariantValue.Parse(value, typeof(DateTime))!).ToString(format ?? "d", EasyBILocalization.FormatCulture),
                DataType.Boolean => ValueFormatter.Display(bool.Parse(value)),
                _ => value,
            };
        }
        catch (FormatException)
        {
            return value;
        }
    }

    /// <summary>A parameter's value as a visual shows it.</summary>
    public static string Display(ReportVariable parameter) =>
        Display(ValueOf(parameter), VariableFormula.Definition(parameter).DataType, parameter.Format);

    /// <summary>A value of a set as a visual shows it, read with the type of its dimension in <paramref name="metadata"/>.</summary>
    public static string DisplayMember(string? member, ReportVariable set, ModelMetadata? metadata)
    {
        var field = (VariableFormula.Definition(set) as SetDefinition)?.Field;
        var type = metadata?.Fields.FirstOrDefault(f => f.Name == field)?.DataType switch
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

    private ReportVariable Require(string name, VariableKind? kind = null)
    {
        var variable = Find(name) ?? throw new InvalidOperationException(Strings.Format(Strings.VariableUnknown, name));
        var actual = VariableFormula.Definition(variable).Kind;
        if (kind is { } expected && (actual == VariableKind.Set) != (expected == VariableKind.Set))
            throw new InvalidOperationException(Strings.Format(expected == VariableKind.Set ? Strings.VariableNotSet : Strings.VariableNotParameter, name));
        return variable;
    }

    /// <summary>
    /// The value of a parameter or of a computed variable, in invariant form; null without a value.
    /// A computed value is queried once until a variable changes or <see cref="Refresh"/>.
    /// </summary>
    public async Task<string?> ValueAsync(string name, SetSource source, CancellationToken cancellationToken = default)
    {
        var variable = Require(name, VariableKind.Parameter);
        if (VariableFormula.Definition(variable).Kind == VariableKind.Parameter) return ValueOf(variable);
        var values = await ComputeAsync(variable, source, cancellationToken).ConfigureAwait(false);
        return values.FirstOrDefault();
    }

    /// <summary>The value of a variable as a visual shows it: a parameter's or a computed value, the values of a set.</summary>
    public async Task<string> DisplayAsync(string name, SetSource source, CancellationToken cancellationToken = default)
    {
        var variable = Require(name);
        var definition = VariableFormula.Definition(variable);
        if (definition.Kind != VariableKind.Set)
            return Display(await ValueAsync(name, source, cancellationToken).ConfigureAwait(false), definition.DataType, variable.Format);
        var members = await MembersAsync(name, source, cancellationToken).ConfigureAwait(false);
        var metadata = source.Model(variable.Model)?.Metadata;
        return string.Join(", ", members.Select(m => DisplayMember(m, variable, metadata)));
    }

    // A number variable as a count
    private async Task<int?> CountAsync(string name, SetSource source, CancellationToken cancellationToken)
    {
        var variable = Require(name, VariableKind.Parameter);
        if (VariableFormula.Definition(variable).DataType != DataType.Number)
            throw new InvalidOperationException(Strings.Format(Strings.VariableNotNumber, name));
        var value = await ValueAsync(name, source, cancellationToken).ConfigureAwait(false);
        return value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (int)Math.Max(0, Math.Floor(number))
            : null;
    }

    /// <summary>
    /// The measures with the values of the variables their formulas use in place of the references (<c>[@Rate]</c>):
    /// a number, a text or a date in quotes, TRUE or FALSE; a variable without a value gives an empty value.
    /// </summary>
    public async Task<IReadOnlyList<CalculatedMeasure>> ResolveAsync(IEnumerable<CalculatedMeasure> measures, SetSource source,
        CancellationToken cancellationToken = default)
    {
        var all = measures.ToArray();
        var literals = new Dictionary<string, string>();
        foreach (var name in all.SelectMany(m => MeasureFormula.Variables(m.Formula)).Distinct())
        {
            if (Find(name) is not { } variable || VariableFormula.Definition(variable) is not { Kind: not VariableKind.Set } definition)
                continue;
            literals[name] = Literal(await ValueAsync(name, source, cancellationToken).ConfigureAwait(false), definition.DataType);
        }
        return [.. all.Select(m => MeasureFormula.Variables(m.Formula).Count == 0 ? m
            : m with { Formula = MeasureFormula.WithValues(m.Formula, n => literals.TryGetValue(n, out var literal) ? literal : VariableReferences.Reference(n)) })];
    }

    private static string Literal(string? value, DataType type) => type switch
    {
        // A division by zero is the formulas' empty value
        DataType.Number => value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? "(" + number.ToString("0.#################", CultureInfo.InvariantCulture) + ")"
            : "(0/0)",
        DataType.Boolean => bool.TryParse(value, out var flag) && flag ? "TRUE" : "FALSE",
        DataType.Date when value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) =>
            "\"" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\"",
        _ => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"",
    };

    /// <summary>
    /// The values of a set, in invariant form: those it lists, or those its conditions, order and count choose over the
    /// data the report's filters of its model keep (except those on sets). Computed once until a variable changes or
    /// <see cref="Refresh"/>.
    /// </summary>
    public async Task<IReadOnlyList<string?>> MembersAsync(string name, SetSource source, CancellationToken cancellationToken = default)
    {
        var set = Require(name, VariableKind.Set);
        var definition = (SetDefinition)VariableFormula.Definition(set);
        if (definition.IsList) return definition.Members!;
        return await ComputeAsync(set, source, cancellationToken).ConfigureAwait(false);
    }

    // The values of a set, or the value of a computed variable, queried once
    private async Task<IReadOnlyList<string?>> ComputeAsync(ReportVariable variable, SetSource source, CancellationToken cancellationToken)
    {
        var model = source.Model(variable.Model) ?? throw new InvalidOperationException($"Unknown model '{variable.Model}'.");
        var metadata = model.Metadata;
        var reportMeasures = source.Measures.Where(m => m.Model == variable.Model).ToArray();
        var filters = new List<FilterSpec>();
        foreach (var filter in source.Filters(variable.Model).Where(f => !Names(f).Any(n => Definition(n)?.Kind == VariableKind.Set)))
            filters.Add(await ResolveValuesAsync(filter, source, cancellationToken).ConfigureAwait(false));

        VisualDefinition visual;
        List<CalculatedMeasure> extra = [];
        CalculatedMeasure Measure(string measureName, string formula) =>
            new() { Name = measureName, Model = variable.Model, Formula = formula };
        if (VariableFormula.Definition(variable) is SetDefinition set)
        {
            bool IsMeasure(string field) =>
                metadata.Fields.Any(f => f.Name == field && f.Kind == FieldKind.Measure) || reportMeasures.Any(m => m.Name == field);
            var (rows, groups, order) = MeasureFormula.SetParts(variable.Formula, IsMeasure);
            var values = new Dictionary<string, string?>();
            foreach (var used in rows.SelectMany(MeasureFormula.Variables).Distinct())
                values[used] = await ValueAsync(used, source, cancellationToken).ConfigureAwait(false);
            filters.AddRange(rows.Select(r => MeasureFormula.ToRowFilter(r, n => metadata.Fields.FirstOrDefault(f => f.Name == n), n => values[n])));
            if (set.Members is { } members) filters.Add(new FilterSpec(set.Field, FilterOperator.In, members));
            if (order is not null) extra.Add(Measure(OrderMeasure, order));
            if (groups.Count > 0)
            {
                // The values whose groups match every condition
                extra.Add(Measure(ConditionMeasure, "IF(" + string.Join(" AND ", groups.Select(g => "(" + g + ")")) + "; 1; 0)"));
                filters.Add(new FilterSpec(ConditionMeasure, FilterOperator.GreaterThanOrEqual, ["1"]));
            }
            if (extra.Count == 0) extra.Add(Measure(RowsMeasure, "COUNT()"));
            var top = set.TopVariable is { } count ? await CountAsync(count, source, cancellationToken).ConfigureAwait(false) : set.Top;
            var axis = new FieldRef(set.Field);
            visual = new VisualDefinition
            {
                Id = "set:" + variable.Name,
                Model = variable.Model,
                Type = VisualType.Table,
                Axis = [axis],
                Values = [.. extra.Select(m => new MeasureRef(m.Name))],
                Filters = filters,
                Sort = order is null ? new SortSpec(axis.Key) : new SortSpec(OrderMeasure, set.Descending),
                Top = top,
            };
        }
        else
        {
            extra.Add(Measure(ValueMeasure, await WithGroupsAsync(variable, model, filters, reportMeasures, source, cancellationToken).ConfigureAwait(false)));
            visual = new VisualDefinition
            {
                Id = "value:" + variable.Name,
                Model = variable.Model,
                Type = VisualType.Kpi,
                Values = [new MeasureRef(ValueMeasure)],
                Filters = filters,
            };
        }
        var measures = await ResolveAsync([.. reportMeasures, .. extra], source, cancellationToken).ConfigureAwait(false);
        return await CachedAsync(model, visual, measures, visual.Axis.Count > 0 ? visual.Axis[0].Key : ValueMeasure, cancellationToken).ConfigureAwait(false);
    }

    // A computed value's formula with each aggregation BY a dimension replaced by its value: the value is queried for
    // each of the dimension's values (inside the IQueryable), then those values are aggregated
    private async Task<string> WithGroupsAsync(ReportVariable variable, ISemanticModel model, IReadOnlyList<FilterSpec> filters,
        IReadOnlyList<CalculatedMeasure> reportMeasures, SetSource source, CancellationToken cancellationToken)
    {
        var formula = variable.Formula;
        var parts = MeasureFormula.GroupedParts(formula);
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            var (start, end, function, value, by) = parts[i];
            var axis = new FieldRef(by);
            var visual = new VisualDefinition
            {
                Id = "by:" + variable.Name,
                Model = variable.Model,
                Type = VisualType.Table,
                Axis = [axis],
                Values = [new MeasureRef(GroupMeasure)],
                Filters = filters,
                Sort = new SortSpec(axis.Key),
            };
            CalculatedMeasure[] measures = [.. reportMeasures, new() { Name = GroupMeasure, Model = variable.Model, Formula = value }];
            var resolved = await ResolveAsync(measures, source, cancellationToken).ConfigureAwait(false);
            var values = (await CachedAsync(model, visual, resolved, GroupMeasure, cancellationToken).ConfigureAwait(false))
                .Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null)
                .OfType<double>().ToArray();
            double? result = values.Length == 0 ? null : function switch
            {
                "SUM" => values.Sum(),
                "AVERAGE" => values.Average(),
                "MIN" => values.Min(),
                _ => values.Max(),
            };
            // An empty value stays empty in the formula: IF without its else
            var literal = result is { } number ? "(" + number.ToString("0.#################", CultureInfo.InvariantCulture) + ")" : "IF(FALSE; 0)";
            formula = formula.Substring(0, start) + literal + formula.Substring(end);
        }
        return formula;
    }

    // The values of a column of a visual, queried once while the cache holds them
    private async Task<IReadOnlyList<string?>> CachedAsync(ISemanticModel model, VisualDefinition visual, IReadOnlyList<CalculatedMeasure> measures,
        string column, CancellationToken cancellationToken)
    {
        var key = JsonSerializer.Serialize(new { visual, measures, column, Culture = EasyBILocalization.FormatCulture.Name }, ReportSerializer.Options);
        Task<IReadOnlyList<string?>> task;
        lock (_gate)
        {
            if (!_computed.TryGetValue(key, out task!))
            {
                // Shared by every visual using the variable: none of them cancels it for the others
                _computed[key] = task = Compute(model, visual, measures, column);
            }
        }
        return await WithCancellation(task, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string?>> Compute(ISemanticModel model, VisualDefinition visual, IReadOnlyList<CalculatedMeasure> measures, string key)
    {
        if (visual.Top == 0) return [];
        try
        {
            var data = await model.GetDataAsync(visual.ToDataRequest(model.Metadata, measures)).ConfigureAwait(false);
            var column = data.Result.IndexOf(key);
            return [.. data.Result.Rows.Select(r => InvariantValue.Format(r[column]))];
        }
        catch
        {
            // Asked again next time
            lock (_gate)
            {
                foreach (var failed in _computed.Where(m => m.Value.IsFaulted || m.Value.IsCanceled).Select(m => m.Key).ToArray())
                    _computed.Remove(failed);
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
    /// set" as the set's name and the others as "Others", unless the visual labels them otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">The visual references a variable that does not exist, or of the wrong kind.</exception>
    public async Task<VisualDefinition> ResolveAsync(VisualDefinition visual, SetSource source, CancellationToken cancellationToken = default)
    {
        var used = VariableReferences.UsedBy(visual);
        if (used.Count == 0) return visual;

        // Texts only show the variables that exist: a text may write braces
        var texts = new Dictionary<string, string>();
        foreach (var name in VariableReferences.InText(visual.Title).Concat(VariableReferences.InText(visual.Text?.Text)).Distinct())
        {
            if (Find(name) is not null) texts[name] = await DisplayAsync(name, source, cancellationToken).ConfigureAwait(false);
        }

        async Task<FieldRef> Field(FieldRef field)
        {
            if (field.Set is not { } name) return field;
            Require(name, VariableKind.Set);
            var labels = new Dictionary<string, string> { [bool.TrueString] = name, [bool.FalseString] = Strings.SetOthers };
            foreach (var label in field.Labels ?? new Dictionary<string, string>()) labels[label.Key] = label.Value;
            return field with { SetMembers = await MembersAsync(name, source, cancellationToken).ConfigureAwait(false), Labels = labels };
        }

        var filters = new List<FilterSpec>();
        foreach (var filter in visual.Filters) filters.Add(await ResolveValuesAsync(filter, source, cancellationToken).ConfigureAwait(false));
        var axis = new List<FieldRef>();
        foreach (var field in visual.Axis) axis.Add(await Field(field).ConfigureAwait(false));
        return visual with
        {
            Title = VariableReferences.Interpolate(visual.Title, n => texts.TryGetValue(n, out var text) ? text : null),
            Text = visual.Text is { } text ? text with { Text = VariableReferences.Interpolate(text.Text, n => texts.TryGetValue(n, out var shown) ? shown : null) } : null,
            Filters = filters,
            Top = visual.TopVariable is { } top ? await CountAsync(top, source, cancellationToken).ConfigureAwait(false) : visual.Top,
            TopVariable = null,
            Axis = axis,
            Legend = visual.Legend is { } legend ? await Field(legend).ConfigureAwait(false) : null,
        };
    }

    // The values of the variables a filter uses: a value, or the values of a set ("one of" or "none of")
    private async Task<FilterSpec> ResolveValuesAsync(FilterSpec filter, SetSource source, CancellationToken cancellationToken)
    {
        if (filter.Alternatives is { } alternatives)
        {
            var groups = new List<IReadOnlyList<FilterSpec>>();
            foreach (var group in alternatives)
            {
                var resolved = new List<FilterSpec>();
                foreach (var f in group) resolved.Add(await ResolveValuesAsync(f, source, cancellationToken).ConfigureAwait(false));
                groups.Add(resolved);
            }
            filter = filter with { Alternatives = groups };
        }
        if (!filter.Values.Any(v => VariableReferences.NameOf(v) is not null)) return filter;
        var values = new List<string?>();
        foreach (var value in filter.Values)
        {
            if (VariableReferences.NameOf(value) is not { } name) values.Add(value);
            else if (Definition(name) is { Kind: VariableKind.Set })
            {
                if (filter.Operator is not (FilterOperator.In or FilterOperator.NotIn))
                    throw new InvalidOperationException(Strings.Format(Strings.VariableNotParameter, name));
                values.AddRange(await MembersAsync(name, source, cancellationToken).ConfigureAwait(false));
            }
            else values.Add(await ValueAsync(name, source, cancellationToken).ConfigureAwait(false));
        }
        return filter with { Values = values };
    }

    private static IEnumerable<string> Names(FilterSpec filter) =>
        filter.Values.Select(VariableReferences.NameOf).OfType<string>()
            .Concat((filter.Alternatives ?? []).SelectMany(g => g).SelectMany(Names));
}
