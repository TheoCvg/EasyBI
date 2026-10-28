using System.Globalization;
using EasyBI.Abstractions;
using EasyBI.Core.Resources;

namespace EasyBI.Core;

// The formulas of the report's variables (see VariableFormula): read with the tokens and the parser of the measures'
public static partial class MeasureFormula
{
    // --- Reading: what a variable's formula defines, without the model

    internal static VariableDefinition? ReadVariable(string formula) => ParseVariable(formula, out var parsed) is null ? parsed!.Definition : null;

    private static Fault? ParseVariable(string formula, out ParsedVariable? parsed)
    {
        parsed = null;
        if (Tokenize(formula, out var tokens) is { } fault) return fault;
        if (tokens.Count == 1) return new Fault(Strings.FormulaEmpty, 0, formula.Length);
        var reader = new VariableReader(formula, tokens);
        var result = IsWord(tokens[0], "SELECT") ? reader.Set() : reader.IsParameter() ? reader.Parameter() : reader.Computed();
        if (reader.Fault is not null) return reader.Fault;
        parsed = result;
        return null;
    }

    private static bool IsWord(Token token, string word) =>
        token.Kind == TokenKind.Word && string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>A variable's formula as read: what it defines, and its parts to check against the model.</summary>
    private sealed record ParsedVariable(VariableDefinition Definition)
    {
        // A set: the dimension, the values listed, the conditions (each with whether it is on rows), the order
        public FieldNode? Field { get; init; }
        public IReadOnlyList<Node> Members { get; init; } = [];
        public IReadOnlyList<(Node Condition, bool OnRows)> Conditions { get; init; } = [];
        public FieldNode? TopVariable { get; init; }
        public Node? Order { get; init; }

        // A computed value: its formula
        public Node? Computed { get; init; }
    }

    private sealed class VariableReader(string formula, List<Token> tokens)
    {
        private int _index;

        public Fault? Fault { get; private set; }

        private Token Current => tokens[Math.Min(_index, tokens.Count - 1)];

        // --- Parameters: a value, optionally with BETWEEN min AND max [STEP step] or IN (choices)

        public bool IsParameter()
        {
            var after = tokens[0] is { Kind: TokenKind.Operator, Text: "-" } ? 2 : 1;
            return IsLiteral(0) && (tokens[after].Kind == TokenKind.End || IsWord(tokens[after], "BETWEEN") || IsWord(tokens[after], "IN"));
        }

        public ParsedVariable? Parameter()
        {
            var value = Literal();
            if (value is null) return null;
            var type = TypeOf(value);
            var definition = new ParameterDefinition { DataType = type, Value = Invariant(value, type) };
            if (Accept("BETWEEN"))
            {
                var minimum = Number();
                Expect("AND");
                var maximum = Number();
                double? step = Accept("STEP") ? Number() : null;
                if (Fault is not null) return null;
                if (type != DataType.Number) return Fail(Strings.VariableBetweenNumbers, value);
                if (minimum >= maximum || step <= 0) return Fail(Strings.VariableBetween, value.Start, Position);
                var number = ((NumberNode)value).Value;
                if (number < minimum || number > maximum)
                    return Fail(Strings.Format(Strings.VariableOutOfRange, Text(value), Format(minimum!.Value), Format(maximum!.Value)), value);
                definition = definition with { Minimum = minimum, Maximum = maximum, Step = step };
            }
            else if (Accept("IN"))
            {
                var choices = List();
                if (Fault is not null) return null;
                foreach (var choice in choices.Where(c => TypeOf(c) != type))
                    return Fail(Strings.Format(Strings.VariableChoiceType, Text(choice), Text(value)), choice);
                var invariant = choices.Select(c => Invariant(c, type)).ToArray();
                if (!invariant.Contains(definition.Value)) return Fail(Strings.Format(Strings.VariableNotAChoice, Text(value)), value);
                definition = definition with { Choices = invariant! };
            }
            End();
            return Fault is null ? new ParsedVariable(definition) : null;
        }

        // --- Sets: SELECT [TOP n] [dimension] [IN (values)] [WHERE condition] [ORDER BY expression [ASC | DESC]]

        public ParsedVariable? Set()
        {
            _index++;
            int? top = null;
            FieldNode? topVariable = null;
            if (Accept("TOP"))
            {
                switch (Current)
                {
                    case { Kind: TokenKind.Number } count when int.TryParse(count.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n):
                        top = n;
                        _index++;
                        break;
                    case { Kind: TokenKind.Field } reference when IsVariable(reference.Text):
                        topVariable = new FieldNode(reference.Text, reference.Start, reference.End);
                        _index++;
                        break;
                    default:
                        return Fail(Strings.VariableTop, Current);
                }
            }
            if (Current is not { Kind: TokenKind.Field } dimension || IsVariable(dimension.Text)) return Fail(Strings.VariableSelectField, Current);
            _index++;
            var field = new FieldNode(dimension.Text, dimension.Start, dimension.End);

            IReadOnlyList<Node> members = Accept("IN") ? List() : [];
            var conditions = new List<(Node, bool)>();
            if (Accept("WHERE"))
            {
                // Until ORDER BY, outside parentheses
                var condition = Clause(i => IsWord(tokens[i], "ORDER") && IsWord(tokens[i + 1], "BY"));
                if (condition is not null) conditions.AddRange(Terms(condition).Select(t => (t, false)));
            }
            Node? order = null;
            var descending = false;
            if (Accept("ORDER"))
            {
                Expect("BY");
                var last = tokens.Count - 2;
                var direction = last > _index && (IsWord(tokens[last], "DESC") || IsWord(tokens[last], "ASC"));
                descending = direction && IsWord(tokens[last], "DESC");
                order = Clause(i => direction && i == last);
                if (direction) _index++;
            }
            End();
            if (Fault is not null) return null;
            var definition = new SetDefinition(field.Text)
            {
                DataType = DataType.Text,
                Top = top,
                TopVariable = topVariable?.Text.Substring(1),
                Members = members.Count > 0 ? members.Select(m => Invariant(m, TypeOf(m))).ToArray() : null,
                Descending = descending,
                HasConditions = conditions.Count > 0,
                HasOrder = order is not null,
            };
            return new ParsedVariable(definition) { Field = field, Members = members, Conditions = conditions, TopVariable = topVariable, Order = order };
        }

        // The conditions joined by AND, each kept on rows or on groups
        private static IEnumerable<Node> Terms(Node condition) =>
            condition is BinaryNode { Operator: "AND" } and ? Terms(and.Left).Concat(Terms(and.Right)) : [condition];

        // --- Computed values: a formula of the measures' language

        public ParsedVariable? Computed()
        {
            var node = Clause(_ => false);
            End();
            return Fault is null ? new ParsedVariable(new ComputedDefinition()) { Computed = node } : null;
        }

        // --- Helpers

        // The tokens from here to the first one ending the clause (outside parentheses), read as a condition
        private Node? Clause(Func<int, bool> ends)
        {
            if (Fault is not null) return null;
            var start = _index;
            var depth = 0;
            while (tokens[_index].Kind != TokenKind.End && !(depth == 0 && ends(_index)))
            {
                depth += tokens[_index].Kind switch { TokenKind.Open => 1, TokenKind.Close => -1, _ => 0 };
                _index++;
            }
            var end = tokens[_index].Start;
            var clause = tokens.GetRange(start, _index - start);
            clause.Add(new Token(TokenKind.End, "", end, end));
            if (clause.Count == 1)
            {
                Fault = new Fault(Strings.Format(Strings.FormulaExpectedOperand, end + 1), end, end);
                return null;
            }
            var parser = new Parser(clause);
            var node = parser.Condition();
            if (parser.Current.Kind != TokenKind.End) parser.Unexpected();
            Fault ??= parser.Fault;
            return Fault is null ? node : null;
        }

        private bool IsLiteral(int index) => tokens[index] switch
        {
            { Kind: TokenKind.Number or TokenKind.Text } => true,
            { Kind: TokenKind.Operator, Text: "-" } => tokens[index + 1].Kind == TokenKind.Number,
            var token => IsWord(token, "TRUE") || IsWord(token, "FALSE"),
        };

        // A number, a text, TRUE or FALSE
        private Node? Literal()
        {
            if (Fault is not null) return null;
            var token = Current;
            if (!IsLiteral(_index))
            {
                Fail(Strings.Format(Strings.FormulaUnexpected, token.Text, token.Start + 1), token);
                return null;
            }
            _index++;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return new NumberNode(double.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), token.Start, token.End);
                case TokenKind.Text:
                    return new TextNode(token.Text, token.Start, token.End);
                case TokenKind.Operator:
                    var number = Current;
                    _index++;
                    return new NumberNode(-double.Parse(number.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), token.Start, number.End);
                default:
                    return new BooleanNode(IsWord(token, "TRUE"), token.Start, token.End);
            }
        }

        private double? Number()
        {
            var literal = Literal();
            if (literal is NumberNode number) return number.Value;
            if (literal is not null) Fail(Strings.Format(Strings.FormulaNotANumber, Text(literal)), literal);
            return null;
        }

        // ( literal ; literal ... )
        private IReadOnlyList<Node> List()
        {
            var items = new List<Node>();
            if (!Expect("(")) return items;
            while (Fault is null)
            {
                if (Literal() is { } item) items.Add(item);
                if (Current.Kind == TokenKind.Separator) _index++;
                else break;
            }
            Expect(")");
            return items;
        }

        private bool Accept(string word)
        {
            if (Fault is not null || !IsWord(Current, word)) return false;
            _index++;
            return true;
        }

        private bool Expect(string expected)
        {
            if (Fault is not null) return false;
            var found = expected switch
            {
                "(" => Current.Kind == TokenKind.Open,
                ")" => Current.Kind == TokenKind.Close,
                _ => IsWord(Current, expected),
            };
            if (found)
            {
                _index++;
                return true;
            }
            Fail(Current.Kind == TokenKind.End
                ? Strings.Format(Strings.VariableExpected, expected, Current.Start + 1)
                : Strings.Format(Strings.FormulaUnexpected, Current.Text, Current.Start + 1), Current);
            return false;
        }

        private void End()
        {
            if (Fault is null && Current.Kind != TokenKind.End)
                Fail(Strings.Format(Strings.FormulaUnexpected, Current.Text, Current.Start + 1), Current);
        }

        // Where the last token read ends
        private int Position => _index == 0 ? 0 : tokens[_index - 1].End;

        private string Text(Node node) => formula[node.Start..node.End].Trim();

        private ParsedVariable? Fail(string message, Node node) => Fail(message, node.Start, node.End);

        private ParsedVariable? Fail(string message, Token token) => Fail(message, token.Start, token.End);

        private ParsedVariable? Fail(string message, int start, int end)
        {
            Fault ??= new Fault(message, start, end);
            return null;
        }

        private static string Format(double value) => value.ToString(EasyBILocalization.FormatCulture);
    }

    // The type of a literal: a text written as yyyy-MM-dd is a date
    private static DataType TypeOf(Node literal) => literal switch
    {
        NumberNode => DataType.Number,
        BooleanNode => DataType.Boolean,
        TextNode text when IsDate(text.Value) => DataType.Date,
        _ => DataType.Text,
    };

    private static bool IsDate(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    // A literal in invariant form, as filter values are
    private static string? Invariant(Node literal, DataType type) => literal switch
    {
        NumberNode number => number.Value.ToString("R", CultureInfo.InvariantCulture),
        BooleanNode boolean => boolean.Value ? bool.TrueString : bool.FalseString,
        TextNode text => text.Value,
        _ => null,
    };

    // --- Checking against the model

    internal static FormulaProblem? CheckVariable(
        string text,
        ModelMetadata metadata,
        out string? stored,
        IEnumerable<CalculatedMeasure>? measures,
        string? name,
        IEnumerable<ReportVariable>? variables,
        bool byCaption)
    {
        stored = null;
        var all = variables?.ToArray() ?? [];
        // A variable reads the others, not itself: its own name is left out of the scope
        var scope = new Scope(metadata, measures, null, all.Where(v => v.Name != name));
        var fault = ParseVariable(text, out var parsed) ?? CheckVariable(text, metadata, scope, parsed!, byCaption)
            ?? VariableCycle(text, name, all);
        if (fault is not null)
        {
            return fault.Start < 0
                ? new FormulaProblem(fault.Message, 0, text.Length)
                : new FormulaProblem(fault.Message, fault.Start, Math.Max(fault.End - fault.Start, 0));
        }
        Tokenize(text, out var tokens);
        var references = tokens.Where(t => t.Kind == TokenKind.Field && !IsVariable(t.Text)).Select(t => t.Text).Distinct()
            .ToDictionary(reference => reference, reference => scope.Resolve(reference, byCaption, out _)!.Name);
        stored = Rewrite(text, reference => references.TryGetValue(reference, out var named) ? named : reference);
        return null;
    }

    private static Fault? CheckVariable(string text, ModelMetadata metadata, Scope scope, ParsedVariable parsed, bool byCaption)
    {
        Resolver resolve = (string reference, out string? problem) => scope.Resolve(reference, byCaption, out problem);
        var checker = new Checker(text, metadata, resolve, scope.Variable);
        if (parsed.Computed is { } computed) return checker.Check(computed) ?? scope.CheckCycles(computed, byCaption);
        if (parsed.Field is not { } dimension) return null;

        // A set: the values of a dimension
        if (resolve(dimension.Text, out var unknown) is not { } field) return new Fault(unknown!, dimension.Start, dimension.End);
        if (field.Kind != FieldKind.Dimension) return new Fault(Strings.Format(Strings.VariableSetNotDimension, dimension.Text), dimension.Start, dimension.End);
        foreach (var member in parsed.Members)
        {
            var type = TypeOf(member);
            if (type != field.DataType && !(type == DataType.Number && field.DataType == DataType.Text))
                return new Fault(Strings.Format(Strings.VariableMemberType, text[member.Start..member.End], dimension.Text), member.Start, member.End);
        }
        if (parsed.TopVariable is { } top)
        {
            switch (scope.Variable(top.Text))
            {
                case null: return new Fault(Strings.Format(Strings.VariableUnknown, top.Text.Substring(1)), top.Start, top.End);
                case { DataType: not DataType.Number } or { Kind: VariableKind.Set }: return new Fault(Strings.Format(Strings.VariableNotNumber, top.Text.Substring(1)), top.Start, top.End);
            }
        }
        foreach (var (condition, _) in parsed.Conditions)
        {
            // On rows when it only reads dimensions and values: a condition on measures keeps groups
            var fault = IsOnRows(condition, reference => scope.Resolve(reference, byCaption, out _) is { Kind: FieldKind.Measure })
                ? checker.CheckCondition(condition, "WHERE") ?? RowFilter(condition, reference => resolve(reference, out _), _ => "0", text).Fault
                : checker.CheckCondition(condition, null) ?? scope.CheckCycles(condition, byCaption);
            if (fault is not null) return fault;
        }
        return parsed.Order is { } order ? checker.Check(order) ?? scope.CheckCycles(order, byCaption) : null;
    }

    // On rows when it only reads dimensions, variables and values: a condition on measures or with a function keeps groups
    private static bool IsOnRows(Node condition, Func<string, bool> isMeasure) => condition switch
    {
        CallNode => false,
        FieldNode reference => IsVariable(reference.Text) || !isMeasure(reference.Text),
        NegateNode negate => IsOnRows(negate.Operand, isMeasure),
        NotNode not => IsOnRows(not.Operand, isMeasure),
        BinaryNode binary => IsOnRows(binary.Left, isMeasure) && IsOnRows(binary.Right, isMeasure),
        _ => true,
    };

    // A variable using itself through others
    private static Fault? VariableCycle(string text, string? name, IReadOnlyList<ReportVariable> variables)
    {
        if (name is null) return null;
        List<string>? Cycle(string formula, List<string> path)
        {
            foreach (var used in Variables(formula))
            {
                if (path.Contains(used)) return [.. path.SkipWhile(p => p != used), used];
                if (variables.FirstOrDefault(v => v.Name == used) is not { } next) continue;
                path.Add(used);
                if (Cycle(next.Formula, path) is { } found) return found;
                path.RemoveAt(path.Count - 1);
            }
            return null;
        }
        return Cycle(text, [name]) is { } cycle ? new Fault(Strings.Format(Strings.VariableCycle, string.Join(" → ", cycle.Select(c => "[@" + c + "]")))) : null;
    }

    // --- Querying a set: its conditions as filters and measures

    /// <summary>
    /// The parts of a stored set's formula: its conditions on rows and on groups, its order, as texts.
    /// <paramref name="isMeasure"/> tells whether a name is a measure of the model or of the report.
    /// </summary>
    internal static (IReadOnlyList<string> Rows, IReadOnlyList<string> Groups, string? Order) SetParts(string formula, Func<string, bool> isMeasure)
    {
        if (ParseVariable(formula, out var parsed) is not null || parsed!.Field is null) return ([], [], null);
        var rows = new List<string>();
        var groups = new List<string>();
        foreach (var (condition, _) in parsed.Conditions)
        {
            var text = formula[condition.Start..condition.End];
            (IsOnRows(condition, isMeasure) ? rows : groups).Add(text);
        }
        return (rows, groups, parsed.Order is { } order ? formula[order.Start..order.End] : null);
    }

    /// <summary>
    /// A condition on the rows of a stored set's formula as a filter. <paramref name="field"/> gives the model's field
    /// of a name, <paramref name="variable"/> the value of a variable in invariant form.
    /// </summary>
    internal static FilterSpec ToRowFilter(string condition, Func<string, FieldDescriptor?> field, Func<string, string?> variable)
    {
        if (Parse(condition, out var tree) is { } fault) throw new InvalidOperationException(fault.Message);
        var (filter, problem) = RowFilter(tree!, field, variable, condition);
        return filter ?? throw new InvalidOperationException(problem!.Message);
    }

    /// <summary>
    /// A condition on rows as a filter: dimensions compared with values (<c>[Region] = "North"</c>, <c>[Date] &gt;=
    /// "2025-01-01"</c>, <c>[Quantity] &gt; 10</c>), joined by AND, OR and NOT.
    /// </summary>
    private static (FilterSpec? Filter, Fault? Fault) RowFilter(Node node, Func<string, FieldDescriptor?> field, Func<string, string?> variable,
        string formula, bool negated = false)
    {
        switch (node)
        {
            case NotNode not:
                return RowFilter(not.Operand, field, variable, formula, !negated);
            case BinaryNode { Operator: "AND" or "OR" } logical:
                var left = RowFilter(logical.Left, field, variable, formula, negated);
                if (left.Fault is not null) return left;
                var right = RowFilter(logical.Right, field, variable, formula, negated);
                if (right.Fault is not null) return right;
                // NOT (a AND b) = NOT a OR NOT b
                var all = (logical.Operator == "AND") != negated;
                return (all ? FilterSpec.AnyOf([[left.Filter!, right.Filter!]]) : FilterSpec.AnyOf([[left.Filter!], [right.Filter!]]), null);
            case BinaryNode { Operator: "=" or "<>" or "<" or "<=" or ">" or ">=" } comparison:
                var (reference, value, op) = comparison.Left is FieldNode { Text: var l } && !IsVariable(l)
                    ? (comparison.Left as FieldNode, comparison.Right, comparison.Operator)
                    : (comparison.Right as FieldNode, comparison.Left, Flip(comparison.Operator));
                if (reference is null || IsVariable(reference.Text) || field(reference.Text) is not { Kind: FieldKind.Dimension } dimension)
                    break;
                string? text;
                switch (value)
                {
                    case FieldNode v when IsVariable(v.Text): text = variable(v.Text.Substring(1)); break;
                    case NumberNode or BooleanNode or TextNode: text = Invariant(value, TypeOf(value)); break;
                    case NegateNode { Operand: NumberNode number }: text = (-number.Value).ToString("R", CultureInfo.InvariantCulture); break;
                    default: return (null, new Fault(Strings.VariableRowCondition, node.Start, node.End));
                }
                if (negated) op = Negate(op);
                return (Compare(dimension, op, text), null);
        }
        return (null, new Fault(Strings.VariableRowCondition, node.Start, node.End));
    }

    private static string Flip(string op) => op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op };

    private static string Negate(string op) => op switch
    {
        "=" => "<>", "<>" => "=", "<" => ">=", "<=" => ">", ">" => "<=", _ => "<",
    };

    private static FilterSpec Compare(FieldDescriptor dimension, string op, string? value)
    {
        var name = dimension.Name;
        if (dimension.DataType == DataType.Date)
        {
            // Days, from one or until one, included or not; a missing date keeps no row
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) return new FilterSpec(name, FilterOperator.In, []);
            DateOnly Day(int offset) => DateOnly.FromDateTime(date.Date.AddDays(offset));
            return op switch
            {
                "=" => PeriodFilters.Between(name, Day(0), Day(0)),
                "<>" => FilterSpec.AnyOf([[PeriodFilters.Between(name, null, Day(-1))], [PeriodFilters.Between(name, Day(1), null)]]),
                ">=" => PeriodFilters.Between(name, Day(0), null),
                ">" => PeriodFilters.Between(name, Day(1), null),
                "<=" => PeriodFilters.Between(name, null, Day(0)),
                _ => PeriodFilters.Between(name, null, Day(-1)),
            };
        }
        if (op is "=" or "<>") return new FilterSpec(name, op == "=" ? FilterOperator.In : FilterOperator.NotIn, [value]);
        var @operator = op switch
        {
            ">" => FilterOperator.GreaterThan,
            ">=" => FilterOperator.GreaterThanOrEqual,
            "<" => FilterOperator.LessThan,
            _ => FilterOperator.LessThanOrEqual,
        };
        return new FilterSpec(name, @operator, [value]);
    }
}
