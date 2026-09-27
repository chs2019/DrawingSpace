using System.Globalization;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

public sealed record FormulaDiagnostic(string PageId, string ShapeId, string Cell, string Message);

/// <summary>Connects the pure formula engine to the document. Unsupported formulas retain their cached values.</summary>
public static class ShapeSheetService
{
    private static readonly string[] GeometryCells = ["Width", "Height", "PinX", "PinY", "Angle", "FlipX", "FlipY"];
    private static readonly IReadOnlyDictionary<string, string> PropertyCells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Width"] = "Width", ["Height"] = "Height", ["Style.Fill"] = "FillForegnd", ["Style.Stroke"] = "LineColor",
        ["Style.StrokeWidth"] = "LineWeight", ["Style.TextColor"] = "Char.Color", ["Style.FontSize"] = "Char.Size",
        ["Style.Bold"] = "Char.Style", ["Style.Italic"] = "Char.Style", ["Text"] = "Text"
    };

    public static IReadOnlyList<FormulaDiagnostic> Recalculate(DiagramDocument document)
    {
        var diagnostics = new List<FormulaDiagnostic>();
        var pending = new List<Action>();
        foreach (var page in document.Pages)
        {
            var scope = new ShapeSheetScope(document, page);
            foreach (var shape in page.Shapes)
            {
                var values = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in scope.Names(shape))
                {
                    var value = scope.Evaluate(shape, name);
                    if (value.IsError) { diagnostics.Add(new(page.Id, shape.Id, name, value.ToString())); continue; }
                    values[name] = value;
                }
                if (values.Count == 0) continue;
                pending.Add(() => Apply(page, shape, values, diagnostics));
            }
        }
        foreach (var change in pending) change();
        return diagnostics;
    }

    private static void Apply(DiagramPage page, Shape shape, Dictionary<string, FormulaValue> values, List<FormulaDiagnostic> diagnostics)
    {
        var center = shape.Bounds.Center;
        double Number(string name, double fallback, double scale, double minimum, double maximum)
        {
            if (!values.TryGetValue(name, out var value)) return fallback;
            if (!value.IsNumeric || !double.IsFinite(value.Numeric * scale) || value.Numeric * scale < minimum || value.Numeric * scale > maximum)
            { diagnostics.Add(new(page.Id, shape.Id, name, "#VALUE! Result is outside the document's supported range.")); return fallback; }
            return value.Numeric * scale;
        }
        shape.Width = Number("Width", shape.Width, 96, 1, 100000);
        shape.Height = Number("Height", shape.Height, 96, 1, 100000);
        center = new(Number("PinX", center.X, 96, -1000000, 1000000), page.Height - Number("PinY", page.Height - center.Y, 96, -1000000, 1000000));
        shape.X = center.X - shape.Width / 2; shape.Y = center.Y - shape.Height / 2;
        shape.Rotation = Number("Angle", shape.Rotation, -180 / Math.PI, -360000, 360000);
        if (values.TryGetValue("FlipX", out var flipX)) shape.FlipX = flipX.IsTrue;
        if (values.TryGetValue("FlipY", out var flipY)) shape.FlipY = flipY.IsTrue;
        shape.Style.StrokeWidth = Number("LineWeight", shape.Style.StrokeWidth, 96, 0, 100);
        shape.Style.FontSize = Number("Char.Size", shape.Style.FontSize, 96, 1, 1024);
        if (values.TryGetValue("Char.Style", out var characterStyle) && characterStyle.IsNumeric)
        { shape.Style.Bold = ((int)characterStyle.Numeric & 1) != 0; shape.Style.Italic = ((int)characterStyle.Numeric & 2) != 0; }
        if (values.TryGetValue("FillForegnd", out var fill) && Color(fill) is { } fillColor) shape.Style.Fill = fillColor;
        if (values.TryGetValue("LineColor", out var line) && Color(line) is { } lineColor) shape.Style.Stroke = lineColor;
        if (values.TryGetValue("Char.Color", out var text) && Color(text) is { } textColor) shape.Style.TextColor = textColor;
        if (values.TryGetValue("Text", out var label) && label.ToString() != shape.Text)
        { shape.Text = label.ToString(); shape.TextSpans.Clear(); shape.Paragraphs.Clear(); }
        foreach (var (name, value) in values)
        {
            if (shape.Cells.TryGetValue(name, out var cell)) cell.Value = value.ToString();
            if (name.StartsWith("Prop.", StringComparison.OrdinalIgnoreCase)) shape.Data[name[5..]] = value.ToString();
        }
    }

    public static string? Color(FormulaValue value)
    {
        if (value.Kind == FormulaValueKind.Text)
        {
            var text = value.String;
            return text.Length is 7 or 9 && text[0] == '#' && text.AsSpan(1).ToString().All(Uri.IsHexDigit) ? text : null;
        }
        if (!value.IsNumeric || value.Numeric is < 0 or > 16777215) return null;
        var rgb = (int)value.Numeric;
        return $"#{rgb & 255:X2}{(rgb >> 8) & 255:X2}{(rgb >> 16) & 255:X2}";
    }

    public static void SetCell(DiagramDocument document, DiagramPage page, Shape shape, string name, string formula, bool force = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256 || formula.Length > FormulaParser.MaximumLength) throw new ArgumentException("Cell name or formula exceeds the input budget.");
        var scope = new ShapeSheetScope(document, page);
        if (!force && scope.Cell(shape, name) is { } old && FormulaInspection.IsGuarded(old.Formula)) throw new InvalidOperationException("This cell is protected by GUARD. Use the explicit override action to replace it.");
        // Parse before modifying the document. Reference and evaluation errors remain visible and editable.
        FormulaParser.Parse(formula);
        var value = scope.EvaluateWithFallback(shape, name);
        SetLocal(shape, name, new() { Formula = formula, Value = value.IsError ? "0" : value.ToString(), Unit = Unit(name) });
        var property = PropertyCells.FirstOrDefault(p => p.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
        if (property is not null && !shape.LocalOverrides.Contains(property, StringComparer.OrdinalIgnoreCase)) shape.LocalOverrides.Add(property);
    }

    /// <summary>Translate direct manipulation into cell writes before recalculation; guarded formulas win.</summary>
    public static void SynchronizeDirectEdits(DiagramDocument before, DiagramDocument current)
    {
        var oldPages = before.Pages.ToDictionary(p => p.Id);
        foreach (var page in current.Pages)
        {
            if (!oldPages.TryGetValue(page.Id, out var oldPage)) continue;
            var previous = oldPage.Shapes.ToDictionary(s => s.Id);
            var scope = new ShapeSheetScope(current, page);
            foreach (var shape in page.Shapes)
            {
                if (!previous.TryGetValue(shape.Id, out var old)) continue;
                var changed = new Dictionary<string, FormulaValue>(StringComparer.OrdinalIgnoreCase);
                void Numeric(string cell, double a, double b, string unit)
                { if (Math.Abs(a - b) > 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)))) changed[cell] = FormulaValue.Number(b, unit == "RAD" ? new(Angle: 1) : unit == "IN" ? new(Length: 1) : default); }
                Numeric("Width", old.Width / 96, shape.Width / 96, "IN"); Numeric("Height", old.Height / 96, shape.Height / 96, "IN");
                Numeric("PinX", old.Bounds.Center.X / 96, shape.Bounds.Center.X / 96, "IN");
                // A page-height edit does not count as a direct manipulation of every shape.
                Numeric("PinY", (page.Height - old.Bounds.Center.Y) / 96, (page.Height - shape.Bounds.Center.Y) / 96, "IN");
                Numeric("Angle", -old.Rotation * Math.PI / 180, -shape.Rotation * Math.PI / 180, "RAD");
                Numeric("FlipX", old.FlipX ? 1 : 0, shape.FlipX ? 1 : 0, ""); Numeric("FlipY", old.FlipY ? 1 : 0, shape.FlipY ? 1 : 0, "");
                Numeric("LineWeight", old.Style.StrokeWidth / 96, shape.Style.StrokeWidth / 96, "IN");
                Numeric("Char.Size", old.Style.FontSize / 96, shape.Style.FontSize / 96, "IN");
                Numeric("Char.Style", (old.Style.Bold ? 1 : 0) | (old.Style.Italic ? 2 : 0), (shape.Style.Bold ? 1 : 0) | (shape.Style.Italic ? 2 : 0), "");
                if (old.Style.Fill != shape.Style.Fill) changed["FillForegnd"] = FormulaValue.Text(shape.Style.Fill);
                if (old.Style.Stroke != shape.Style.Stroke) changed["LineColor"] = FormulaValue.Text(shape.Style.Stroke);
                if (old.Style.TextColor != shape.Style.TextColor) changed["Char.Color"] = FormulaValue.Text(shape.Style.TextColor);
                if (old.Text != shape.Text) changed["Text"] = FormulaValue.Text(shape.Text);
                foreach (var (name, value) in changed)
                {
                    if (scope.Cell(shape, name) is not { } cell) continue;
                    // An explicit formula edit in this transaction must not be overwritten by its preview value.
                    var oldCell = new ShapeSheetScope(before, oldPage).Cell(old, name);
                    if (oldCell is not null && cell.Formula != oldCell.Formula) continue;
                    WriteValue(current, page, shape, name, value, new HashSet<string>(), 0);
                }
            }
        }
    }

    private static void WriteValue(DiagramDocument document, DiagramPage page, Shape shape, string name, FormulaValue value, HashSet<string> active, int depth)
    {
        if (depth >= 10 || !active.Add(shape.Id + "!" + name.ToUpperInvariant())) throw new InvalidOperationException("Cyclic SETATREF assignment.");
        var scope = new ShapeSheetScope(document, page); var old = scope.Cell(shape, name);
        if (old is not null && FormulaInspection.IsGuarded(old.Formula)) return;
        if (old is not null && FormulaInspection.AssignmentTarget(old.Formula) is { } target)
        {
            var index = target.IndexOf('!'); var targetShape = index < 0 ? shape : scope.FindSheet(shape, target[..index]);
            if (targetShape is null) throw new InvalidOperationException("SETATREF references a missing shape.");
            WriteValue(document, page, targetShape, index < 0 ? target : target[(index + 1)..], value, active, depth + 1);
            return;
        }
        SetLocal(shape, name, new() { Value = value.ToString(), Unit = old?.Unit ?? Unit(name) });
    }

    private static void SetLocal(Shape shape, string name, ShapeCell value)
    {
        var key = shape.Cells.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        shape.Cells[key] = value;
    }
    private static string Unit(string name) => name.Equals("Angle", StringComparison.OrdinalIgnoreCase) ? "RAD"
        : GeometryCells.Take(4).Append("LineWeight").Append("Char.Size").Contains(name, StringComparer.OrdinalIgnoreCase) ? "IN" : "";
}

internal static class ShapeSheetScopeExtensions
{
    public static FormulaValue EvaluateWithFallback(this ShapeSheetScope scope, Shape shape, string name) => scope.Evaluate(shape, name);
}
