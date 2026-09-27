using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static class ShapeTransforms
{
    // Clockwise: northwest, north, northeast, east, southeast, south, southwest, west.
    public static PointD[] Handles(Shape shape)
    {
        var b = shape.Bounds;
        return new[] { new PointD(b.Left, b.Top), new(b.Center.X, b.Top), new(b.Right, b.Top), new(b.Right, b.Center.Y), new(b.Right, b.Bottom), new(b.Center.X, b.Bottom), new(b.Left, b.Bottom), new(b.Left, b.Center.Y) }
            .Select(p => p.Rotate(shape.Rotation, b.Center)).ToArray();
    }
    public static void Resize(Shape shape, Shape original, int handle, PointD start, PointD current, bool preserveAspect)
    {
        if (handle is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(handle));
        var b = original.Bounds;
        var delta = current.Rotate(-original.Rotation, b.Center) - start.Rotate(-original.Rotation, b.Center);
        var left = b.Left; var right = b.Right; var top = b.Top; var bottom = b.Bottom;
        var west = handle is 0 or 6 or 7; var east = handle is 2 or 3 or 4;
        var north = handle is 0 or 1 or 2; var south = handle is 4 or 5 or 6;
        if (west) left = Math.Min(right - 16, left + delta.X);
        if (east) right = Math.Max(left + 16, right + delta.X);
        if (north) top = Math.Min(bottom - 16, top + delta.Y);
        if (south) bottom = Math.Max(top + 16, bottom + delta.Y);
        if (preserveAspect)
        {
            var aspect = original.Width / original.Height;
            if (east || west)
            {
                var height = (right - left) / aspect;
                if (north) top = bottom - height;
                else if (south) bottom = top + height;
                else { top = b.Center.Y - height / 2; bottom = b.Center.Y + height / 2; }
            }
            else
            {
                var width = (bottom - top) * aspect;
                left = b.Center.X - width / 2; right = b.Center.X + width / 2;
            }
        }
        var center = new PointD((left + right) / 2, (top + bottom) / 2).Rotate(original.Rotation, b.Center);
        shape.Width = right - left; shape.Height = bottom - top;
        shape.X = center.X - shape.Width / 2; shape.Y = center.Y - shape.Height / 2;
    }
}
