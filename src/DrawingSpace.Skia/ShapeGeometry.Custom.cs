using DrawingSpace.Core;
using DrawingSpace.Documents;
using SkiaSharp;

namespace DrawingSpace.Skia;

public static partial class ShapeGeometry
{
    public static SKMatrix Matrix(MatrixD m) => new((float)m.A, (float)m.C, (float)m.Tx, (float)m.B, (float)m.D, (float)m.Ty, 0, 0, 1);
    public static SKPath CreateFigure(Shape shape, GeometryFigure figure)
    {
        var path = new SKPath { FillType = figure.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        SKPoint Point(PointD p) => new((float)(shape.X + p.X * shape.Width), (float)(shape.Y + p.Y * shape.Height));
        var current = PointD.Zero; var first = PointD.Zero;
        void Cubic(GeometrySegment segment) => path.CubicTo(Point(segment.Control1), Point(segment.Control2), Point(segment.End));
        foreach (var segment in figure.Segments)
        {
            switch (segment.Verb)
            {
                case GeometryVerb.Move: path.MoveTo(Point(segment.End)); first = segment.End; break;
                case GeometryVerb.Line: path.LineTo(Point(segment.End)); break;
                case GeometryVerb.Quadratic: path.QuadTo(Point(segment.Control1), Point(segment.End)); break;
                case GeometryVerb.Cubic: Cubic(segment); break;
                case GeometryVerb.Arc:
                    if (ArcGeometry.TryFromEndpoint(current, segment, shape.Width, shape.Height, out var arc))
                        foreach (var curve in ArcGeometry.ToCubics(arc, shape.Width, shape.Height)) Cubic(curve);
                    else path.LineTo(Point(segment.End));
                    break;
                case GeometryVerb.Close: path.Close(); current = first; continue;
            }
            current = segment.End;
        }
        return path;
    }
}
