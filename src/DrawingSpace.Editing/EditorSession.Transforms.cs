using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>Creates a fixed snapshot for a host-owned Begin/Preview/Commit gesture.</summary>
    public SelectionTransformSnapshot CaptureSelectionTransform()
        => SelectionTransformSnapshot.Capture(Page, SelectedShapes, SelectedConnectors);

    /// <summary>Resizes the entire selection about an eight-handle selection rectangle.</summary>
    public void ResizeSelection(int handle, PointD start, PointD current, bool preserveAspect = false)
    {
        var snapshot = CaptureSelectionTransform();
        var matrix = ShapeTransforms.SelectionResize(snapshot.Bounds, handle, start, current, preserveAspect);
        Execute("Resize selection", () => snapshot.Apply(matrix));
    }

    /// <summary>Rotates selected shapes, descendants, routes and free endpoints together.</summary>
    public void RotateSelection(double degrees, PointD? pivot = null)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        var snapshot = CaptureSelectionTransform();
        var matrix = MatrixD.Around(pivot ?? snapshot.Bounds.Center, MatrixD.Rotation(degrees));
        Execute("Rotate selection", () => snapshot.Apply(matrix));
    }
}
