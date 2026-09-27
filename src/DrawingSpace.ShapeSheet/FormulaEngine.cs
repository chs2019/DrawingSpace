using System.Collections.Concurrent;
using System.Globalization;

namespace DrawingSpace.ShapeSheet;

/// <summary>Pure evaluator. References are provided by a document adapter; side-effecting Office functions are never executed.</summary>
public sealed class FormulaEngine
{
    private readonly ConcurrentDictionary<string, ParsedFormula> _cache = new(StringComparer.Ordinal);
    public FormulaValue Evaluate(string formula, Func<string, FormulaValue>? resolve = null)
    {
        try
        {
            if (_cache.Count > 2048) _cache.Clear();
            var parsed = _cache.GetOrAdd(formula, FormulaParser.Parse);
            return Evaluate(parsed, resolve);
        }
        catch (FormatException ex) { return FormulaValue.Error("#PARSE!", ex.Message); }
        catch (OverflowException) { return FormulaValue.Error("#NUM!", "Numeric overflow."); }
        catch (ArgumentException ex) { return FormulaValue.Error("#VALUE!", ex.Message); }
    }
    public FormulaValue Evaluate(ParsedFormula formula, Func<string, FormulaValue>? resolve = null)
    {
        var budget = FormulaParser.MaximumTokens * 4;
        return Visit(formula.Root, resolve ?? (name => FormulaValue.Error("#REF!", name)), ref budget, 0);
    }
    private FormulaValue Visit(FormulaNode node, Func<string, FormulaValue> resolve, ref int budget, int depth)
    {
        if (--budget < 0 || depth > FormulaParser.MaximumDepth + 1) return FormulaValue.Error("#LIMIT!", "Evaluation budget exceeded.");
        switch (node)
        {
            case LiteralNode literal: return literal.Value;
            case ReferenceNode reference: return resolve(reference.Name);
            case UnaryNode unary:
                var value = Visit(unary.Operand, resolve, ref budget, depth + 1);
                return value.IsError ? value : value.IsNumeric ? FormulaValue.Number(unary.Operator == "-" ? -value.Numeric : value.Numeric, value.Dimension) : FormulaValue.Error("#VALUE!", "Numeric operand required.");
            case BinaryNode binary:
                var left = Visit(binary.Left, resolve, ref budget, depth + 1); if (left.IsError) return left;
                var right = Visit(binary.Right, resolve, ref budget, depth + 1); if (right.IsError) return right;
                return Binary(binary.Operator, left, right);
            case CallNode call:
                return Call(call, resolve, ref budget, depth + 1);
            default: return FormulaValue.Error("#VALUE!");
        }
    }
    private static FormulaValue Binary(string op, FormulaValue a, FormulaValue b)
    {
        if (op == "&") return FormulaValue.Text(a + b.ToString());
        if (op is "=" or "<>" or "<" or ">" or "<=" or ">=")
        {
            if (a.IsNumeric && b.IsNumeric && a.Dimension != b.Dimension && a.Dimension != default && b.Dimension != default) return FormulaValue.Error("#UNIT!");
            var comparison = a.IsNumeric && b.IsNumeric ? a.Numeric.CompareTo(b.Numeric) : StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString());
            return FormulaValue.Boolean(op switch { "=" => comparison == 0, "<>" => comparison != 0, "<" => comparison < 0, ">" => comparison > 0, "<=" => comparison <= 0, _ => comparison >= 0 });
        }
        if (!a.IsNumeric || !b.IsNumeric) return FormulaValue.Error("#VALUE!", "Numeric operands required.");
        if (op is "+" or "-")
        {
            if (a.Dimension != b.Dimension && a.Dimension != default && b.Dimension != default) return FormulaValue.Error("#UNIT!", "Incompatible dimensions.");
            return FormulaValue.Number(op == "+" ? a.Numeric + b.Numeric : a.Numeric - b.Numeric, a.Dimension == default ? b.Dimension : a.Dimension);
        }
        return op switch
        {
            "*" => FormulaValue.Number(a.Numeric * b.Numeric, a.Dimension + b.Dimension),
            "/" => b.Numeric == 0 ? FormulaValue.Error("#DIV/0!") : FormulaValue.Number(a.Numeric / b.Numeric, a.Dimension - b.Dimension),
            "^" when b.Dimension != default => FormulaValue.Error("#UNIT!", "Exponent must be dimensionless."),
            "^" when a.Dimension != default && (b.Numeric != Math.Truncate(b.Numeric) || Math.Abs(b.Numeric) > 16) => FormulaValue.Error("#UNIT!", "Dimensional powers must be small integers."),
            "^" => FormulaValue.Number(Math.Pow(a.Numeric, b.Numeric), a.Dimension == default ? default : a.Dimension.Power((int)b.Numeric)),
            _ => FormulaValue.Error("#NAME?", op)
        };
    }
    private FormulaValue Call(CallNode call, Func<string, FormulaValue> resolve, ref int budget, int depth)
    {
        var nodes = call.Arguments; var name = call.Name;
        FormulaValue Arity() => FormulaValue.Error("#VALUE!", "Invalid arguments for " + name + ".");
        if (name == "IF")
        {
            if (nodes.Count != 3) return Arity();
            var condition = Visit(nodes[0], resolve, ref budget, depth); if (condition.IsError) return condition;
            return Visit(nodes[condition.IsTrue ? 1 : 2], resolve, ref budget, depth);
        }
        if (name == "IFERROR")
        {
            if (nodes.Count != 2) return Arity();
            var first = Visit(nodes[0], resolve, ref budget, depth);
            return first.IsError ? Visit(nodes[1], resolve, ref budget, depth) : first;
        }
        if (name is "AND" or "OR")
        {
            foreach (var child in nodes)
            {
                var result = Visit(child, resolve, ref budget, depth); if (result.IsError) return result;
                if (name == "AND" && !result.IsTrue) return FormulaValue.Boolean(false);
                if (name == "OR" && result.IsTrue) return FormulaValue.Boolean(true);
            }
            return FormulaValue.Boolean(name == "AND");
        }
        if (name == "SETATREF")
        {
            if (nodes.Count is < 1 or > 3 || nodes[0] is not ReferenceNode) return Arity();
            if (nodes.Count == 3 && Visit(nodes[2], resolve, ref budget, depth).IsTrue) return FormulaValue.Number(0);
            return Visit(nodes[0], resolve, ref budget, depth);
        }
        var values = new FormulaValue[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            values[i] = Visit(nodes[i], resolve, ref budget, depth);
            if (values[i].IsError && name is not "ISERR" and not "ISERROR") return values[i];
        }
        var a = values.Length > 0 ? values[0] : FormulaValue.Number(0);
        var b = values.Length > 1 ? values[1] : FormulaValue.Number(0);
        var n = values.Length;
        if (name is "GUARD" or "SETATREFEVAL" or "THEMEGUARD" or "THEME") return n == 1 ? a : Arity();
        if (name is "ISERR" or "ISERROR") return n == 1 ? FormulaValue.Boolean(a.IsError) : Arity();
        if (name == "ISNUM") return n == 1 ? FormulaValue.Boolean(a.IsNumeric) : Arity();
        if (name == "ISTEXT") return n == 1 ? FormulaValue.Boolean(a.Kind == FormulaValueKind.Text) : Arity();
        if (name == "STRSAME") return n is 2 or 3 ? FormulaValue.Boolean(string.Equals(a.ToString(), b.ToString(), n == 3 && values[2].IsTrue ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) : Arity();
        if (name == "LEN") return n == 1 ? FormulaValue.Number(a.ToString().Length) : Arity();
        if (name == "LOWER") return n == 1 ? FormulaValue.Text(a.ToString().ToLowerInvariant()) : Arity();
        if (name == "UPPER") return n == 1 ? FormulaValue.Text(a.ToString().ToUpperInvariant()) : Arity();
        if (name == "TRIM") return n == 1 ? FormulaValue.Text(a.ToString().Trim()) : Arity();
        if (name == "CHAR") return n == 1 && a.IsNumeric && a.Numeric is >= 0 and <= 0x10FFFF && a.Numeric is not (>= 0xD800 and <= 0xDFFF) ? FormulaValue.Text(char.ConvertFromUtf32((int)a.Numeric)) : Arity();
        if (name == "LEFT") return n is 1 or 2 && (n == 1 || b.IsNumeric) ? FormulaValue.Text(a.ToString()[..Math.Clamp(n == 1 ? 1 : (int)b.Numeric, 0, a.ToString().Length)]) : Arity();
        if (name == "RIGHT") { var s = a.ToString(); return n is 1 or 2 && (n == 1 || b.IsNumeric) ? FormulaValue.Text(s[(s.Length - Math.Clamp(n == 1 ? 1 : (int)b.Numeric, 0, s.Length))..]) : Arity(); }
        if (name == "MID") { var s = a.ToString(); if (n != 3 || !b.IsNumeric || !values[2].IsNumeric) return Arity(); var start = Math.Clamp((int)b.Numeric - 1, 0, s.Length); return FormulaValue.Text(s.Substring(start, Math.Clamp((int)values[2].Numeric, 0, s.Length - start))); }
        if (name == "SUBSTITUTE") return n == 3 && b.ToString().Length > 0 ? FormulaValue.Text(a.ToString().Replace(b.ToString(), values[2].ToString(), StringComparison.Ordinal)) : Arity();
        if (name == "FIND") { if (n is < 2 or > 3) return Arity(); var s = b.ToString(); var start = n == 3 ? Math.Clamp((int)values[2].Numeric - 1, 0, s.Length) : 0; var position = s.IndexOf(a.ToString(), start, StringComparison.Ordinal); return position < 0 ? FormulaValue.Error("#VALUE!", "Text not found.") : FormulaValue.Number(position + 1); }
        if (name == "FORMAT") { if (n != 2 || !a.IsNumeric) return Arity(); try { return FormulaValue.Text(a.Numeric.ToString(b.ToString(), CultureInfo.InvariantCulture)); } catch (FormatException) { return Arity(); } }
        if (name == "VALUE")
        {
            if (n != 1) return Arity();
            try { return Visit(FormulaParser.Parse(a.ToString()).Root, resolve, ref budget, depth + 1); }
            catch (FormatException ex) { return FormulaValue.Error("#PARSE!", ex.Message); }
        }
        if (name == "CONVERT") return n == 2 ? FormulaUnits.Convert(a, b.ToString()) : Arity();
        if (name == "RGB") return n == 3 && values.All(v => v.IsNumeric) ? FormulaValue.Number((int)Math.Clamp(a.Numeric, 0, 255) | (int)Math.Clamp(b.Numeric, 0, 255) << 8 | (int)Math.Clamp(values[2].Numeric, 0, 255) << 16) : Arity();
        if (name == "PI") return n == 0 ? FormulaValue.Number(Math.PI) : Arity();
        if (name is "TRUE" or "FALSE") return n == 0 ? FormulaValue.Boolean(name == "TRUE") : Arity();
        if (name == "NOT") return n == 1 ? FormulaValue.Boolean(!a.IsTrue) : Arity();
        if (values.Any(v => !v.IsNumeric)) return Arity();
        var x = a.Numeric; var y = b.Numeric;
        if (name is "MIN" or "MAX" or "SUM" or "AVERAGE")
        {
            if (n == 0) return Arity();
            var dimension = values.FirstOrDefault(v => v.Dimension != default).Dimension;
            if (values.Any(v => v.Dimension != default && v.Dimension != dimension)) return FormulaValue.Error("#UNIT!");
            return FormulaValue.Number(name switch { "MIN" => values.Min(v => v.Numeric), "MAX" => values.Max(v => v.Numeric), "SUM" => values.Sum(v => v.Numeric), _ => values.Average(v => v.Numeric) }, dimension);
        }
        if (name == "ATAN2") return n == 2 ? FormulaValue.Number(Math.Atan2(y, x), new(Angle: 1)) : Arity();
        if (name == "MOD") return n == 2 && y != 0 ? FormulaValue.Number(x - y * Math.Floor(x / y), a.Dimension) : Arity();
        if (name is "ROUND" or "ROUNDUP" or "ROUNDDOWN")
        {
            if (n is < 1 or > 2 || y is < -15 or > 15) return Arity();
            var factor = Math.Pow(10, n == 1 ? 0 : Math.Truncate(y));
            return FormulaValue.Number((name == "ROUND" ? Math.Round(x * factor, MidpointRounding.AwayFromZero) : name == "ROUNDUP" ? Math.Sign(x) * Math.Ceiling(Math.Abs(x) * factor) : Math.Truncate(x * factor)) / factor, a.Dimension);
        }
        if (name is "CEILING" or "FLOOR")
        {
            if (n is < 1 or > 2 || n == 2 && y == 0) return Arity();
            var factor = n == 1 ? 1 : Math.Abs(y);
            return FormulaValue.Number((name == "CEILING" ? Math.Ceiling(x / factor) : Math.Floor(x / factor)) * factor, a.Dimension);
        }
        if (n != 1) return FormulaValue.Error("#NAME?", "Unsupported function or arity: " + name);
        return name switch
        {
            "ABS" => FormulaValue.Number(Math.Abs(x), a.Dimension), "SIGN" => FormulaValue.Number(Math.Sign(x)),
            "INT" => FormulaValue.Number(Math.Floor(x), a.Dimension), "TRUNC" => FormulaValue.Number(Math.Truncate(x), a.Dimension),
            "SQRT" when a.Dimension.Length % 2 != 0 || a.Dimension.Angle % 2 != 0 || a.Dimension.Time % 2 != 0 => FormulaValue.Error("#UNIT!"),
            "SQRT" => FormulaValue.Number(Math.Sqrt(x), new(a.Dimension.Length / 2, a.Dimension.Angle / 2, a.Dimension.Time / 2)),
            "SIN" => FormulaValue.Number(Math.Sin(x)), "COS" => FormulaValue.Number(Math.Cos(x)), "TAN" => FormulaValue.Number(Math.Tan(x)),
            "ASIN" => FormulaValue.Number(Math.Asin(x), new(Angle: 1)), "ACOS" => FormulaValue.Number(Math.Acos(x), new(Angle: 1)), "ATAN" => FormulaValue.Number(Math.Atan(x), new(Angle: 1)),
            "SINH" => FormulaValue.Number(Math.Sinh(x)), "COSH" => FormulaValue.Number(Math.Cosh(x)), "TANH" => FormulaValue.Number(Math.Tanh(x)),
            "EXP" => FormulaValue.Number(Math.Exp(x)), "LN" => FormulaValue.Number(Math.Log(x)), "LOG10" => FormulaValue.Number(Math.Log10(x)),
            "DEG" => FormulaValue.Number(x * 180 / Math.PI), "RAD" => FormulaValue.Number(x * Math.PI / 180, new(Angle: 1)),
            _ => FormulaValue.Error("#NAME?", "Unsupported function: " + name)
        };
    }
}
