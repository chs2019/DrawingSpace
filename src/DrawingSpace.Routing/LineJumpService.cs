using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

public readonly record struct LineJump(int Segment, PointD Point, double Radius);
public sealed record LineJumpResult(IReadOnlyDictionary<string, IReadOnlyList<LineJump>> Jumps, bool BudgetExceeded)
{
    /// <summary>Exact segment intersection tests, after spatial and drawing-order filtering.</summary>
    public int Comparisons { get; init; }
    public int IndexedSegments { get; init; }
}

/// <summary>
/// Deterministic crossing ownership in drawing order. A bulk-built spatial index rejects
/// distant segments before they consume the intersection budget. Shared endpoints and
/// collinear overlaps never get bridges. The input routes must remain unchanged during a call.
/// </summary>
public static class LineJumpService
{
    public static LineJumpResult Analyze(IReadOnlyList<Connector> edges, IReadOnlyDictionary<string, RouteResult> routes,
        int maximumComparisons = 200000, int maximumSegments = 262144)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(routes);
        if (maximumComparisons < 1) throw new ArgumentOutOfRangeException(nameof(maximumComparisons));
        if (maximumSegments is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        var result = new Dictionary<string, IReadOnlyList<LineJump>>(edges.Count, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (!result.TryAdd(edge.Id, Array.Empty<LineJump>()))
                throw new ArgumentException("Connector identities must be unique.", nameof(edges));
        }
        var comparisons = 0;
        var segments = new List<IndexedSegment>();
        LineJumpResult Finish(bool exceeded) => new(result, exceeded) { Comparisons = comparisons, IndexedSegments = segments.Count };
        // Do not allocate an index for pages on which no connector can own a jump.
        if (!edges.Any(e => e.LineJumps != LineJumpStyle.None && e.JumpSize >= .5)) return Finish(false);
        for (var owner = 0; owner < edges.Count; owner++)
        {
            if (!routes.TryGetValue(edges[owner].Id, out var route)) continue;
            for (var segment = 1; segment < route.Points.Count; segment++)
            {
                var a = route.Points[segment - 1]; var b = route.Points[segment];
                if (!a.IsFinite || !b.IsFinite)
                    throw new ArgumentException("Route points must be finite.", nameof(routes));
                if (a == b) continue;
                if (segments.Count == maximumSegments) return Finish(true);
                var bounds = new RectD(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                if (!bounds.IsFinite) throw new ArgumentException("Route bounds exceed the finite coordinate range.", nameof(routes));
                segments.Add(new(owner, segment, a, b, bounds));
            }
        }
        var spatial = new SpatialBoundsIndex(segments.Select(s => s.Bounds));
        var candidates = new List<int>();
        List<LineJump>? jumps = null;
        var activeOwner = -1;
        foreach (var current in segments)
        {
            var edge = edges[current.Owner];
            if (edge.LineJumps == LineJumpStyle.None || edge.JumpSize < .5) continue;
            if (activeOwner != current.Owner)
            {
                activeOwner = current.Owner;
                jumps = new List<LineJump>();
                result[edge.Id] = jumps;
            }
            var length = current.A.Distance(current.B);
            if (length < 2) continue;
            candidates.Clear();
            spatial.Query(current.Bounds, candidates);
            // The BVH's traversal order is spatial, not painter order. Stable original
            // ordinals preserve the old first-crossing-wins overlap suppression rule.
            candidates.Sort();
            var segmentJumpStart = jumps!.Count;
            foreach (var ordinal in candidates)
            {
                var other = segments[ordinal];
                if (other.Owner >= current.Owner) break;
                if (comparisons == maximumComparisons) return Finish(true);
                comparisons++;
                if (!Intersect(current.A, current.B, other.A, other.B, out var crossing)) continue;
                var radius = Math.Min(edge.JumpSize, Math.Min(current.A.Distance(crossing), current.B.Distance(crossing)) * .45);
                if (radius < .5) continue;
                var overlap = false;
                for (var j = segmentJumpStart; j < jumps.Count; j++)
                    if (jumps[j].Point.Distance(crossing) < radius + jumps[j].Radius) { overlap = true; break; }
                if (!overlap) jumps.Add(new(current.Segment, crossing, radius));
            }
        }
        return Finish(false);
    }

    private readonly record struct IndexedSegment(int Owner, int Segment, PointD A, PointD B, RectD Bounds);

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
        return route.Points.LastOrDefault() + edge.LabelOffset;
    }
}
