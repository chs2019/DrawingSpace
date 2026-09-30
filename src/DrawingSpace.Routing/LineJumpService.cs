using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

public readonly record struct LineJump(int Segment, PointD Point, double Radius);
public sealed record LineJumpResult(IReadOnlyDictionary<string, IReadOnlyList<LineJump>> Jumps, bool BudgetExceeded)
{
    public int Comparisons { get; init; }
    public int IndexedSegments { get; init; }
    public int SpatialQueries { get; init; }
    public long Candidates { get; init; }
    /// <summary>False when no later jumping owner can cross earlier route segments.</summary>
    public bool SpatialIndexBuilt { get; init; }
}

/// <summary>
/// Deterministic crossing ownership in drawing order. Spatial filtering excludes distant,
/// self and later segments. Shared endpoints and collinear overlaps never get bridges.
/// Segment storage and exact comparisons are bounded. Input routes must remain unchanged.
/// </summary>
public static class LineJumpService
{
    public static LineJumpResult Analyze(IReadOnlyList<Connector> edges, IReadOnlyDictionary<string, RouteResult> routes,
        int maximumComparisons = 200000, int maximumSegments = 262144)
    {
        ArgumentNullException.ThrowIfNull(edges); ArgumentNullException.ThrowIfNull(routes);
        if (maximumComparisons < 1) throw new ArgumentOutOfRangeException(nameof(maximumComparisons));
        if (maximumSegments is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        var result = new Dictionary<string, IReadOnlyList<LineJump>>(edges.Count, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            ArgumentNullException.ThrowIfNull(edge);
            if (!result.TryAdd(edge.Id, Array.Empty<LineJump>()))
                throw new ArgumentException("Connector identities must be unique.", nameof(edges));
        }
        var comparisons = 0; var spatialQueries = 0; var candidateCount = 0L; var indexBuilt = false;
        var segments = new List<IndexedSegment>();
        LineJumpResult Finish(bool exceeded) => new(result, exceeded)
        {
            Comparisons = comparisons, IndexedSegments = segments.Count,
            SpatialQueries = spatialQueries, Candidates = candidateCount, SpatialIndexBuilt = indexBuilt
        };
        if (!edges.Any(e => e.LineJumps != LineJumpStyle.None && e.JumpSize >= .5)) return Finish(false);
        var ownerStarts = new int[edges.Count]; var requiresIndex = false;
        for (var owner = 0; owner < edges.Count; owner++)
        {
            ownerStarts[owner] = segments.Count;
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
                if (ownerStarts[owner] > 0 && edges[owner].LineJumps != LineJumpStyle.None
                    && !(edges[owner].JumpSize < .5) && a.Distance(b) >= 2) requiresIndex = true;
            }
        }
        // A single effective route, or only non-jumping later owners, cannot own bridges.
        // Validate and charge segment budgets above before skipping BVH allocation/sorting.
        if (!requiresIndex) return Finish(false);
        var spatial = new SpatialBoundsIndex(segments.Select(s => s.Bounds)); indexBuilt = true;
        var candidates = new List<int>();
        List<LineJump>? jumps = null; var activeOwner = -1;
        foreach (var current in segments)
        {
            var edge = edges[current.Owner];
            if (edge.LineJumps == LineJumpStyle.None || edge.JumpSize < .5 || ownerStarts[current.Owner] == 0) continue;
            if (activeOwner != current.Owner)
            { activeOwner = current.Owner; jumps = new List<LineJump>(); result[edge.Id] = jumps; }
            var length = current.A.Distance(current.B);
            if (length < 2) continue;
            candidates.Clear(); spatialQueries++;
            spatial.Query(current.Bounds, candidates, ownerStarts[current.Owner]); candidateCount += candidates.Count;
            candidates.Sort();
            var segmentJumpStart = jumps!.Count;
            foreach (var ordinal in candidates)
            {
                var other = segments[ordinal];
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
        var distance = length * edge.LabelPosition;
        for (var i = 1; i < route.Points.Count; i++)
        {
            var a = route.Points[i - 1]; var b = route.Points[i]; var segment = a.Distance(b);
            if (distance <= segment && segment > 1e-9) return a + (b - a) * (distance / segment) + edge.LabelOffset;
            distance -= segment;
        }
        return route.Points.LastOrDefault() + edge.LabelOffset;
    }
}
