namespace EasyBI.Core;

/// <summary>What a part of a formula is, to color it.</summary>
public enum FormulaTokenKind { Field, Function, Keyword, Text, Number, Operator, Punctuation, Unknown }

/// <summary>A part of a formula: its kind and where it is written.</summary>
public readonly record struct FormulaToken(FormulaTokenKind Kind, int Start, int Length);

/// <summary>The function whose arguments the caret is in, and the argument (from 0).</summary>
public sealed record FormulaCall(FormulaFunction Function, int Argument);

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
    public static FormulaCall? CallAt(string text, int position)
    {
        var tokens = Tokenize(text);
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
                    return i > 0 && tokens[i - 1] is { Kind: FormulaTokenKind.Function } name
                        ? new FormulaCall(MeasureFormula.FindFunction(text.Substring(name.Start, name.Length))!, argument)
                        : null;
                default:
                    if (depth == 0) argument++;
                    break;
            }
        }
        return null;
    }

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
