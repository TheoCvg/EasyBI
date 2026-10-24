using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

/// <summary>
/// Formulas of <see cref="CalculatedMeasure"/>. A measure in brackets is aggregated as the model declares it
/// (<c>[Margin] / [Amount]</c>); an aggregation function computes a value from the rows of each group, where
/// dimensions and measures are read row by row (<c>SUM([Quantity] * [Price])</c>, <c>DISTINCTCOUNT([Customer])</c>),
/// optionally on the rows matching a condition (<c>SUM([Amount]; [Region] = "North")</c>). The operators are
/// + - * /, the comparisons = &lt;&gt; &lt; &lt;= &gt; &gt;=, AND, OR and NOT; numbers take a decimal point, texts and
/// dates go in double quotes (<c>"2024-12-31"</c>), arguments are separated by <c>;</c> or <c>,</c>. A formula is
/// stored with field names and shown with captions; a <c>]</c> inside brackets is written <c>]]</c>, a <c>"</c>
/// inside quotes <c>""</c>.
/// </summary>
public static class MeasureFormula
{
    /// <summary>The functions of the formulas, in the order the designer offers them.</summary>
    public static IReadOnlyList<FormulaFunction> Functions { get; } =
    [
        new("SUM", 1, 2), new("AVERAGE", 1, 2), new("MIN", 1, 2), new("MAX", 1, 2),
        new("COUNT", 0, 1), new("DISTINCTCOUNT", 1, 2), new("IF", 2, 3),
    ];

    // AVG is accepted for AVERAGE
    private static FormulaFunction? FindFunction(string name) =>
        Functions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? (string.Equals(name, "AVG", StringComparison.OrdinalIgnoreCase) ? Functions[1] : null);

    /// <summary>Checks a stored formula against a model.</summary>
    /// <returns>Null when the formula is valid, otherwise the problem worded for the end user.</returns>
    public static string? Validate(string formula, ModelMetadata metadata)
    {
        try
        {
            new Checker(formula, reference => Resolve(metadata, reference, byCaption: false)).Check(Parse(formula));
            return null;
        }
        catch (FormulaException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Whether the formula is only the field <paramref name="name"/>, such as <c>[Amount]</c> (spaces and parentheses aside).</summary>
    public static bool IsOnly(string formula, string name)
    {
        try
        {
            return Parse(formula) is FieldNode reference && reference.Text == name;
        }
        catch (FormulaException)
        {
            return false;
        }
    }

    /// <summary>The formula as the user reads it: field names replaced by their captions.</summary>
    public static string ToDisplay(string formula, ModelMetadata metadata) =>
        Rewrite(formula, name => metadata.Fields.FirstOrDefault(f => f.Name == name)?.Caption ?? name);

    /// <summary>
    /// The formula typed by the user (fields named by caption, or by name) in its stored form, with field names.
    /// </summary>
    /// <returns>The stored formula, or null with <paramref name="error"/> worded for the end user.</returns>
    public static string? FromDisplay(string text, ModelMetadata metadata, out string? error)
    {
        try
        {
            var tree = Parse(text);
            // Checked as typed, so that the problems name the fields by their captions
            new Checker(text, reference => Resolve(metadata, reference, byCaption: true)).Check(tree);
            var references = References(tree).Select(r => r.Text).Distinct()
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
    /// The formula as an expression of nullable doubles over a group. <paramref name="measure"/> gives the value of a
    /// measure for the group, aggregated as the model declares it; <paramref name="selector"/> the row value of a field,
    /// read inside the aggregation functions over <paramref name="rows"/> (whose elements are <paramref name="rowType"/>).
    /// A division by zero gives null.
    /// </summary>
    internal static Expression Build(
        string formula,
        Expression rows,
        Type rowType,
        Func<string, Expression> measure,
        Func<string, LambdaExpression> selector) =>
        new Builder(rows, rowType, measure, selector).Group(Parse(formula));

    private static FieldDescriptor Resolve(ModelMetadata metadata, string reference, bool byCaption)
    {
        var byName = metadata.Fields.FirstOrDefault(f => f.Name == reference);
        if (byCaption)
        {
            var captioned = metadata.Fields.Where(f => string.Equals(f.Caption, reference, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (captioned.Length > 1) throw new FormulaException(Strings.Format(Strings.FormulaAmbiguous, reference));
            byName = captioned.SingleOrDefault() ?? byName;
        }
        return byName ?? throw new FormulaException(Strings.Format(Strings.FormulaUnknownField, reference));
    }

    // Replaces each field reference, keeping the rest of the text (spaces, parentheses) as the user typed it
    private static string Rewrite(string formula, Func<string, string> replace)
    {
        var text = new StringBuilder();
        var position = 0;
        foreach (var token in Tokenize(formula).Where(t => t.Kind == TokenKind.Field))
        {
            text.Append(formula, position, token.Start - position).Append('[').Append(replace(token.Text).Replace("]", "]]")).Append(']');
            position = token.End;
        }
        return text.Append(formula, position, formula.Length - position).ToString();
    }

    private static IEnumerable<FieldNode> References(Node node) => node switch
    {
        FieldNode reference => [reference],
        NegateNode negate => References(negate.Operand),
        NotNode not => References(not.Operand),
        BinaryNode binary => References(binary.Left).Concat(References(binary.Right)),
        CallNode call => call.Arguments.SelectMany(References),
        _ => [],
    };

    // --- Checking: what each part of the formula is, from the model's metadata only

    private enum ValueKind { Number, Text, Date, Boolean }

    /// <summary>
    /// Checks the kinds of values: a formula gives a number, a condition is a comparison, a dimension is only read row
    /// by row inside an aggregation function.
    /// </summary>
    private sealed class Checker(string formula, Func<string, FieldDescriptor> resolve)
    {
        public void Check(Node node) => Require(node, Kind(node, null), ValueKind.Number);

        // aggregation: the function whose rows are being read, null at the level of the group
        private ValueKind Kind(Node node, string? aggregation)
        {
            switch (node)
            {
                case NumberNode:
                    return ValueKind.Number;
                case TextNode:
                    return ValueKind.Text;
                case BooleanNode:
                    return ValueKind.Boolean;
                case FieldNode reference:
                    var field = resolve(reference.Text);
                    if (aggregation is null)
                    {
                        if (field.Kind == FieldKind.Dimension)
                            throw new FormulaException(Strings.Format(Strings.FormulaDimensionOutsideFunction, Text(node)));
                        return ValueKind.Number;
                    }
                    if (field.Kind == FieldKind.Measure)
                    {
                        if (field.IsCalculated) throw new FormulaException(Strings.Format(Strings.FormulaNotAggregatable, Text(node), aggregation));
                        return ValueKind.Number;
                    }
                    return field.DataType switch
                    {
                        DataType.Number => ValueKind.Number,
                        DataType.Date => ValueKind.Date,
                        DataType.Boolean => ValueKind.Boolean,
                        _ => ValueKind.Text,
                    };
                case NegateNode negate:
                    Require(negate.Operand, Kind(negate.Operand, aggregation), ValueKind.Number);
                    return ValueKind.Number;
                case NotNode not:
                    Require(not.Operand, Kind(not.Operand, aggregation), ValueKind.Boolean);
                    return ValueKind.Boolean;
                case BinaryNode { Operator: "AND" or "OR" } logical:
                    Require(logical.Left, Kind(logical.Left, aggregation), ValueKind.Boolean);
                    Require(logical.Right, Kind(logical.Right, aggregation), ValueKind.Boolean);
                    return ValueKind.Boolean;
                case BinaryNode { Operator: "+" or "-" or "*" or "/" } arithmetic:
                    Require(arithmetic.Left, Kind(arithmetic.Left, aggregation), ValueKind.Number);
                    Require(arithmetic.Right, Kind(arithmetic.Right, aggregation), ValueKind.Number);
                    return ValueKind.Number;
                case BinaryNode comparison:
                    Compare(comparison, Kind(comparison.Left, aggregation), Kind(comparison.Right, aggregation));
                    return ValueKind.Boolean;
                case CallNode call:
                    return Call(call, aggregation);
                default:
                    throw new ArgumentOutOfRangeException(nameof(node));
            }
        }

        private ValueKind Call(CallNode call, string? aggregation)
        {
            var function = call.Function;
            if (call.Arguments.Count < function.MinArguments || call.Arguments.Count > function.MaxArguments)
                throw new FormulaException(Strings.Format(Strings.FormulaUsage, function.Usage));
            if (function.Name == "IF")
            {
                Require(call.Arguments[0], Kind(call.Arguments[0], aggregation), ValueKind.Boolean);
                foreach (var result in call.Arguments.Skip(1)) Require(result, Kind(result, aggregation), ValueKind.Number);
                return ValueKind.Number;
            }
            if (aggregation is not null) throw new FormulaException(Strings.Format(Strings.FormulaNestedAggregation, function.Name, aggregation));

            // The condition (COUNT's only argument, the others' second) selects the rows
            var hasValue = function.Name != "COUNT";
            if (hasValue && call.Arguments.Count > 0)
            {
                var value = Kind(call.Arguments[0], function.Name);
                if (function.Name != "DISTINCTCOUNT") Require(call.Arguments[0], value, ValueKind.Number);
                else if (value == ValueKind.Boolean) throw new FormulaException(Strings.Format(Strings.FormulaUsage, function.Usage));
            }
            if (call.Arguments.Skip(hasValue ? 1 : 0).FirstOrDefault() is { } condition)
                Require(condition, Kind(condition, function.Name), ValueKind.Boolean);
            return ValueKind.Number;
        }

        private void Compare(BinaryNode comparison, ValueKind left, ValueKind right)
        {
            // A date is written as a text: "2024-12-31"
            if (left == ValueKind.Date && comparison.Right is TextNode rightDate) { RequireDate(rightDate); right = ValueKind.Date; }
            if (right == ValueKind.Date && comparison.Left is TextNode leftDate) { RequireDate(leftDate); left = ValueKind.Date; }
            if (left != right)
                throw new FormulaException(Strings.Format(Strings.FormulaMismatch, Text(comparison.Left), Text(comparison.Right)));
            if (left is ValueKind.Text or ValueKind.Boolean && comparison.Operator is not ("=" or "<>"))
                throw new FormulaException(Strings.Format(Strings.FormulaEqualityOnly, Text(comparison)));
        }

        private void RequireDate(TextNode text)
        {
            if (!DateTime.TryParse(text.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                throw new FormulaException(Strings.Format(Strings.FormulaNotADate, Text(text)));
        }

        private void Require(Node node, ValueKind actual, ValueKind expected)
        {
            if (actual == expected) return;
            throw new FormulaException(Strings.Format(
                expected == ValueKind.Boolean ? Strings.FormulaNotACondition : Strings.FormulaNotANumber, Text(node)));
        }

        private string Text(Node node) => formula[node.Start..node.End].Trim();
    }

    // --- Building: the checked formula as an expression of the query

    private sealed class Builder(Expression rows, Type rowType, Func<string, Expression> measure, Func<string, LambdaExpression> selector)
    {
        private static readonly ConstantExpression Empty = Expression.Constant(null, typeof(double?));

        // The value for the group: measures are aggregated as declared, functions read the rows
        public Expression Group(Node node) => node switch
        {
            NumberNode number => Expression.Constant(number.Value, typeof(double?)),
            TextNode text => Expression.Constant(text.Value),
            FieldNode reference => Number(measure(reference.Text)),
            CallNode call when call.Function.Name != "IF" => Aggregate(call),
            _ => Combine(node, Group),
        };

        // The value for one row of the group
        private Expression Row(Node node, ParameterExpression row) => node switch
        {
            NumberNode number => Expression.Constant(number.Value, typeof(double?)),
            TextNode text => Expression.Constant(text.Value),
            FieldNode reference => new ParameterReplacer(selector(reference.Text), row).Body,
            _ => Combine(node, n => Row(n, row)),
        };

        // Operators, comparisons and IF, built the same way for the group and for its rows
        private static Expression Combine(Node node, Func<Node, Expression> build)
        {
            switch (node)
            {
                case BooleanNode boolean:
                    return Expression.Constant(boolean.Value);
                case NegateNode negate:
                    return Expression.Negate(Number(build(negate.Operand)));
                case NotNode not:
                    return Expression.Not(Condition(build(not.Operand)));
                case CallNode { Function.Name: "IF" } call:
                    return Expression.Condition(
                        Condition(build(call.Arguments[0])),
                        Number(build(call.Arguments[1])),
                        call.Arguments.Count > 2 ? Number(build(call.Arguments[2])) : Empty);
                case BinaryNode { Operator: "AND" or "OR" } logical:
                    var (first, second) = (Condition(build(logical.Left)), Condition(build(logical.Right)));
                    return logical.Operator == "AND" ? Expression.AndAlso(first, second) : Expression.OrElse(first, second);
                case BinaryNode { Operator: "+" or "-" or "*" or "/" } arithmetic:
                    var (left, right) = (Number(build(arithmetic.Left)), Number(build(arithmetic.Right)));
                    return arithmetic.Operator switch
                    {
                        "+" => Expression.Add(left, right),
                        "-" => Expression.Subtract(left, right),
                        "*" => Expression.Multiply(left, right),
                        _ => Expression.Condition(
                            Expression.Equal(right, Expression.Constant(0d, typeof(double?))), Empty, Expression.Divide(left, right)),
                    };
                case BinaryNode comparison:
                    return Compare(comparison.Operator, build(comparison.Left), build(comparison.Right));
                default:
                    throw new ArgumentOutOfRangeException(nameof(node));
            }
        }

        private Expression Aggregate(CallNode call)
        {
            var row = Expression.Parameter(rowType, "r");
            var function = call.Function.Name;
            var arguments = call.Arguments;
            var condition = (function == "COUNT" ? arguments.FirstOrDefault() : arguments.Skip(1).FirstOrDefault()) is { } filter
                ? Condition(Row(filter, row))
                : null;
            Expression Where(Expression? test) =>
                test is null ? rows : Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [rowType], rows, Expression.Lambda(test, row));

            if (function == "COUNT")
                return Number(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [rowType], Where(condition)));
            if (function == "DISTINCTCOUNT")
            {
                // Missing values are not counted, as in SQL's COUNT(DISTINCT ...)
                var value = Row(arguments[0], row);
                var present = value.Type.IsValueType && Nullable.GetUnderlyingType(value.Type) is null
                    ? null
                    : Expression.NotEqual(value, Expression.Constant(null, value.Type));
                var kept = present is null ? condition : condition is null ? present : Expression.AndAlso(present, condition);
                var values = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [rowType, value.Type], Where(kept), Expression.Lambda(value, row));
                var distinct = Expression.Call(typeof(Enumerable), nameof(Enumerable.Distinct), [value.Type], values);
                return Number(Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [value.Type], distinct));
            }

            // The rows outside the condition give no value: ignored by the sum, the average, the minimum and the maximum
            var number = Number(Row(arguments[0], row));
            if (condition is not null) number = Expression.Condition(condition, number, Empty);
            var method = function switch
            {
                "SUM" => nameof(Enumerable.Sum),
                "AVERAGE" => nameof(Enumerable.Average),
                "MIN" => nameof(Enumerable.Min),
                _ => nameof(Enumerable.Max),
            };
            return Expression.Call(typeof(Enumerable), method, [rowType], rows, Expression.Lambda(number, row));
        }

        private static Expression Number(Expression value) =>
            value.Type == typeof(double?) ? value : Expression.Convert(value, typeof(double?));

        private static Expression Condition(Expression value) =>
            value.Type == typeof(bool) ? value : Expression.Equal(value, Expression.Constant(true, value.Type));

        private static Expression Compare(string comparison, Expression left, Expression right)
        {
            if (IsNumber(left.Type) || IsNumber(right.Type)) (left, right) = (Number(left), Number(right));
            else (left, right) = Align(left, right);
            return comparison switch
            {
                "=" => Expression.Equal(left, right),
                "<>" => Expression.NotEqual(left, right),
                "<" => Expression.LessThan(left, right),
                "<=" => Expression.LessThanOrEqual(left, right),
                ">" => Expression.GreaterThan(left, right),
                _ => Expression.GreaterThanOrEqual(left, right),
            };
        }

        // A text written in the formula takes the type of the value it is compared with (a date, an enum...), and a
        // value compared with a nullable one becomes nullable
        private static (Expression, Expression) Align(Expression left, Expression right)
        {
            if (left is ConstantExpression { Value: string leftText } && right.Type != typeof(string))
                left = Expression.Constant(InvariantValue.Parse(leftText, right.Type), right.Type);
            if (right is ConstantExpression { Value: string rightText } && left.Type != typeof(string))
                right = Expression.Constant(InvariantValue.Parse(rightText, left.Type), left.Type);
            if (left.Type != right.Type)
            {
                if (Nullable.GetUnderlyingType(right.Type) == left.Type) left = Expression.Convert(left, right.Type);
                else if (Nullable.GetUnderlyingType(left.Type) == right.Type) right = Expression.Convert(right, left.Type);
            }
            return (left, right);
        }

        private static bool IsNumber(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return (type.IsPrimitive && type != typeof(bool) && type != typeof(char)) || type == typeof(decimal);
        }
    }

    private sealed class ParameterReplacer(LambdaExpression lambda, ParameterExpression to) : ExpressionVisitor
    {
        public Expression Body => Visit(lambda.Body);

        protected override Expression VisitParameter(ParameterExpression node) => node == lambda.Parameters[0] ? to : node;
    }

    // --- Parsing:
    //     condition  := conjunction (OR conjunction)*      conjunction := negation (AND negation)*
    //     negation   := NOT negation | comparison           comparison  := expression (('=' | '<>' | '<' | '<=' | '>' | '>=') expression)?
    //     expression := term (('+' | '-') term)*           term        := factor (('*' | '/') factor)*
    //     factor     := number | "text" | TRUE | FALSE | [field] | FUNCTION '(' (condition (';' condition)*)? ')'
    //                 | '(' condition ')' | '-' factor

    private static Node Parse(string formula)
    {
        var tokens = Tokenize(formula);
        if (tokens.Count == 1) throw new FormulaException(Strings.FormulaEmpty);
        var parser = new Parser(tokens);
        var node = parser.Condition();
        if (parser.Current.Kind != TokenKind.End) throw parser.Unexpected();
        return node;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        public Token Current => tokens[_index];

        private int End => _index == 0 ? 0 : tokens[_index - 1].End;

        public Node Condition()
        {
            var node = Conjunction();
            while (IsWord("OR"))
            {
                Next();
                node = new BinaryNode("OR", node, Conjunction(), node.Start, End);
            }
            return node;
        }

        private Node Conjunction()
        {
            var node = Negation();
            while (IsWord("AND"))
            {
                Next();
                node = new BinaryNode("AND", node, Negation(), node.Start, End);
            }
            return node;
        }

        private Node Negation()
        {
            if (!IsWord("NOT")) return Comparison();
            var start = Next().Start;
            var operand = Negation();
            return new NotNode(operand, start, End);
        }

        private Node Comparison()
        {
            var node = Expression();
            if (Current.Kind != TokenKind.Comparison) return node;
            var op = Next().Text;
            return new BinaryNode(op, node, Expression(), node.Start, End);
        }

        private Node Expression()
        {
            var node = Term();
            while (Current.Kind == TokenKind.Operator && Current.Text is "+" or "-")
            {
                var op = Next().Text;
                node = new BinaryNode(op, node, Term(), node.Start, End);
            }
            return node;
        }

        private Node Term()
        {
            var node = Factor();
            while (Current.Kind == TokenKind.Operator && Current.Text is "*" or "/")
            {
                var op = Next().Text;
                node = new BinaryNode(op, node, Factor(), node.Start, End);
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
                    return new NumberNode(double.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), token.Start, token.End);
                case TokenKind.Text:
                    Next();
                    return new TextNode(token.Text, token.Start, token.End);
                case TokenKind.Field:
                    Next();
                    return new FieldNode(token.Text, token.Start, token.End);
                case TokenKind.Word when IsWord("TRUE") || IsWord("FALSE"):
                    Next();
                    return new BooleanNode(IsWord("TRUE", token), token.Start, token.End);
                case TokenKind.Word:
                    return Call();
                case TokenKind.Operator when token.Text == "-":
                    Next();
                    var operand = Factor();
                    return new NegateNode(operand, token.Start, End);
                case TokenKind.Open:
                    Next();
                    var inner = Condition();
                    if (Current.Kind != TokenKind.Close) throw Current.Kind == TokenKind.End ? new FormulaException(Strings.FormulaMissingParenthesis) : Unexpected();
                    Next();
                    return inner;
                case TokenKind.End:
                    throw new FormulaException(Strings.Format(Strings.FormulaExpectedOperand, token.Start + 1));
                default:
                    throw Unexpected();
            }
        }

        private Node Call()
        {
            var name = Next();
            var function = FindFunction(name.Text) ?? throw new FormulaException(Strings.Format(Strings.FormulaUnknownFunction, name.Text));
            if (Current.Kind != TokenKind.Open) throw new FormulaException(Strings.Format(Strings.FormulaUsage, function.Usage));
            Next();
            var arguments = new List<Node>();
            if (Current.Kind != TokenKind.Close)
            {
                arguments.Add(Condition());
                while (Current.Kind == TokenKind.Separator)
                {
                    Next();
                    arguments.Add(Condition());
                }
            }
            if (Current.Kind != TokenKind.Close) throw Current.Kind == TokenKind.End ? new FormulaException(Strings.FormulaMissingParenthesis) : Unexpected();
            Next();
            return new CallNode(function, arguments, name.Start, End);
        }

        public FormulaException Unexpected() =>
            new(Strings.Format(Strings.FormulaUnexpected, Current.Text, Current.Start + 1));

        private bool IsWord(string word, Token? token = null) =>
            (token ?? Current) is { Kind: TokenKind.Word } t && string.Equals(t.Text, word, StringComparison.OrdinalIgnoreCase);

        private Token Next() => tokens[_index++];
    }

    private static List<Token> Tokenize(string formula)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < formula.Length)
        {
            var c = formula[i];
            var start = i;
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsAsciiDigit(formula[i + 1])))
            {
                while (i < formula.Length && (char.IsAsciiDigit(formula[i]) || formula[i] == '.')) i++;
                // "0,5" is a decimal comma rather than two arguments
                if (i + 1 < formula.Length && formula[i] == ',' && char.IsAsciiDigit(formula[i + 1]))
                    throw new FormulaException(Strings.FormulaDecimalComma);
                var number = formula[start..i];
                if (number.Count(ch => ch == '.') > 1) throw new FormulaException(Strings.Format(Strings.FormulaUnexpected, number, start + 1));
                tokens.Add(new Token(TokenKind.Number, number, start, i));
            }
            else if (c is '[' or '"')
            {
                // [field] or "text", the closing character doubled inside
                var close = c == '[' ? ']' : '"';
                var text = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= formula.Length) throw new FormulaException(c == '[' ? Strings.FormulaMissingBracket : Strings.FormulaMissingQuote);
                    if (formula[i] == close)
                    {
                        if (i + 1 < formula.Length && formula[i + 1] == close)
                        {
                            text.Append(close);
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    text.Append(formula[i++]);
                }
                tokens.Add(c == '[' ? new Token(TokenKind.Field, text.ToString().Trim(), start, i) : new Token(TokenKind.Text, text.ToString(), start, i));
            }
            else if (char.IsAsciiLetter(c))
            {
                while (i < formula.Length && (char.IsAsciiLetterOrDigit(formula[i]) || formula[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Word, formula[start..i], start, i));
            }
            else if (c is '<' or '>' or '=')
            {
                i++;
                if (c != '=' && i < formula.Length && (formula[i] == '=' || (c == '<' && formula[i] == '>'))) i++;
                tokens.Add(new Token(TokenKind.Comparison, formula[start..i], start, i));
            }
            else
            {
                var kind = c switch
                {
                    '+' or '-' or '*' or '/' => TokenKind.Operator,
                    '(' => TokenKind.Open,
                    ')' => TokenKind.Close,
                    ';' or ',' => TokenKind.Separator,
                    _ => throw new FormulaException(Strings.Format(Strings.FormulaUnexpected, c, i + 1)),
                };
                tokens.Add(new Token(kind, c.ToString(), start, ++i));
            }
        }
        tokens.Add(new Token(TokenKind.End, "", formula.Length, formula.Length));
        return tokens;
    }

    private enum TokenKind { Number, Text, Field, Word, Operator, Comparison, Open, Close, Separator, End }

    private sealed record Token(TokenKind Kind, string Text, int Start, int End);

    // Each node knows where it is written, to quote it in the problems
    private abstract record Node(int Start, int End);

    private sealed record NumberNode(double Value, int Start, int End) : Node(Start, End);

    private sealed record TextNode(string Value, int Start, int End) : Node(Start, End);

    private sealed record BooleanNode(bool Value, int Start, int End) : Node(Start, End);

    private sealed record FieldNode(string Text, int Start, int End) : Node(Start, End);

    private sealed record NegateNode(Node Operand, int Start, int End) : Node(Start, End);

    private sealed record NotNode(Node Operand, int Start, int End) : Node(Start, End);

    /// <summary>An arithmetic operator, a comparison, AND or OR.</summary>
    private sealed record BinaryNode(string Operator, Node Left, Node Right, int Start, int End) : Node(Start, End);

    private sealed record CallNode(FormulaFunction Function, IReadOnlyList<Node> Arguments, int Start, int End) : Node(Start, End);

    private sealed class FormulaException(string message) : Exception(message);
}

/// <summary>A function of the <see cref="MeasureFormula"/> formulas, with its usage and description for the end user.</summary>
public sealed record FormulaFunction(string Name, int MinArguments, int MaxArguments)
{
    /// <summary>How to write it, such as <c>SUM(value; condition)</c>, in the EasyBI language.</summary>
    public string Usage => Strings.Named("FormulaUsage" + Name);

    /// <summary>What it computes, in the EasyBI language.</summary>
    public string Description => Strings.Named("FormulaDescription" + Name);
}
