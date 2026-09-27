using System.Globalization;

namespace DrawingSpace.ShapeSheet;

public sealed class ParsedFormula
{
    internal FormulaNode Root { get; }
    public IReadOnlyList<string> References { get; }
    internal ParsedFormula(FormulaNode root, IEnumerable<string> references) { Root = root; References = references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
}
internal abstract record FormulaNode;
internal sealed record LiteralNode(FormulaValue Value) : FormulaNode;
internal sealed record ReferenceNode(string Name) : FormulaNode;
internal sealed record UnaryNode(string Operator, FormulaNode Operand) : FormulaNode;
internal sealed record BinaryNode(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;
internal sealed record CallNode(string Name, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>Pratt parser with explicit input, token and recursion budgets. It never evaluates C# or JavaScript.</summary>
public sealed class FormulaParser
{
    public const int MaximumLength = 32768;
    public const int MaximumTokens = 4096;
    public const int MaximumDepth = 64;
    private readonly string _text;
    private readonly List<string> _references = [];
    private int _offset, _tokens;
    private Token _token;
    private readonly record struct Token(string Kind, string Text, int Position);
    private FormulaParser(string text) { _text = text; Advance(); }
    public static ParsedFormula Parse(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        if (formula.Length > MaximumLength) throw new FormatException("Formula exceeds the input budget.");
        var parser = new FormulaParser(formula.TrimStart().TrimStart('='));
        var root = parser.Expression(0, 0);
        if (parser._token.Kind != "end") throw parser.Error("Unexpected token.");
        return new(root, parser._references);
    }
    private FormatException Error(string message) => new($"{message} At character {_token.Position}.");
    private FormulaNode Expression(int minimum, int depth)
    {
        if (depth > MaximumDepth) throw Error("Formula nesting exceeds the depth budget.");
        FormulaNode left;
        var token = _token;
        Advance();
        if (token.Kind == "number")
        {
            if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw Error("Invalid number.");
            var literal = FormulaValue.Number(value);
            if ((_token.Kind == "identifier" || _token.Text == "%") && FormulaUnits.IsUnit(_token.Text)) { literal = FormulaUnits.Literal(value, _token.Text); Advance(); }
            left = new LiteralNode(literal);
        }
        else if (token.Kind == "string") left = new LiteralNode(FormulaValue.Text(token.Text));
        else if (token.Text is "+" or "-") left = new UnaryNode(token.Text, Expression(55, depth + 1));
        else if (token.Text == "(") { left = Expression(0, depth + 1); Require(")"); }
        else if (token.Kind == "identifier")
        {
            if (_token.Text == "(")
            {
                Advance(); var args = new List<FormulaNode>();
                if (_token.Text != ")") do { args.Add(Expression(0, depth + 1)); if (_token.Text is not "," and not ";") break; Advance(); } while (true);
                Require(")"); left = new CallNode(token.Text.ToUpperInvariant(), args);
            }
            else if (token.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) left = new LiteralNode(FormulaValue.Boolean(true));
            else if (token.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) left = new LiteralNode(FormulaValue.Boolean(false));
            else { _references.Add(token.Text); left = new ReferenceNode(token.Text); }
        }
        else throw Error("Expected a value, reference or function.");
        while (true)
        {
            var op = _token.Text;
            var binding = op switch { "=" or "<>" or "<" or ">" or "<=" or ">=" => 10, "&" => 20, "+" or "-" => 30, "*" or "/" => 40, "^" => 60, _ => -1 };
            if (binding < minimum) break;
            Advance();
            left = new BinaryNode(op, left, Expression(op == "^" ? binding : binding + 1, depth + 1));
        }
        return left;
    }
    private void Require(string value) { if (_token.Text != value) throw Error("Expected '" + value + "'."); Advance(); }
    private void Advance()
    {
        if (++_tokens > MaximumTokens) throw new FormatException("Formula exceeds the token budget.");
        while (_offset < _text.Length && char.IsWhiteSpace(_text[_offset])) _offset++;
        var start = _offset;
        if (_offset == _text.Length) { _token = new("end", "", start); return; }
        var c = _text[_offset++];
        if (c == '"')
        {
            var text = new System.Text.StringBuilder(); var closed = false;
            while (_offset < _text.Length)
            {
                c = _text[_offset++];
                if (c != '"') { text.Append(c); continue; }
                if (_offset < _text.Length && _text[_offset] == '"') { text.Append('"'); _offset++; }
                else { closed = true; break; }
            }
            if (!closed) throw new FormatException("Unterminated string.");
            _token = new("string", text.ToString(), start); return;
        }
        if (char.IsDigit(c) || c == '.' && _offset < _text.Length && char.IsDigit(_text[_offset]))
        {
            while (_offset < _text.Length && (char.IsDigit(_text[_offset]) || _text[_offset] == '.')) _offset++;
            if (_offset < _text.Length && _text[_offset] is 'e' or 'E')
            {
                _offset++; if (_offset < _text.Length && _text[_offset] is '+' or '-') _offset++;
                while (_offset < _text.Length && char.IsDigit(_text[_offset])) _offset++;
            }
            _token = new("number", _text[start.._offset], start); return;
        }
        if (char.IsLetter(c) || c is '_' or '$' or '°')
        {
            while (_offset < _text.Length && (char.IsLetterOrDigit(_text[_offset]) || _text[_offset] is '_' or '.' or '!' or '$' or '[' or ']')) _offset++;
            _token = new("identifier", _text[start.._offset], start); return;
        }
        if (c is '<' or '>' && _offset < _text.Length && (_text[_offset] == '=' || c == '<' && _text[_offset] == '>')) _offset++;
        _token = new("operator", _text[start.._offset], start);
    }
}
