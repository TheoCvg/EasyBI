using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Formulas of <see cref="CalculatedMeasure"/>: measures in brackets, numbers with a decimal point, + - * / and
/// parentheses. A formula is stored with field names (<c>[Margin] / [Amount]</c>) and shown with captions
/// (<c>[Marge] / [Chiffre d'affaires]</c>); a <c>]</c> inside brackets is written <c>]]</c>.
/// </summary>
public static class MeasureFormula
{
    /// <summary>Checks a stored formula against a model.</summary>
    /// <returns>Null when the formula is valid, otherwise the problem worded for the end user.</returns>
    public static string? Validate(string formula, ModelMetadata metadata)
    {
        try
        {
            foreach (var reference in References(Parse(formula)))
                Resolve(metadata, reference.Text, byCaption: false);
            return null;
        }
        catch (FormulaException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The formula as the user reads it: field names replaced by their captions.</summary>
    public static string ToDisplay(string formula, ModelMetadata metadata) =>
        Rewrite(formula, name => metadata.Fields.FirstOrDefault(f => f.Name == name)?.Caption ?? name);

    /// <summary>
    /// The formula typed by the user (measures named by caption, or by name) in its stored form, with field names.
    /// </summary>
    /// <returns>The stored formula, or null with <paramref name="error"/> worded for the end user.</returns>
    public static string? FromDisplay(string text, ModelMetadata metadata, out string? error)
    {
        try
        {
            var references = References(Parse(text)).Select(r => r.Text).Distinct()
                .ToDictionary(reference => reference, reference => Resolve(metadata, reference, byCaption: true).Name);
            error = null;
            return Rewrite(text, reference => references[reference]);
        }
        catch (FormulaException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// The formula as an expression of nullable doubles: <paramref name="measure"/> gives the value of each referenced
    /// measure for the group; a division by zero gives null.
    /// </summary>
    internal static Expression Build(string formula, Func<string, Expression> measure) => Build(Parse(formula), measure);

    private static Expression Build(Node node, Func<string, Expression> measure)
    {
        switch (node)
        {
            case NumberNode number:
                return Expression.Constant(number.Value, typeof(double?));
            case MeasureNode reference:
                return Expression.Convert(measure(reference.Text), typeof(double?));
            case NegateNode negate:
                return Expression.Negate(Build(negate.Operand, measure));
            case BinaryNode binary:
                var (left, right) = (Build(binary.Left, measure), Build(binary.Right, measure));
                return binary.Operator switch
                {
                    '+' => Expression.Add(left, right),
                    '-' => Expression.Subtract(left, right),
                    '*' => Expression.Multiply(left, right),
                    _ => Expression.Condition(
                        Expression.Equal(right, Expression.Constant(0d, typeof(double?))),
                        Expression.Constant(null, typeof(double?)),
                        Expression.Divide(left, right)),
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private static FieldDescriptor Resolve(ModelMetadata metadata, string reference, bool byCaption)
    {
        var byName = metadata.Fields.FirstOrDefault(f => f.Name == reference);
        if (byCaption)
        {
            var captioned = metadata.Fields.Where(f => string.Equals(f.Caption, reference, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (captioned.Length > 1) throw new FormulaException(Strings.Format(Strings.FormulaAmbiguous, reference));
            byName = captioned.SingleOrDefault() ?? byName;
        }
        if (byName is null) throw new FormulaException(Strings.Format(Strings.FormulaUnknownMeasure, reference));
        if (byName.Kind != FieldKind.Measure) throw new FormulaException(Strings.Format(Strings.FormulaNotAMeasure, reference));
        return byName;
    }

    // Replaces each measure reference, keeping the rest of the text (spaces, parentheses) as the user typed it
    private static string Rewrite(string formula, Func<string, string> replace)
    {
        var text = new StringBuilder();
        var position = 0;
        foreach (var token in Tokenize(formula).Where(t => t.Kind == TokenKind.Measure))
        {
            text.Append(formula, position, token.Start - position).Append('[').Append(replace(token.Text).Replace("]", "]]")).Append(']');
            position = token.Start + token.Length;
        }
        return text.Append(formula, position, formula.Length - position).ToString();
    }

    private static IEnumerable<MeasureNode> References(Node node) => node switch
    {
        MeasureNode reference => [reference],
        NegateNode negate => References(negate.Operand),
        BinaryNode binary => References(binary.Left).Concat(References(binary.Right)),
        _ => [],
    };

    // --- Parsing: expression := term (('+' | '-') term)*, term := factor (('*' | '/') factor)*,
    //     factor := number | [measure] | '(' expression ')' | '-' factor

    private static Node Parse(string formula)
    {
        var tokens = Tokenize(formula);
        if (tokens.Count == 1) throw new FormulaException(Strings.FormulaEmpty);
        var parser = new Parser(tokens);
        var node = parser.Expression();
        if (parser.Current.Kind != TokenKind.End) throw Unexpected(parser.Current);
        return node;
    }

    private static FormulaException Unexpected(Token token) =>
        new(Strings.Format(Strings.FormulaUnexpected, token.Text, token.Start + 1));

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        public Token Current => tokens[_index];

        public Node Expression()
        {
            var node = Term();
            while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var op = Next().Text[0];
                node = new BinaryNode(op, node, Term());
            }
            return node;
        }

        private Node Term()
        {
            var node = Factor();
            while (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/")
            {
                var op = Next().Text[0];
                node = new BinaryNode(op, node, Factor());
            }
            return node;
        }

        private Node Factor()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    Next();
                    return new NumberNode(double.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
                case TokenKind.Measure:
                    Next();
                    return new MeasureNode(token.Text);
                case TokenKind.Operator when token.Text == "-":
                    Next();
                    return new NegateNode(Factor());
                case TokenKind.Open:
                    Next();
                    var inner = Expression();
                    if (Current.Kind != TokenKind.Close) throw new FormulaException(Strings.FormulaMissingParenthesis);
                    Next();
                    return inner;
                case TokenKind.End:
                    throw new FormulaException(Strings.Format(Strings.FormulaExpectedOperand, token.Start + 1));
                default:
                    throw Unexpected(token);
            }
        }

        private Token Next() => tokens[_index++];
    }

    private static List<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < formula.Length)
        {
            var c = formula[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsAsciiDigit(formula[i + 1])))
            {
                var start = i;
                while (i < formula.Length && (char.IsAsciiDigit(formula[i]) || formula[i] == '.')) i++;
                if (i < formula.Length && formula[i] == ',' && i + 1 < formula.Length && char.IsAsciiDigit(formula[i + 1]))
                    throw new FormulaException(Strings.FormulaDecimalComma);
                var number = formula[start..i];
                if (number.Count(ch => ch == '.') > 1) throw new FormulaException(Strings.Format(Strings.FormulaUnexpected, number, start + 1));
                tokens.Add(new Token(TokenKind.Number, number, start, i - start));
            }
            else if (c == '[')
            {
                var start = i++;
                var name = new StringBuilder();
                while (true)
                {
                    if (i >= formula.Length) throw new FormulaException(Strings.FormulaMissingBracket);
                    if (formula[i] == ']')
                    {
                        if (i + 1 < formula.Length && formula[i + 1] == ']')
                        {
                            name.Append(']');
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    name.Append(formula[i++]);
                }
                tokens.Add(new Token(TokenKind.Measure, name.ToString().Trim(), start, i - start));
            }
            else
            {
                var kind = c switch
                {
                    '+' or '-' or '*' or '/' => TokenKind.Operator,
                    '(' => TokenKind.Open,
                    ')' => TokenKind.Close,
                    _ => throw new FormulaException(Strings.Format(Strings.FormulaUnexpected, c, i + 1)),
                };
                tokens.Add(new Token(kind, c.ToString(), i, 1));
                i++;
            }
        }
        tokens.Add(new Token(TokenKind.End, "", formula.Length, 0));
        return tokens;
    }

    private enum TokenKind { Number, Measure, Operator, Open, Close, End }

    private sealed record Token(TokenKind Kind, string Text, int Start, int Length);

    private abstract record Node;

    private sealed record NumberNode(double Value) : Node;

    private sealed record MeasureNode(string Text) : Node;

    private sealed record NegateNode(Node Operand) : Node;

    private sealed record BinaryNode(char Operator, Node Left, Node Right) : Node;

    private sealed class FormulaException(string message) : Exception(message);
}
