using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

/// <summary>Visibility-grid A* with bend costs. Attached endpoints are recomputed from shape identities.</summary>
public sealed class OrthogonalRouter
{
    public double Clearance { get; init; } = 12;
    public double BendPenalty { get; init; } = 20;
    public int MaximumObstacles { get; init; } = 36;

    public RouteResult Route(DiagramPage page, Connector connector)
    {
        var source = page.Find(connector.SourceId);
        var target = page.Find(connector.TargetId);
        var from = source?.Bounds.Center ?? connector.Start;
        var to = target?.Bounds.Center ?? connector.End;
        var sourceSide = connector.SourcePort == PortSide.Auto ? Facing(from, to) : connector.SourcePort;
        var targetSide = connector.TargetPort == PortSide.Auto ? Facing(to, from) : connector.TargetPort;
        if (source == target && source is not null && connector.SourcePort == PortSide.Auto && connector.TargetPort == PortSide.Auto)
        {
            sourceSide = PortSide.East;
            targetSide = PortSide.South;
        }
        var a = source?.Port(sourceSide) ?? connector.Start;
        var b = target?.Port(targetSide) ?? connector.End;
        if (connector.Kind == ConnectorKind.Straight) return new([a, b], true);
        var start = source is null ? a : a + Direction(sourceSide).Rotate(source.Rotation, PointD.Zero) * (Clearance + 8);
        var end = target is null ? b : b + Direction(targetSide).Rotate(target.Rotation, PointD.Zero) * (Clearance + 8);
        var corridor = RectD.FromPoints(start, end).Inflate(256);
        var all = page.Shapes.Where(s => page.IsVisible(s.LayerId) && s.Kind is not ShapeKind.Text and not ShapeKind.Container and not ShapeKind.Annotation)
            .Select(s => s.WorldBounds.Inflate(Clearance)).Where(r => r.Intersects(corridor)).ToArray();
        var obstacles = all.OrderBy(r => r.Center.Distance(corridor.Center)).Take(MaximumObstacles).ToArray();
        var middle = Search(start, end, obstacles);
        if (middle is null)
        {
            var alternatives = new[]
            {
                new[] { start, new PointD(start.X, end.Y), end },
                new[] { start, new PointD(end.X, start.Y), end },
                new[] { start, new PointD(start.X, corridor.Top), new PointD(end.X, corridor.Top), end },
                new[] { start, new PointD(corridor.Right, start.Y), new PointD(corridor.Right, end.Y), end }
            };
            middle = alternatives.FirstOrDefault(p => Clear(p, all)) ?? alternatives[0];
        }
        var clear = Clear(middle, all);
        return new(Simplify(new[] { a }.Concat(middle).Append(b)), clear);
    }

    public static PortSide Facing(PointD from, PointD to)
    {
        var d = to - from;
        return Math.Abs(d.X) > Math.Abs(d.Y)
            ? d.X >= 0 ? PortSide.East : PortSide.West
            : d.Y >= 0 ? PortSide.South : PortSide.North;
    }
    public static PointD Direction(PortSide side) => side switch
    {
        PortSide.North => new(0, -1), PortSide.East => new(1, 0),
        PortSide.South => new(0, 1), PortSide.West => new(-1, 0), _ => default
    };
    private IReadOnlyList<PointD>? Search(PointD start, PointD end, RectD[] obstacles)
    {
        if (start.Distance(end) < 1e-8) return [start, end];
        var xs = obstacles.SelectMany(r => new[] { r.Left - 1, r.Right + 1 }).Append(start.X).Append(end.X).Distinct().Order().ToArray();
        var ys = obstacles.SelectMany(r => new[] { r.Top - 1, r.Bottom + 1 }).Append(start.Y).Append(end.Y).Distinct().Order().ToArray();
        var nx = xs.Length;
        var ny = ys.Length;
        var startNode = Array.IndexOf(ys, start.Y) * nx + Array.IndexOf(xs, start.X);
        var endNode = Array.IndexOf(ys, end.Y) * nx + Array.IndexOf(xs, end.X);
        PointD Point(int node) => new(xs[node % nx], ys[node / nx]);
        var costs = new Dictionary<(int Node, int Direction), double>();
        var parents = new Dictionary<(int Node, int Direction), (int Node, int Direction)>();
        var queue = new PriorityQueue<(int Node, int Direction), double>();
        var initial = (Node: startNode, Direction: 0);
        costs[initial] = 0;
        queue.Enqueue(initial, start.Manhattan(end));
        var visited = new HashSet<(int Node, int Direction)>();
        while (queue.TryDequeue(out var state, out _))
        {
            if (!visited.Add(state)) continue;
            if (state.Node == endNode)
            {
                var path = new List<PointD> { Point(state.Node) };
                while (parents.TryGetValue(state, out var parent)) { state = parent; path.Add(Point(state.Node)); }
                path.Reverse();
                return Simplify(path);
            }
            var x = state.Node % nx;
            var y = state.Node / nx;
            var p = Point(state.Node);
            foreach (var (dx, dy, direction) in new[] { (-1, 0, 1), (1, 0, 1), (0, -1, 2), (0, 1, 2) })
            {
                var xx = x + dx; var yy = y + dy;
                if (xx < 0 || xx >= nx || yy < 0 || yy >= ny) continue;
                var next = (Node: yy * nx + xx, Direction: direction);
                var q = Point(next.Node);
                if (obstacles.Any(r => CrossesInterior(p, q, r))) continue;
                var cost = costs[state] + p.Manhattan(q) + (state.Direction != 0 && state.Direction != direction ? BendPenalty : 0);
                if (costs.TryGetValue(next, out var previous) && previous <= cost) continue;
                costs[next] = cost; parents[next] = state;
                queue.Enqueue(next, cost + q.Manhattan(end));
            }
        }
        return null;
    }
    public static bool CrossesInterior(PointD a, PointD b, RectD r)
    {
        const double epsilon = 1e-7;
        if (Math.Abs(a.X - b.X) < epsilon)
            return a.X > r.Left + epsilon && a.X < r.Right - epsilon && Math.Max(a.Y, b.Y) > r.Top + epsilon && Math.Min(a.Y, b.Y) < r.Bottom - epsilon;
        if (Math.Abs(a.Y - b.Y) < epsilon)
            return a.Y > r.Top + epsilon && a.Y < r.Bottom - epsilon && Math.Max(a.X, b.X) > r.Left + epsilon && Math.Min(a.X, b.X) < r.Right - epsilon;
        // Rotated port escape segments are checked conservatively.
        return RectD.FromPoints(a, b).Intersects(r);
    }
    private static bool Clear(IReadOnlyList<PointD> points, RectD[] obstacles)
    {
        for (var i = 1; i < points.Count; i++)
            if (obstacles.Any(r => CrossesInterior(points[i - 1], points[i], r))) return false;
        return true;
    }
    public static IReadOnlyList<PointD> Simplify(IEnumerable<PointD> input)
    {
        var result = new List<PointD>();
        foreach (var p in input)
        {
            if (result.Count > 0 && result[^1].Distance(p) < 1e-7) continue;
            if (result.Count >= 2)
            {
                var a = result[^2]; var b = result[^1];
                if (Math.Abs((b.X - a.X) * (p.Y - b.Y) - (b.Y - a.Y) * (p.X - b.X)) < 1e-7 && (b - a).X * (p - b).X + (b - a).Y * (p - b).Y >= 0) result.RemoveAt(result.Count - 1);
            }
            result.Add(p);
        }
        return result;
    }
}
