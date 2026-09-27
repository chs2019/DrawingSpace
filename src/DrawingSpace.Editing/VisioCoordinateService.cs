using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>Adapts parent-local Visio cells to flattened, affine editor geometry without mixing coordinate spaces.</summary>
public static class VisioCoordinateService
{
    private static readonly string[] GeometryNames = ["Width", "Height", "PinX", "PinY", "Angle", "FlipX", "FlipY"];
    public static FormulaValue? Builtin(DiagramPage page, Shape shape, string name)
    {
        if (!shape.UsesVisioCoordinates) return null;
        var value = ShapeCoordinates.Read(page, shape); var length = new FormulaDimension(Length: 1);
        return name.ToUpperInvariant() switch
        {
            "WIDTH" => FormulaValue.Number(value.Width, length), "HEIGHT" => FormulaValue.Number(value.Height, length),
            "PINX" => FormulaValue.Number(value.PinX, length), "PINY" => FormulaValue.Number(value.PinY, length),
            "LOCPINX" => FormulaValue.Number(value.LocPinX, length), "LOCPINY" => FormulaValue.Number(value.LocPinY, length),
            "ANGLE" => FormulaValue.Number(value.Angle, new(Angle: 1)), "FLIPX" => FormulaValue.Boolean(value.FlipX), "FLIPY" => FormulaValue.Boolean(value.FlipY), _ => null
        };
    }

    public static IReadOnlyList<Action> Prepare(DiagramDocument document, List<FormulaDiagnostic> diagnostics)
    {
        var pending = new List<Action>();
        foreach (var page in document.Pages)
        {
            var scope = new ShapeSheetScope(document, page); var matrices = new Dictionary<string, MatrixD>(); var active = new HashSet<string>();
            MatrixD Resolve(Shape shape)
            {
                if (!shape.UsesVisioCoordinates) return shape.WorldMatrix;
                if (matrices.TryGetValue(shape.Id, out var existing)) return existing;
                if (!active.Add(shape.Id) || active.Count > 64) throw new InvalidDataException("Cyclic Visio coordinate frames.");
                var value = ShapeCoordinates.Read(page, shape);
                double Cell(string name, double fallback, double minimum, double maximum)
                {
                    if (scope.Cell(shape, name) is null) return fallback;
                    var result = scope.Evaluate(shape, name);
                    if (result.IsNumeric && result.Numeric >= minimum && result.Numeric <= maximum) return result.Numeric;
                    if (diagnostics.Count < 4096) diagnostics.Add(new(page.Id, shape.Id, name, result.IsError ? result.ToString() : "#VALUE! Invalid coordinate result."));
                    return fallback;
                }
                value = value with
                {
                    Width = Cell("Width", value.Width, 1d / 96, 100000d / 96), Height = Cell("Height", value.Height, 1d / 96, 100000d / 96),
                    PinX = Cell("PinX", value.PinX, -1000000d / 96, 1000000d / 96), PinY = Cell("PinY", value.PinY, -1000000d / 96, 1000000d / 96),
                    LocPinX = Cell("LocPinX", value.LocPinX, -1000000d / 96, 1000000d / 96), LocPinY = Cell("LocPinY", value.LocPinY, -1000000d / 96, 1000000d / 96),
                    Angle = Cell("Angle", value.Angle, -Math.Tau * 1000, Math.Tau * 1000),
                    FlipX = Cell("FlipX", value.FlipX ? 1 : 0, 0, 1) != 0, FlipY = Cell("FlipY", value.FlipY ? 1 : 0, 0, 1) != 0
                };
                var parent = shape.FormulaParentId is { } id ? page.Find(id) : null;
                var frame = parent is null ? ShapeCoordinates.PageFrame(page) : ShapeCoordinates.ChildFrame(parent, Resolve(parent));
                var matrix = ShapeCoordinates.Compose(frame, value);
                active.Remove(shape.Id); matrices[shape.Id] = matrix; return matrix;
            }
            foreach (var shape in page.Shapes.Where(s => s.UsesVisioCoordinates))
            {
                var target = Resolve(shape);
                pending.Add(() =>
                {
                    if (!shape.WorldMatrix.TryInvert(out var inverse)) throw new InvalidDataException("A shape has a singular world transform.");
                    shape.ApplyWorldTransform(target * inverse);
                });
            }
        }
        return pending;
    }

    public static void ReplaceGeometryChanges(DiagramPage oldPage, Shape old, DiagramPage page, Shape current, Dictionary<string, FormulaValue> changes)
    {
        if (!current.UsesVisioCoordinates || !old.UsesVisioCoordinates) return;
        foreach (var name in GeometryNames) changes.Remove(name);
        var a = ShapeCoordinates.Read(oldPage, old); var b = ShapeCoordinates.Read(page, current);
        void Set(string name, double before, double after, FormulaDimension dimension = default)
        {
            if (Math.Abs(before - after) > 1e-8 * Math.Max(1, Math.Max(Math.Abs(before), Math.Abs(after)))) changes[name] = FormulaValue.Number(after, dimension);
        }
        var length = new FormulaDimension(Length: 1);
        Set("Width", a.Width, b.Width, length); Set("Height", a.Height, b.Height, length);
        Set("PinX", a.PinX, b.PinX, length); Set("PinY", a.PinY, b.PinY, length); Set("Angle", a.Angle, b.Angle, new(Angle: 1));
        Set("FlipX", a.FlipX ? 1 : 0, b.FlipX ? 1 : 0); Set("FlipY", a.FlipY ? 1 : 0, b.FlipY ? 1 : 0);
    }
}
