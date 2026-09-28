using System.Text;

namespace DrawingSpace.ShapeSheet;

/// <summary>Rewrites reference tokens without touching string literals, whitespace or unknown functions.</summary>
public static class FormulaReferenceRewriter
{
    /// <remarks>The callback receives only identifier tokens containing '!'. Return null to keep a token.</remarks>
    public static string Rewrite(string formula, Func<string, string?> replace)
    {
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(replace);
        if (formula.Length > FormulaParser.MaximumLength) throw new FormatException("Formula exceeds the input budget.");
        StringBuilder? result = null;
        var copied = 0;
        for (var i = 0; i < formula.Length;)
        {
            // A doubled quote is an escaped quote, not the end of a string.
            if (formula[i] is '"' or '\'')
            {
                var quote = formula[i++];
                while (i < formula.Length)
                {
                    if (formula[i++] != quote) continue;
                    if (i < formula.Length && formula[i] == quote) { i++; continue; }
                    break;
                }
                continue;
            }
            if (!char.IsLetter(formula[i]) && formula[i] is not '_' and not '$') { i++; continue; }
            var start = i++;
            while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] is '_' or '.' or '!' or '$' or '[' or ']')) i++;
            var token = formula.AsSpan(start, i - start);
            if (!token.Contains('!')) continue;
            var text = token.ToString();
            var replacement = replace(text);
            if (replacement is null || replacement == text) continue;
            result ??= new StringBuilder(formula.Length);
            result.Append(formula, copied, start - copied).Append(replacement);
            if (result.Length > FormulaParser.MaximumLength) throw new FormatException("Rewritten formula exceeds the input budget.");
            copied = i;
        }
        if (result is null) return formula;
        result.Append(formula, copied, formula.Length - copied);
        if (result.Length > FormulaParser.MaximumLength) throw new FormatException("Rewritten formula exceeds the input budget.");
        return result.ToString();
    }

    public static string RemapSheets(string formula, IReadOnlyDictionary<string, string> identities) => Rewrite(formula, token =>
    {
        var separator = token.IndexOf('!');
        if (!token.StartsWith("Sheet.", StringComparison.OrdinalIgnoreCase)) return null;
        return identities.TryGetValue(token[6..separator], out var id) ? "Sheet." + id + token[separator..] : null;
    });

    public static string Literal(FormulaValue value)
    {
        if (value.IsError) throw new ArgumentException("An error cannot be materialized as a formula literal.", nameof(value));
        if (!value.IsNumeric) return "\"" + value.ToString().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var text = value.Numeric.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (value.Dimension == default) return "(" + text + ")";
        var expression = new StringBuilder("(").Append(text);
        void Unit(string unit, int power)
        {
            if (power != 0) expression.Append("*(1 ").Append(unit).Append(")^").Append(power);
        }
        Unit("in", value.Dimension.Length); Unit("rad", value.Dimension.Angle); Unit("sec", value.Dimension.Time);
        return expression.Append(')').ToString();
    }
}
