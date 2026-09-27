using System.Globalization;
using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public readonly record struct LocalShapeTransform(double Width, double Height, double PinX, double PinY, double LocPinX, double LocPinY, double Angle, bool FlipX, bool FlipY, double Shear = 0);

public static class ShapeCoordinates
{
    public static MatrixD PageFrame(DiagramPage page) => new(96, 0, 0, -96, 0, page.Height);
    public static MatrixD ChildFrame(Shape parent, MatrixD? world = null)
    {
        var width = parent.CoordinateWidth > 0 ? parent.CoordinateWidth : parent.Width / 96;
        var height = parent.CoordinateHeight > 0 ? parent.CoordinateHeight : parent.Height / 96;
        return (world ?? parent.WorldMatrix) * new MatrixD(1 / width, 0, 0, -1 / height, 0, 1);
    }
    public static MatrixD ParentFrame(DiagramPage page, Shape shape) => shape.FormulaParentId is { } id && page.Find(id) is { } parent ? ChildFrame(parent) : PageFrame(page);
    public static LocalShapeTransform Read(DiagramPage page, Shape shape)
    {
        if (!ParentFrame(page, shape).TryInvert(out var inverse)) throw new InvalidDataException("The parent coordinate frame is singular.");
        var matrix = inverse * shape.WorldMatrix * new MatrixD(1, 0, 0, -1, 0, 1);
        var width = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B); var height = Math.Abs(matrix.Determinant) / Math.Max(width, 1e-16);
        var flipX = Cached(shape, "FlipX", 0) != 0; var flipY = matrix.Determinant < 0 != flipX;
        var angle = Math.Atan2(matrix.B * (flipX ? -1 : 1), matrix.A * (flipX ? -1 : 1));
        var lx = Cached(shape, "LocPinX", width / 2); var ly = Cached(shape, "LocPinY", height / 2);
        var pin = matrix.Map(new PointD(lx / width, ly / height));
        var shear = (matrix.A * matrix.C + matrix.B * matrix.D) / Math.Max(width * height, 1e-16);
        return new(width, height, pin.X, pin.Y, lx, ly, angle, flipX, flipY, shear);
    }
    public static MatrixD Compose(MatrixD parent, LocalShapeTransform value) => parent
        * MatrixD.Translation(value.PinX, value.PinY) * MatrixD.Rotation(value.Angle * 180 / Math.PI)
        * new MatrixD(value.FlipX ? -1 : 1, 0, value.Shear, value.FlipY ? -1 : 1, 0, 0)
        * MatrixD.Translation(-value.LocPinX, -value.LocPinY) * new MatrixD(value.Width, 0, 0, -value.Height, 0, value.Height);
    public static double Cached(Shape shape, string name, double fallback)
    {
        var cell = shape.Cells.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        return cell is not null && double.TryParse(cell.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
    }
}
