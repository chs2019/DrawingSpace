using System.Globalization;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>One evaluation scope. Evaluate all cells before applying results so recalculation is order-independent.</summary>
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
        try
        {
            var cell = Cell(shape, name);
            return _values[key] = cell is null ? Builtin(shape, name) : EvaluateCell(cell, reference => Resolve(shape, reference));
        }
        finally { _active.Remove(key); }
    }

    public ShapeCell? Cell(Shape shape, string name)
    {
        var local = FindCell(shape.Cells, name);
        if (local is not null && !local.Inherited && !local.Formula.Equals("Inh", StringComparison.OrdinalIgnoreCase)) return local;
        var template = MasterService.Template(document, shape);
        return template is null ? local : FindCell(template.Cells, name) ?? local;
    }

    public IEnumerable<string> Names(Shape shape) => shape.Cells.Keys
        .Concat(MasterService.Template(document, shape)?.Cells.Keys ?? Enumerable.Empty<string>())
        .Distinct(StringComparer.OrdinalIgnoreCase);

    public FormulaValue Resolve(Shape shape, string reference)
    {
        var separator = reference.IndexOf('!');
        if (separator < 0) return Evaluate(shape, reference);
        var sheet = reference[..separator]; var cell = reference[(separator + 1)..];
        if (sheet.Equals("ThePage", StringComparison.OrdinalIgnoreCase)) return PageCell(cell);
        if (sheet.Equals("TheDoc", StringComparison.OrdinalIgnoreCase)) return Global("document:" + document.Id, document.Cells, cell, referenceName =>
        {
            var name = referenceName.StartsWith("TheDoc!", StringComparison.OrdinalIgnoreCase) ? referenceName[7..] : referenceName;
            return Global("document:" + document.Id, document.Cells, name, r => Resolve(shape, r));
        });
        var target = FindSheet(shape, sheet);
        return target is null ? FormulaValue.Error("#REF!", reference) : Evaluate(target, cell);
    }

    public Shape? FindSheet(Shape shape, string sheet)
    {
        if (sheet.Equals("ParentShape", StringComparison.OrdinalIgnoreCase)) return page.Find(shape.FormulaParentId) ?? page.Find(shape.ContainerId);
        var id = sheet.StartsWith("Sheet.", StringComparison.OrdinalIgnoreCase) ? sheet[6..] : sheet;
        return page.Shapes.FirstOrDefault(s => s.Id == id || s.VisioId?.ToString(CultureInfo.InvariantCulture) == id || s.Name.Equals(sheet, StringComparison.OrdinalIgnoreCase));
    }

    public FormulaValue EvaluateFormula(Shape shape, string formula) => _engine.Evaluate(formula, name => Resolve(shape, name));

    private FormulaValue PageCell(string name)
    {
        if (name.Equals("PageWidth", StringComparison.OrdinalIgnoreCase) && FindCell(page.Cells, name) is null) return FormulaValue.Number(page.Width / 96, new(Length: 1));
        if (name.Equals("PageHeight", StringComparison.OrdinalIgnoreCase) && FindCell(page.Cells, name) is null) return FormulaValue.Number(page.Height / 96, new(Length: 1));
        return Global("page:" + page.Id, page.Cells, name, reference => reference.StartsWith("TheDoc!", StringComparison.OrdinalIgnoreCase)
            ? Global("document:" + document.Id, document.Cells, reference[7..], _ => FormulaValue.Error("#REF!"))
            : PageCell(reference.StartsWith("ThePage!", StringComparison.OrdinalIgnoreCase) ? reference[8..] : reference));
    }

    private FormulaValue Global(string id, Dictionary<string, ShapeCell> cells, string name, Func<string, FormulaValue> resolve)
    {
        var key = (id, name.ToUpperInvariant());
        if (_values.TryGetValue(key, out var value)) return value;
        if (_active.Count >= 64 || ++_evaluations > 100000) return FormulaValue.Error("#LIMIT!");
        if (!_active.Add(key)) return FormulaValue.Error("#CYCLE!", name);
        try { return _values[key] = FindCell(cells, name) is { } cell ? EvaluateCell(cell, resolve) : FormulaValue.Error("#REF!", name); }
        finally { _active.Remove(key); }
    }

    private FormulaValue EvaluateCell(ShapeCell cell, Func<string, FormulaValue> resolve)
    {
        if (!string.IsNullOrWhiteSpace(cell.Formula) && !cell.Formula.Equals("Inh", StringComparison.OrdinalIgnoreCase)) return _engine.Evaluate(cell.Formula, resolve);
        if (double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // VSDX V is already in internal units. U describes the display unit; it must not be applied twice.
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

    private FormulaValue Builtin(Shape shape, string name)
    {
        if (VisioCoordinateService.Builtin(page, shape, name) is { } imported) return imported;
        var length = new FormulaDimension(Length: 1);
        return name.ToUpperInvariant() switch
        {
            "WIDTH" => FormulaValue.Number(shape.Width / 96, length), "HEIGHT" => FormulaValue.Number(shape.Height / 96, length),
            "LOCPINX" => FormulaValue.Number(shape.Width / 192, length), "LOCPINY" => FormulaValue.Number(shape.Height / 192, length),
            "PINX" => FormulaValue.Number(shape.Bounds.Center.X / 96, length), "PINY" => FormulaValue.Number((page.Height - shape.Bounds.Center.Y) / 96, length),
            "ANGLE" => FormulaValue.Number(-shape.Rotation * Math.PI / 180, new(Angle: 1)),
            "FLIPX" => FormulaValue.Boolean(shape.FlipX), "FLIPY" => FormulaValue.Boolean(shape.FlipY),
            "LINEWEIGHT" => FormulaValue.Number(shape.Style.StrokeWidth / 96, length), "CHAR.SIZE" => FormulaValue.Number(shape.Style.FontSize / 96, length),
            "FILLFOREGND" => FormulaValue.Text(shape.Style.Fill), "LINECOLOR" => FormulaValue.Text(shape.Style.Stroke), "CHAR.COLOR" => FormulaValue.Text(shape.Style.TextColor),
            "CHAR.STYLE" => FormulaValue.Number((shape.Style.Bold ? 1 : 0) | (shape.Style.Italic ? 2 : 0)), "TEXT" => FormulaValue.Text(shape.Text),
            _ when name.StartsWith("Prop.", StringComparison.OrdinalIgnoreCase) && shape.Data.TryGetValue(name[5..], out var value)
                => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) ? FormulaValue.Number(numeric) : FormulaValue.Text(value),
            _ => FormulaValue.Error("#REF!", name)
        };
    }

    private static ShapeCell? FindCell(Dictionary<string, ShapeCell> cells, string name)
        => cells.TryGetValue(name, out var value) ? value : cells.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}
