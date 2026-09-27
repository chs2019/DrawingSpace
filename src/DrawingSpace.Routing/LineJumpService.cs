using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

public readonly record struct LineJump(int Segment, PointD Point, double Radius);
public sealed record LineJumpResult(IReadOnlyDictionary<string, IReadOnlyList<LineJump>> Jumps, bool BudgetExceeded);

/// <summary>Deterministic crossing ownership in drawing order. Shared endpoints and collinear overlaps never get bridges.</summary>
public static class LineJumpService
{
    public static LineJumpResult Analyze(IReadOnlyList<Connector> edges, IReadOnlyDictionary<string, RouteResult> routes, int maximumComparisons = 200000)
    {
        if (maximumComparisons < 1) throw new ArgumentOutOfRangeException(nameof(maximumComparisons));
        var result = new Dictionary<string, IReadOnlyList<LineJump>>(StringComparer.Ordinal); var comparisons = 0;
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index]; var jumps = new List<LineJump>(); result[edge.Id] = jumps;
            if (edge.LineJumps == LineJumpStyle.None || !routes.TryGetValue(edge.Id, out var current)) continue;
            for (var segment = 1; segment < current.Points.Count; segment++)
            {
                var a = current.Points[segment - 1]; var b = current.Points[segment]; var length = a.Distance(b);
                for (var previous = 0; previous < index; previous++)
                {
                    if (!routes.TryGetValue(edges[previous].Id, out var other)) continue;
                    for (var k = 1; k < other.Points.Count; k++)
                    {
                        if (++comparisons > maximumComparisons) return new(result, true);
                        if (!Intersect(a, b, other.Points[k - 1], other.Points[k], out var crossing)) continue;
                        var radius = Math.Min(edge.JumpSize, Math.Min(a.Distance(crossing), b.Distance(crossing)) * .45);
                        if (radius < .5 || length < 2 || jumps.Any(j => j.Segment == segment && j.Point.Distance(crossing) < radius + j.Radius)) continue;
                        jumps.Add(new(segment, crossing, radius));
                    }
                }
            }
        }
        return new(result, false);
    }
    private static bool Intersect(PointD a, PointD b, PointD c, PointD d, out PointD point)
    {
        point = default; var u = b - a; var v = d - c; var denominator = u.X * v.Y - u.Y * v.X;
        if (Math.Abs(denominator) <= 1e-10 * Math.Max(1, u.Length * v.Length)) return false;
        var w = c - a; var t = (w.X * v.Y - w.Y * v.X) / denominator; var s = (w.X * u.Y - w.Y * u.X) / denominator;
        if (t <= 1e-7 || t >= 1 - 1e-7 || s <= 1e-7 || s >= 1 - 1e-7) return false;
        point = a + u * t; return point.IsFinite;
    }
    public static PointD LabelPoint(Connector edge, RouteResult route)
    {
        var length = 0d;
        for (var i = 1; i < route.Points.Count; i++) length += route.Points[i - 1].Distance(route.Points[i]);
        var distance = length * Math.Clamp(edge.LabelPosition, 0, 1);
        for (var i = 1; i < route.Points.Count; i++)
        {
            var a = route.Points[i - 1]; var b = route.Points[i]; var segment = a.Distance(b);
            if (distance <= segment && segment > 1e-9) return a + (b - a) * (distance / segment) + edge.LabelOffset;
            distance -= segment;
        }
        return (route.Points.LastOrDefault()) + edge.LabelOffset;
    }
}
