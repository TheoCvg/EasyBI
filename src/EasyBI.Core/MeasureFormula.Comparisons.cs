using EasyBI.Core.Resources;

namespace EasyBI.Core;

// The formulas of comparisons (ComparisonFormula): the same syntax, over two numbers instead of a model
public static partial class MeasureFormula
{
    /// <summary>
    /// Checks a formula over the two values of a comparison; <paramref name="resolve"/> gives the stored name of a
    /// reference (null when unknown).
    /// </summary>
    internal static FormulaProblem? CheckComparison(string text, Func<string, string?> resolve, out string? stored)
    {
        stored = null;
        var fault = Parse(text, out var tree) ?? CheckComparison(tree!, text, resolve, number: true);
        if (fault is not null)
            return fault.Start < 0
                ? new FormulaProblem(fault.Message, 0, text.Length)
                : new FormulaProblem(fault.Message, fault.Start, Math.Max(fault.End - fault.Start, 0));
        stored = Rewrite(text, reference => resolve(reference) ?? reference);
        return null;
    }

    /// <summary>The formula as a function of the two values; null when it is not valid.</summary>
    internal static Func<double?, double?, double?>? CompileComparison(string formula, Func<string, string?> resolve)
    {
        if (Parse(formula, out var tree) is not null || CheckComparison(tree!, formula, resolve, number: true) is not null) return null;
        return (value, compared) => Number(tree!, name => resolve(name) == ComparisonFormula.Value ? value : compared);
    }

    // number: the node must give a number (otherwise a condition)
    private static Fault? CheckComparison(Node node, string text, Func<string, string?> resolve, bool number)
    {
        Fault Not(string message) => new(Strings.Format(message, text[node.Start..node.End]), node.Start, node.End);
        switch (node)
        {
            case NumberNode:
                return number ? null : Not(Strings.FormulaNotACondition);
            case FieldNode field:
                if (resolve(field.Text) is null)
                    return new Fault(Strings.Format(Strings.ComparisonFormulaUnknown, field.Text, ComparisonFormula.ValueCaption, ComparisonFormula.ComparedCaption),
                        field.Start, field.End);
                return number ? null : Not(Strings.FormulaNotACondition);
            case NegateNode negate:
                return number ? CheckComparison(negate.Operand, text, resolve, number: true) : Not(Strings.FormulaNotACondition);
            case NotNode not:
                return number ? Not(Strings.FormulaNotANumber) : CheckComparison(not.Operand, text, resolve, number: false);
            case BinaryNode { Operator: "+" or "-" or "*" or "/" } arithmetic:
                return number
                    ? CheckComparison(arithmetic.Left, text, resolve, number: true) ?? CheckComparison(arithmetic.Right, text, resolve, number: true)
                    : Not(Strings.FormulaNotACondition);
            case BinaryNode { Operator: "AND" or "OR" } logical:
                return number
                    ? Not(Strings.FormulaNotANumber)
                    : CheckComparison(logical.Left, text, resolve, number: false) ?? CheckComparison(logical.Right, text, resolve, number: false);
            case BinaryNode comparison:
                return number
                    ? Not(Strings.FormulaNotANumber)
                    : CheckComparison(comparison.Left, text, resolve, number: true) ?? CheckComparison(comparison.Right, text, resolve, number: true);
            case CallNode { Function.Name: "IF", By: null } call when call.Arguments.Count is 2 or 3:
                if (!number) return Not(Strings.FormulaNotACondition);
                return CheckComparison(call.Arguments[0], text, resolve, number: false)
                    ?? call.Arguments.Skip(1).Select(a => CheckComparison(a, text, resolve, number: true)).FirstOrDefault(f => f is not null);
            case CallNode { Function.Name: "IF" } call:
                return new Fault(Strings.Format(Strings.FormulaUsage, call.Function.Usage), call.Start, call.End);
            case CallNode call:
                return new Fault(Strings.Format(Strings.ComparisonFormulaFunction, call.Function.Name), call.Start, call.End);
            default:
                return Not(number ? Strings.FormulaNotANumber : Strings.FormulaNotACondition);
        }
    }

    private static double? Number(Node node, Func<string, double?> value)
    {
        switch (node)
        {
            case NumberNode n:
                return n.Value;
            case FieldNode field:
                return value(field.Text);
            case NegateNode negate:
                return -Number(negate.Operand, value);
            case BinaryNode binary:
                var (left, right) = (Number(binary.Left, value), Number(binary.Right, value));
                return binary.Operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    _ => right == 0 ? null : left / right,
                };
            case CallNode call:
                return Condition(call.Arguments[0], value) switch
                {
                    true => Number(call.Arguments[1], value),
                    false when call.Arguments.Count == 3 => Number(call.Arguments[2], value),
                    _ => null,
                };
            default:
                return null;
        }
    }

    // A comparison with a missing value is neither true nor false: the IF is then empty
    private static bool? Condition(Node node, Func<string, double?> value)
    {
        switch (node)
        {
            case NotNode not:
                return !Condition(not.Operand, value);
            case BinaryNode { Operator: "AND" } and:
                return Condition(and.Left, value) is { } l && Condition(and.Right, value) is { } r ? l && r : null;
            case BinaryNode { Operator: "OR" } or:
                return Condition(or.Left, value) is { } a && Condition(or.Right, value) is { } b ? a || b : null;
            case BinaryNode comparison:
                if (Number(comparison.Left, value) is not { } left || Number(comparison.Right, value) is not { } right) return null;
                return comparison.Operator switch
                {
                    "=" => left == right,
                    "<>" => left != right,
                    "<" => left < right,
                    "<=" => left <= right,
                    ">" => left > right,
                    _ => left >= right,
                };
            default:
                return null;
        }
    }
}
