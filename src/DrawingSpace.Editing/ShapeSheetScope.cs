using System.Globalization;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>One immutable-input evaluation scope. Memoized references make recalculation order-independent.</summary>
public sealed class ShapeSheetScope(DiagramDocument document, DiagramPage page)
{
    private readonly FormulaEngine _engine = new();
    private readonly Dictionary<(string Shape, string Cell), FormulaValue> _values = [];
    private readonly HashSet<(string Shape, string Cell)> _active = [];
    private int _evaluations;
    public FormulaValue Evaluate(Shape shape, string name)
    {
        var key = (shape.Id, name.ToUpperInvariant());
        if (_values.TryGetValue(key, out var cached)) return cached;
        if (_active.Count >= 64 || ++_evaluations > 100000) return FormulaValue.Error("#LIMIT!", "ShapeSheet dependency budget exceeded.");
        if (!_active.Add(key)) return FormulaValue.Error("#CYCLE!", shape.Name + "!" + name);
        FormulaValue result;
        try
        {
            var cell = Cell(shape, name);
            result = cell is null ? Builtin(shape, name) : EvaluateCell(cell, reference => Resolve(shape, reference));
        }
        finally { _active.Remove(key); }
        return _values[key] = result;
    }
    public ShapeCell? Cell(Shape shape, string name)
    {
        var local = FindCell(shape.Cells, name);
        if (local is not null && !local.Inherited && !local.Formula.Equals("Inh", StringComparison.OrdinalIgnoreCase)) return local;
        var template = MasterService.Template(document, shape);
        return template is null ? local : FindCell(template.Cells, name) ?? local;
    }
    public IEnumerable<string> Names(Shape shape) => shape.Cells.Keys.Concat(MasterService.Template(document, shape)?.Cells.Keys ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
    public FormulaValue Resolve(Shape shape, string reference)
    {
        var separator = reference.IndexOf('!');
        if (separator < 0) return Evaluate(shape, reference);
        var sheet = reference[..separator]; var cell = reference[(separator + 1)..];
        if (sheet.Equals("ThePage", StringComparison.OrdinalIgnoreCase))
        {
            if (cell.Equals("PageWidth", StringComparison.OrdinalIgnoreCase)) return FormulaValue.Number(page.Width / 96, new(Length: 1));
            if (cell.Equals("PageHeight", StringComparison.OrdinalIgnoreCase)) return FormulaValue.Number(page.Height / 96, new(Length: 1));
            return Global("page:" + page.Id, page.Cells, cell, shape);
        }
        if (sheet.Equals("TheDoc", StringComparison.OrdinalIgnoreCase)) return Global("document:" + document.Id, document.Cells, cell, shape);
        if (sheet.Equals("ParentShape", StringComparison.OrdinalIgnoreCase) && page.Find(shape.ContainerId) is { } parent) return Evaluate(parent, cell);
        var id = sheet.StartsWith("Sheet.", StringComparison.OrdinalIgnoreCase) ? sheet[6..] : sheet;
        var target = page.Shapes.FirstOrDefault(s => s.Id == id || s.VisioId?.ToString(CultureInfo.InvariantCulture) == id || s.Name.Equals(sheet, StringComparison.OrdinalIgnoreCase));
        return target is null ? FormulaValue.Error("#REF!", reference) : Evaluate(target, cell);
    }
    private FormulaValue Global(string id, Dictionary<string, ShapeCell> cells, string name, Shape shape)
    {
        var key = (id, name.ToUpperInvariant());
        if (_values.TryGetValue(key, out var value)) return value;
        if (_active.Count >= 64 || !_active.Add(key)) return FormulaValue.Error("#CYCLE!", name);
        try { return _values[key] = FindCell(cells, name) is { } cell ? EvaluateCell(cell, r => Resolve(shape, r)) : FormulaValue.Error("#REF!", name); }
        finally { _active.Remove(key); }
    }
    private FormulaValue EvaluateCell(ShapeCell cell, Func<string, FormulaValue> resolve)
    {
        if (!string.IsNullOrWhiteSpace(cell.Formula) && !cell.Formula.Equals("Inh", StringComparison.OrdinalIgnoreCase)) return _engine.Evaluate(cell.Formula, resolve);
        if (double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // VSDX V is already expressed in internal units; U is display metadata, not a multiplier.
            var dimension = cell.Unit.ToUpperInvariant() switch
            {
                "IN" or "MM" or "CM" or "M" or "FT" or "PT" or "PICA" or "DL" => new FormulaDimension(Length: 1),
                "RAD" or "DEG" or "DA" => new FormulaDimension(Angle: 1), "SEC" or "MIN" or "HR" => new FormulaDimension(Time: 1), _ => default
            };
            return FormulaValue.Number(number, dimension);
        }
        if (bool.TryParse(cell.Value, out var boolean)) return FormulaValue.Boolean(boolean);
        return FormulaValue.Text(cell.Value);
    }
    private static FormulaValue Builtin(Shape shape, string name)
    {
        var length = new FormulaDimension(Length: 1);
        return name.ToUpperInvariant() switch
        {
            "WIDTH" => FormulaValue.Number(shape.Width / 96, length), "HEIGHT" => FormulaValue.Number(shape.Height / 96, length),
            "LOC PINX" or "LOCPINX" => FormulaValue.Number(shape.Width / 192, length), "LOCPINY" => FormulaValue.Number(shape.Height / 192, length),
            "ANGLE" => FormulaValue.Number(-shape.Rotation * Math.PI / 180, new(Angle: 1)),
            "FLIPX" => FormulaValue.Boolean(shape.FlipX), "FLIPY" => FormulaValue.Boolean(shape.FlipY),
            "LINEWEIGHT" => FormulaValue.Number(shape.Style.StrokeWidth / 96, length), "CHAR.SIZE" => FormulaValue.Number(shape.Style.FontSize / 96, length),
            "FILLFOREGND" => FormulaValue.Text(shape.Style.Fill), "LINECOLOR" => FormulaValue.Text(shape.Style.Stroke), "CHAR.COLOR" => FormulaValue.Text(shape.Style.TextColor),
            "CHAR.STYLE" => FormulaValue.Number((shape.Style.Bold ? 1 : 0) | (shape.Style.Italic ? 2 : 0)),
            "TEXT" => FormulaValue.Text(shape.Text), "PINX" => FormulaValue.Number(shape.Bounds.Center.X / 96, length),
            _ when name.StartsWith("Prop.", StringComparison.OrdinalIgnoreCase) && shape.Data.TryGetValue(name[5..], out var value)
                => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ? FormulaValue.Number(numeric) : FormulaValue.Text(value),
            _ => FormulaValue.Error("#REF!", name)
        };
    }
    public FormulaValue EvaluateWithPageCoordinates(Shape shape, string name)
        => Cell(shape, name) is null && name.Equals("PinY", StringComparison.OrdinalIgnoreCase)
            ? FormulaValue.Number((page.Height - shape.Bounds.Center.Y) / 96, new(Length: 1)) : Evaluate(shape, name);
    private static ShapeCell? FindCell(Dictionary<string, ShapeCell> cells, string name)
        => cells.TryGetValue(name, out var value) ? value : cells.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
