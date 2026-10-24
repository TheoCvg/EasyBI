namespace EasyBI.Core;

/// <summary>What a part of a formula is, to color it.</summary>
public enum FormulaTokenKind { Field, Function, Keyword, Text, Number, Operator, Punctuation, Unknown }

/// <summary>A part of a formula: its kind and where it is written.</summary>
public readonly record struct FormulaToken(FormulaTokenKind Kind, int Start, int Length);

/// <summary>The function whose arguments the caret is in, and the argument (from 0).</summary>
public sealed record FormulaCall(FormulaFunction Function, int Argument);

/// <summary>What can be written at a place of a formula.</summary>
public enum FormulaSlot
{
    /// <summary>A value of the group: a measure, a function.</summary>
    Value,

    /// <summary>A number read on each row, inside an aggregation: a measure or a number dimension, IF.</summary>
    RowValue,

    /// <summary>A condition on the rows: a comparison of a dimension or a measure, NOT.</summary>
    Condition,

    /// <summary>The field whose different values DISTINCTCOUNT counts.</summary>
    DistinctField,

    /// <summary>After a complete value: an operator; in a condition, AND or OR.</summary>
    AfterOperand,

    /// <summary>The value a field is compared with, after <c>[Region] =</c>: one of the field's values.</summary>
    ComparedValue,
}

/// <summary>What can be written at a place of a formula, and what is already typed there, which a suggestion replaces.</summary>
/// <param name="Slot">What can be written there.</param>
/// <param name="Start">Where what is typed starts.</param>
/// <param name="Length">How many characters a suggestion replaces.</param>
/// <param name="Prefix">What is typed before the caret (without the opening bracket or quote).</param>
/// <param name="InCondition">Whether the place is in a condition, where AND and OR can follow a value.</param>
/// <param name="ComparedField">For <see cref="FormulaSlot.ComparedValue"/>, the field as written in its brackets.</param>
/// <param name="IsField">Whether a field name is being typed after <c>[</c>.</param>
public sealed record FormulaContext(
    FormulaSlot Slot,
    bool InCondition,
    string? ComparedField,
    int Start,
    int Length,
    string Prefix,
    bool IsField);

/// <summary>
/// What is being typed at the caret: a field name after an open <c>[</c> (<see cref="IsField"/>), or a word (a
/// function, AND, OR...). A suggestion replaces <see cref="Length"/> characters from <see cref="Start"/>.
/// </summary>
public sealed record FormulaCompletion(int Start, int Length, string Prefix, bool IsField);

/// <summary>
/// Help to type a <see cref="MeasureFormula"/>: coloring, the function at the caret and completion. Unlike the parser,
/// these accept a formula being typed (an unclosed bracket or quote...) and never fail.
/// </summary>
public static class FormulaEditing
{
    /// <summary>The words of the formulas other than the functions.</summary>
    public static IReadOnlyList<string> Keywords { get; } = ["AND", "OR", "NOT", "TRUE", "FALSE"];

    /// <summary>The parts of a formula, in order; spaces are left out.</summary>
    public static IReadOnlyList<FormulaToken> Tokenize(string text)
    {
        var tokens = new List<FormulaToken>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var start = i;
            FormulaTokenKind kind;
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c is '[' or '"')
            {
                // Up to the closing character (doubled inside), or the end while it is being typed
                var close = c == '[' ? ']' : '"';
                i++;
                while (i < text.Length)
                {
                    if (text[i] == close)
                    {
                        if (i + 1 < text.Length && text[i + 1] == close) { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                kind = c == '[' ? FormulaTokenKind.Field : FormulaTokenKind.Text;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
                kind = FormulaTokenKind.Number;
            }
            else if (char.IsAsciiLetter(c))
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];
                kind = MeasureFormula.FindFunction(word) is not null ? FormulaTokenKind.Function
                    : Keywords.Contains(word, StringComparer.OrdinalIgnoreCase) ? FormulaTokenKind.Keyword
                    : FormulaTokenKind.Unknown;
            }
            else
            {
                i++;
                if (c is '<' or '>' && i < text.Length && (text[i] == '=' || (c == '<' && text[i] == '>'))) i++;
                kind = c switch
                {
                    '+' or '-' or '*' or '/' or '=' or '<' or '>' => FormulaTokenKind.Operator,
                    '(' or ')' or ';' or ',' => FormulaTokenKind.Punctuation,
                    _ => FormulaTokenKind.Unknown,
                };
            }
            tokens.Add(new FormulaToken(kind, start, i - start));
        }
        return tokens;
    }

    /// <summary>The function whose parentheses hold <paramref name="position"/>, or null.</summary>
    public static FormulaCall? CallAt(string text, int position) => Calls(text, position).FirstOrDefault();

    /// <summary>The functions whose parentheses hold <paramref name="position"/>, from the innermost one.</summary>
    public static IReadOnlyList<FormulaCall> Calls(string text, int position)
    {
        var tokens = Tokenize(text);
        var calls = new List<FormulaCall>();
        var depth = 0;
        var argument = 0;
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i];
            if (token.Start >= position) continue;
            // Inside a text or a field name, separators and parentheses are not the formula's
            if (token.Kind is not FormulaTokenKind.Punctuation) continue;
            switch (text[token.Start])
            {
                case ')':
                    depth++;
                    break;
                case '(' when depth > 0:
                    depth--;
                    break;
                case '(':
                    // Plain parentheses only group: the arguments counted so far belong to them
                    if (i > 0 && tokens[i - 1] is { Kind: FormulaTokenKind.Function } name)
                        calls.Add(new FormulaCall(MeasureFormula.FindFunction(text.Substring(name.Start, name.Length))!, argument));
                    argument = 0;
                    break;
                default:
                    if (depth == 0) argument++;
                    break;
            }
        }
        return calls;
    }

    /// <summary>
    /// What can be written at <paramref name="position"/>, from the functions around it and what precedes it: what a
    /// smart completion offers.
    /// </summary>
    public static FormulaContext ContextAt(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        var tokens = Tokenize(text);
        // What is already typed there: a name, or a value (a text or a number) being typed
        var completion = CompletionAt(text, position);
        var typed = tokens.LastOrDefault(t => t.Kind is FormulaTokenKind.Text or FormulaTokenKind.Number
            && t.Start < position && position <= t.Start + t.Length && !(t.Kind == FormulaTokenKind.Text && IsClosedText(text, t) && position == t.Start + t.Length));
        var (start, length, prefix) = completion is not null ? (completion.Start, completion.Length, completion.Prefix)
            : typed != default ? (typed.Start, typed.Length, text[typed.Start..position].TrimStart('"'))
            : (position, 0, "");

        var (slot, inCondition) = Role(Calls(text, start));
        var before = tokens.Where(t => t.Start + t.Length <= start).ToArray();
        if (before.Length > 0)
        {
            var last = before[^1];
            var lastText = text.Substring(last.Start, last.Length);
            if (last.Kind == FormulaTokenKind.Operator && lastText is "=" or "<>" or "<" or "<=" or ">" or ">=")
            {
                if (before.Length > 1 && before[^2].Kind == FormulaTokenKind.Field)
                {
                    var field = text.Substring(before[^2].Start, before[^2].Length);
                    return new(FormulaSlot.ComparedValue, inCondition, field.TrimStart('[').TrimEnd(']').Replace("]]", "]").Trim(),
                        start, length, prefix, IsField: completion?.IsField == true);
                }
            }
            else if (last.Kind is FormulaTokenKind.Field or FormulaTokenKind.Number or FormulaTokenKind.Text
                     || (last.Kind == FormulaTokenKind.Keyword && lastText.ToUpperInvariant() is "TRUE" or "FALSE")
                     || lastText == ")")
            {
                slot = FormulaSlot.AfterOperand;
            }
        }
        return new(slot, inCondition, null, start, length, prefix, IsField: completion?.IsField == true);
    }

    // The slot an argument is, from the innermost function: the value of an aggregation is read on each row, its last
    // argument is a condition, and IF gives values of the place it is in
    private static (FormulaSlot Slot, bool InCondition) Role(IReadOnlyList<FormulaCall> calls)
    {
        var inRow = calls.Any(c => c.Function.Name != "IF");
        foreach (var call in calls)
        {
            switch (call.Function.Name, call.Argument)
            {
                case ("IF", 0):
                    return (FormulaSlot.Condition, true);
                case ("IF", _):
                    return (inRow ? FormulaSlot.RowValue : FormulaSlot.Value, false);
                case ("COUNT", _):
                    return (FormulaSlot.Condition, true);
                case ("DISTINCTCOUNT", 0):
                    return (FormulaSlot.DistinctField, false);
                case (_, 0):
                    return (FormulaSlot.RowValue, false);
                default:
                    return (FormulaSlot.Condition, true);
            }
        }
        return (FormulaSlot.Value, false);
    }

    private static bool IsClosedText(string text, FormulaToken token) =>
        token.Length > 1 && text[token.Start + token.Length - 1] == '"';

    /// <summary>What is being typed at <paramref name="position"/>, or null (in a text, after a space or an operator...).</summary>
    public static FormulaCompletion? CompletionAt(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        var token = Tokenize(text).LastOrDefault(t => t.Start < position && position <= t.Start + t.Length);
        if (token == default) return null;
        var typed = text[token.Start..position];
        switch (token.Kind)
        {
            case FormulaTokenKind.Field:
                // Still inside the brackets: the closing one, if typed, is replaced too
                var whole = text.Substring(token.Start, token.Length);
                if (whole.EndsWith(']') && position == token.Start + token.Length && whole.Length > 1) return null;
                var length = whole.EndsWith(']') ? token.Length : position - token.Start;
                return new FormulaCompletion(token.Start, length, typed[1..], IsField: true);
            case FormulaTokenKind.Function or FormulaTokenKind.Keyword or FormulaTokenKind.Unknown when char.IsAsciiLetter(typed[0]):
                return new FormulaCompletion(token.Start, token.Length, typed, IsField: false);
            default:
                return null;
        }
    }
}
