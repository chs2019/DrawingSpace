using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static class ShapeTransforms
{
    private static readonly PointD[] UnitHandles = [new(0, 0), new(.5, 0), new(1, 0), new(1, .5), new(1, 1), new(.5, 1), new(0, 1), new(0, .5)];
    public static PointD[] Handles(Shape shape) => UnitHandles.Select(shape.WorldMatrix.Map).ToArray();

    public static void Resize(Shape shape, Shape original, int handle, PointD start, PointD current, bool preserveAspect)
    {
        if (handle is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(handle));
        if (!original.WorldMatrix.TryInvert(out var inverse)) throw new InvalidOperationException("Shape transform is singular.");
        var delta = inverse.Map(current) - inverse.Map(start);
        var left = 0d; var right = 1d; var top = 0d; var bottom = 1d;
        var west = handle is 0 or 6 or 7; var east = handle is 2 or 3 or 4;
        var north = handle is 0 or 1 or 2; var south = handle is 4 or 5 or 6;
        var minimumWidth = Math.Min(1, 16 / original.Width); var minimumHeight = Math.Min(1, 16 / original.Height);
        if (west) left = Math.Min(right - minimumWidth, delta.X);
        if (east) right = Math.Max(left + minimumWidth, 1 + delta.X);
        if (north) top = Math.Min(bottom - minimumHeight, delta.Y);
        if (south) bottom = Math.Max(top + minimumHeight, 1 + delta.Y);
        if (preserveAspect)
        {
            if (east || west)
            {
                var scale = right - left;
                if (north) top = bottom - scale; else if (south) bottom = top + scale;
                else { top = .5 - scale / 2; bottom = .5 + scale / 2; }
            }
            else { var scale = bottom - top; left = .5 - scale / 2; right = .5 + scale / 2; }
        }
        var target = original.WorldMatrix * MatrixD.Translation(left, top) * MatrixD.Scale(right - left, bottom - top);
        var center = target.Map(new PointD(.5, .5));
        shape.X = original.X; shape.Y = original.Y; shape.Width = original.Width; shape.Height = original.Height;
        shape.Rotation = original.Rotation; shape.ShearX = original.ShearX; shape.FlipX = original.FlipX; shape.FlipY = original.FlipY;
        shape.ApplyWorldTransform(target * inverse);
        shape.X = center.X - shape.Width / 2; shape.Y = center.Y - shape.Height / 2;
    }

    public static MatrixD SelectionResize(RectD bounds, int handle, PointD start, PointD current, bool preserveAspect)
    {
        var source = new Shape { X = bounds.X, Y = bounds.Y, Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height) };
        var target = source.Clone(); Resize(target, source, handle, start, current, preserveAspect);
        source.WorldMatrix.TryInvert(out var inverse);
        return target.WorldMatrix * inverse;
    }
}
