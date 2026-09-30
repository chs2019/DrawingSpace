using System.Buffers;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    /// <summary>
    /// Builds caller-owned connector geometry. Arbitrarily ordered jumps are ordered
    /// once in pooled scratch storage, then consumed in one route traversal. Input
    /// collections are not changed. Equal-distance jumps retain their input order.
    /// </summary>
    public static SKPath ConnectorPath(Connector edge, RouteResult route, IReadOnlyList<LineJump>? jumps)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(route);
        if (edge.LineJumps == LineJumpStyle.None || jumps is null || jumps.Count == 0 || route.Points.Count < 2)
            return PlainConnectorPath(route.Points);
        if (jumps.Count > 262144) throw new ArgumentException("Connector exceeds the rendered jump budget.", nameof(jumps));
        var ordered = ArrayPool<OrderedJump>.Shared.Rent(jumps.Count);
        SKPath? path = null;
        try
        {
            path = new SKPath();
            var count = 0;
            for (var i = 0; i < jumps.Count; i++)
            {
                var jump = jumps[i];
                if (jump.Segment < 1 || jump.Segment >= route.Points.Count) continue;
                if (!jump.Point.IsFinite || !double.IsFinite(jump.Radius) || jump.Radius < 0)
                    throw new ArgumentException("Jump geometry must be finite with a nonnegative radius.", nameof(jumps));
                ordered[count++] = new(jump, route.Points[jump.Segment - 1].Distance(jump.Point), i);
            }
            Array.Sort(ordered, 0, count);
            Move(path, route.Points[0]);
            var next = 0;
            for (var segment = 1; segment < route.Points.Count; segment++)
            {
                var a = route.Points[segment - 1]; var b = route.Points[segment];
                if (next < count && ordered[next].Jump.Segment == segment)
                {
                    var direction = (b - a).Normalized;
                    var normal = new PointD(direction.Y, -direction.X);
                    if (normal.Y > 0 || Math.Abs(normal.Y) < 1e-8 && normal.X < 0) normal *= -1;
                    while (next < count && ordered[next].Jump.Segment == segment)
                    {
                        var jump = ordered[next++].Jump;
                        var before = jump.Point - direction * jump.Radius; var after = jump.Point + direction * jump.Radius;
                        var top = jump.Point + normal * jump.Radius; var k = jump.Radius * .5522847498307936;
                        Line(path, before);
                        switch (edge.LineJumps)
                        {
                            case LineJumpStyle.Gap: Move(path, after); break;
                            case LineJumpStyle.Square:
                                Line(path, before + normal * jump.Radius); Line(path, after + normal * jump.Radius); Line(path, after); break;
                            default:
                                Cubic(path, before + normal * k, top - direction * k, top);
                                Cubic(path, top + direction * k, after + normal * k, after); break;
                        }
                    }
                }
                Line(path, b);
            }
            return path;
        }
        catch { path?.Dispose(); throw; }
        finally { ArrayPool<OrderedJump>.Shared.Return(ordered); }
    }

    private static SKPath PlainConnectorPath(IReadOnlyList<PointD> points)
    {
        var path = new SKPath();
        try
        {
            if (points.Count > 0)
            {
                Move(path, points[0]);
                for (var i = 1; i < points.Count; i++) Line(path, points[i]);
            }
            return path;
        }
        catch { path.Dispose(); throw; }
    }

    private static void Move(SKPath path, PointD point) => path.MoveTo((float)point.X, (float)point.Y);
    private static void Line(SKPath path, PointD point) => path.LineTo((float)point.X, (float)point.Y);
    private static void Cubic(SKPath path, PointD a, PointD b, PointD c)
        => path.CubicTo((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y);

    private readonly record struct OrderedJump(LineJump Jump, double Distance, int Ordinal) : IComparable<OrderedJump>
    {
        public int CompareTo(OrderedJump other)
        {
            var comparison = Jump.Segment.CompareTo(other.Jump.Segment);
            if (comparison == 0) comparison = Distance.CompareTo(other.Distance);
            return comparison != 0 ? comparison : Ordinal.CompareTo(other.Ordinal);
        }
    }
}
