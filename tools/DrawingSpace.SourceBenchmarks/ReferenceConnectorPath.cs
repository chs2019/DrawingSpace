using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.Skia;
using SkiaSharp;

internal static class ReferenceConnectorPath
{
    // Retained pre-change algorithm; not used by the application renderer.
    public static SKPath Create(Connector edge, RouteResult route, IReadOnlyList<LineJump>? jumps)
    {
        if (edge.LineJumps == LineJumpStyle.None || jumps is null || jumps.Count == 0) return SceneRenderer.Polyline(route.Points);
        var path = new SKPath(); if (route.Points.Count == 0) return path;
        void Move(PointD p) => path.MoveTo((float)p.X, (float)p.Y);
        void Line(PointD p) => path.LineTo((float)p.X, (float)p.Y);
        void Cubic(PointD a, PointD b, PointD c) => path.CubicTo((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y);
        Move(route.Points[0]);
        for (var index = 1; index < route.Points.Count; index++)
        {
            var a = route.Points[index - 1]; var b = route.Points[index]; var direction = (b - a).Normalized;
            var normal = new PointD(direction.Y, -direction.X);
            if (normal.Y > 0 || Math.Abs(normal.Y) < 1e-8 && normal.X < 0) normal *= -1;
            foreach (var jump in jumps.Where(j => j.Segment == index).OrderBy(j => a.Distance(j.Point)))
            {
                var before = jump.Point - direction * jump.Radius; var after = jump.Point + direction * jump.Radius;
                var top = jump.Point + normal * jump.Radius; var k = jump.Radius * .5522847498307936;
                Line(before);
                switch (edge.LineJumps)
                {
                    case LineJumpStyle.Gap: Move(after); break;
                    case LineJumpStyle.Square: Line(before + normal * jump.Radius); Line(after + normal * jump.Radius); Line(after); break;
                    default: Cubic(before + normal * k, top - direction * k, top); Cubic(top + direction * k, after + normal * k, after); break;
                }
            }
            Line(b);
        }
        return path;
    }
}
