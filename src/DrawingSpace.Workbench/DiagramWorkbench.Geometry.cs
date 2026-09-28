using DrawingSpace.Skia;

namespace DrawingSpace.Workbench;

public sealed partial class DiagramWorkbench
{
    private string? _geometryPrimaryId;

    private RibbonGroup BuildGeometryGroup()
    {
        bool Multiple() => Session.SelectedShapes.Count is >= 2 and <= ShapeBooleanGeometry.MaximumOperands && !Session.IsInteracting;
        return new("Shape Operations",
            Stack(Command("Union", OfficeIcon.Group, () => ApplyGeometryOperation(ShapeBooleanOperation.Union), enabled: Multiple),
                Command("Intersect", OfficeIcon.Group, () => ApplyGeometryOperation(ShapeBooleanOperation.Intersect), enabled: Multiple),
                Command("Subtract", OfficeIcon.Group, () => ApplyGeometryOperation(ShapeBooleanOperation.Subtract), enabled: Multiple)),
            Stack(Command("Combine", OfficeIcon.Group, () => ApplyGeometryOperation(ShapeBooleanOperation.Combine), enabled: Multiple,
                    tooltip: "Exclusive-or filled areas; overlaps become holes."),
                Command("Primary Shape", OfficeIcon.Pointer, ChooseGeometryPrimary, enabled: Multiple,
                    tooltip: "Choose the subtraction base and source of text/formatting. The backmost selected shape is the default."),
                Command("Create Path Copy", OfficeIcon.Copy, CreateGeometryCopy,
                    enabled: () => Session.SelectedShapes.Count == 1 && !Session.IsInteracting,
                    tooltip: "Create an independent filled-outline copy without changing the original shape or its formulas.")));
    }

    private void ChooseGeometryPrimary()
    {
        ShowMenu("Primary Boolean operand", Session.SelectedShapes.Select((shape, index) =>
            ($"{index + 1}. {shape.Name}", (Action)(() =>
            {
                _geometryPrimaryId = shape.Id;
                ShowStatus("Primary Boolean operand: " + shape.Name);
            }))));
    }

    private void ApplyGeometryOperation(ShapeBooleanOperation operation)
    {
        var ids = Session.SelectedShapes.Select(s => s.Id).ToList();
        if (_geometryPrimaryId is { } primary && ids.Remove(primary)) ids.Insert(0, primary);
        var operands = Session.GetGeometryOperands(ids);
        var result = ShapeBooleanGeometry.Compute(operands, operation);
        if (result is null)
        {
            ShowStatus("The operation has no filled area; the original shapes were kept.");
            return;
        }
        Session.ReplaceShapesWithGeometry(ids, result, operation + " shapes");
        _geometryPrimaryId = null;
        ShowStatus(operation + " completed. Connector anchors were retained; Undo restores the original shapes.");
        Surface.FocusCanvas();
    }

    private void CreateGeometryCopy()
    {
        if (Session.SelectedShapes.Count != 1) return;
        var original = Session.SelectedShapes[0];
        var copy = ShapeBooleanGeometry.CreatePathCopy(original);
        if (copy is null) { ShowStatus("The shape has no filled vector area to copy."); return; }
        copy.Name = original.Name.Length < 1019 ? original.Name + " path" : original.Name;
        Session.AddShape(copy);
        ShowStatus("Created an independent path copy. The original and its live relationships are unchanged.");
        Surface.FocusCanvas();
    }
}
