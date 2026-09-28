using System.Globalization;
using DrawingSpace.Documents;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>Preserves flattened world geometry when changing a formula coordinate frame.</summary>
public static class CoordinateRebase
{
    public static void Reparent(DiagramPage page, Shape shape, string? parentId)
    {
        shape.FormulaParentId = parentId;
        if (!shape.UsesVisioCoordinates) return;
        var value = ShapeCoordinates.Read(page, shape);
        Set(shape, "Width", value.Width, "IN"); Set(shape, "Height", value.Height, "IN");
        Set(shape, "PinX", value.PinX, "IN"); Set(shape, "PinY", value.PinY, "IN");
        Set(shape, "LocPinX", value.LocPinX, "IN"); Set(shape, "LocPinY", value.LocPinY, "IN");
        Set(shape, "Angle", value.Angle, "RAD");
        Set(shape, "FlipX", value.FlipX ? 1 : 0, ""); Set(shape, "FlipY", value.FlipY ? 1 : 0, "");
    }

    /// <summary>Write only placement cells after a copy/translate; size and other formulas remain live.</summary>
    public static void SynchronizePlacement(DiagramPage page, Shape shape)
    {
        if (shape.UsesVisioCoordinates)
        {
            var local = ShapeCoordinates.Read(page, shape);
            Set(shape, "PinX", local.PinX, "IN"); Set(shape, "PinY", local.PinY, "IN");
        }
        else
        {
            if (shape.Cells.ContainsKey("PinX")) Set(shape, "PinX", shape.Bounds.Center.X / 96, "IN");
            if (shape.Cells.ContainsKey("PinY")) Set(shape, "PinY", (page.Height - shape.Bounds.Center.Y) / 96, "IN");
        }
    }

    public static void MaterializeParentReferences(ShapeSheetScope scope, Shape source, Shape target)
    {
        foreach (var name in scope.Names(source).ToArray())
        {
            if (scope.Cell(source, name) is not { } cell) continue;
            var formula = FormulaReferenceRewriter.Rewrite(cell.Formula, token =>
            {
                if (!token.StartsWith("ParentShape!", StringComparison.OrdinalIgnoreCase)) return null;
                var value = scope.Resolve(source, token);
                return value.IsError ? null : FormulaReferenceRewriter.Literal(value);
            });
            if (formula == cell.Formula) continue;
            var local = cell.Clone(); local.Formula = formula; local.Inherited = false; target.Cells[name] = local;
        }
    }

    private static void Set(Shape shape, string name, double value, string unit)
        => shape.Cells[name] = new() { Value = value.ToString("R", CultureInfo.InvariantCulture), Unit = unit };
}
