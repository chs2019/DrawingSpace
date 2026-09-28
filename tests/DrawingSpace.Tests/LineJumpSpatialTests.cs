using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class LineJumpSpatialTests
{
    [Fact]
    public void SparseRoutesDoNotConsumeTheExactIntersectionBudget()
    {
        var edges = Enumerable.Range(0, 10000).Select(i => new Connector { Id = "edge-" + i }).ToArray();
        var routes = edges.Select((e, i) => (e.Id, Route: new RouteResult(new PointD[] { new(i * 20, 0), new(i * 20 + 10, 10) }, true)))
            .ToDictionary(p => p.Id, p => p.Route);
        foreach (var edge in edges) edge.LineJumps = LineJumpStyle.Arc;
        var result = LineJumpService.Analyze(edges, routes, maximumComparisons: 1);
        Assert.False(result.BudgetExceeded);
        Assert.Equal(0, result.Comparisons);
        Assert.Equal(edges.Length, result.IndexedSegments);
        Assert.Equal(edges.Length, result.Jumps.Count);
        Assert.All(result.Jumps.Values, Assert.Empty);
    }

    [Fact]
    public void DenseRoutesStillRespectTheComparisonBudget()
    {
        var edges = Enumerable.Range(0, 20).Select(i => new Connector { Id = "edge-" + i, LineJumps = LineJumpStyle.Arc }).ToArray();
        var routes = edges.ToDictionary(e => e.Id, _ => new RouteResult(new PointD[] { new(0, 0), new(100, 100) }, true));
        var result = LineJumpService.Analyze(edges, routes, maximumComparisons: 7);
        Assert.True(result.BudgetExceeded);
        Assert.Equal(7, result.Comparisons);
        Assert.Equal(edges.Length, result.Jumps.Count);
    }

    [Fact]
    public void SegmentStorageIsBoundedAndReportsIncompleteAnalysis()
    {
        var edges = new[] { new Connector { Id = "a", LineJumps = LineJumpStyle.Arc } };
        var routes = new Dictionary<string, RouteResult> { ["a"] = new(new PointD[] { new(0, 0), new(10, 0), new(20, 0) }, true) };
        var result = LineJumpService.Analyze(edges, routes, maximumSegments: 1);
        Assert.True(result.BudgetExceeded);
        Assert.Equal(1, result.IndexedSegments);
        Assert.Empty(result.Jumps["a"]);
    }

    [Fact]
    public void NoneStyleStillParticipatesAsAnEarlierCrossedLine()
    {
        var edges = new[] { new Connector { Id = "a", LineJumps = LineJumpStyle.None }, new Connector { Id = "b", LineJumps = LineJumpStyle.Arc, JumpSize = 6 } };
        var routes = new Dictionary<string, RouteResult>
        {
            ["a"] = new(new PointD[] { new(0, 50), new(100, 50) }, true),
            ["b"] = new(new PointD[] { new(50, 0), new(50, 100) }, true)
        };
        var result = LineJumpService.Analyze(edges, routes);
        Assert.Empty(result.Jumps["a"]);
        Assert.Equal(new LineJump(1, new PointD(50, 50), 6), Assert.Single(result.Jumps["b"]));
        Assert.Equal(1, result.Comparisons);
    }

    [Theory]
    [InlineData(50, 50, 50, 100)]
    [InlineData(0, 50, 100, 50)]
    [InlineData(50, 50, 50, 50)]
    public void SharedEndpointsCollinearityAndZeroLengthDoNotCreateBridges(double x1, double y1, double x2, double y2)
    {
        var edges = new[] { new Connector { Id = "a" }, new Connector { Id = "b", LineJumps = LineJumpStyle.Arc } };
        var routes = new Dictionary<string, RouteResult>
        {
            ["a"] = new(new PointD[] { new(0, 50), new(100, 50) }, true),
            ["b"] = new(new PointD[] { new(x1, y1), new(x2, y2) }, true)
        };
        Assert.All(LineJumpService.Analyze(edges, routes).Jumps.Values, Assert.Empty);
    }

    [Fact]
    public void SpatialTraversalCannotChangeOverlappingJumpPriority()
    {
        var edges = new[]
        {
            new Connector { Id = "first", LineJumps = LineJumpStyle.None },
            new Connector { Id = "second", LineJumps = LineJumpStyle.None },
            new Connector { Id = "owner", LineJumps = LineJumpStyle.Arc, JumpSize = 10 }
        };
        var routes = new Dictionary<string, RouteResult>
        {
            ["first"] = new(new PointD[] { new(55, 0), new(55, 100) }, true),
            ["second"] = new(new PointD[] { new(45, 0), new(45, 100) }, true),
            ["owner"] = new(new PointD[] { new(0, 50), new(100, 50) }, true)
        };
        Assert.Equal(new PointD(55, 50), Assert.Single(LineJumpService.Analyze(edges, routes).Jumps["owner"]).Point);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(891)]
    [InlineData(123456)]
    public void IndexedResultsMatchUnboundedExhaustiveReference(int seed)
    {
        var random = new Random(seed);
        var edges = Enumerable.Range(0, 80).Select(i => new Connector
        {
            Id = "edge-" + i, JumpSize = 1 + random.NextDouble() * 12,
            LineJumps = i % 5 == 0 ? LineJumpStyle.None : LineJumpStyle.Arc
        }).ToArray();
        var routes = new Dictionary<string, RouteResult>();
        foreach (var edge in edges)
        {
            var points = Enumerable.Range(0, 5).Select(_ => new PointD(random.Next(-300, 301), random.Next(-300, 301))).ToArray();
            routes[edge.Id] = new(points, true);
        }
        var expected = Exhaustive(edges, routes);
        var actual = LineJumpService.Analyze(edges, routes, int.MaxValue);
        Assert.False(actual.BudgetExceeded);
        foreach (var edge in edges) Assert.Equal(expected[edge.Id].ToArray(), actual.Jumps[edge.Id].ToArray());
        Assert.True(actual.Comparisons < 80 * 79 / 2 * 16);
    }

    [Fact]
    public void MissingRoutesAndDisabledJumpsAreCheapAndExplicit()
    {
        var edge = new Connector { Id = "missing", LineJumps = LineJumpStyle.None };
        var result = LineJumpService.Analyze(new[] { edge }, new Dictionary<string, RouteResult>());
        Assert.Empty(result.Jumps[edge.Id]);
        Assert.Equal(0, result.IndexedSegments);
        Assert.False(result.BudgetExceeded);
        Assert.Throws<ArgumentOutOfRangeException>(() => LineJumpService.Analyze(new[] { edge }, new Dictionary<string, RouteResult>(), 0));
        Assert.Throws<ArgumentException>(() => LineJumpService.Analyze(new[] { edge, edge }, new Dictionary<string, RouteResult>()));
    }

    // Independent exhaustive baseline keeps painter/segment order and the original
    // intersection tolerance. This test is about output equivalence, not stopwatch timing.
    private static Dictionary<string, List<LineJump>> Exhaustive(IReadOnlyList<Connector> edges, IReadOnlyDictionary<string, RouteResult> routes)
    {
        var result = new Dictionary<string, List<LineJump>>();
        for (var owner = 0; owner < edges.Count; owner++)
        {
            var edge = edges[owner]; var jumps = new List<LineJump>(); result[edge.Id] = jumps;
            if (edge.LineJumps == LineJumpStyle.None || !routes.TryGetValue(edge.Id, out var route)) continue;
            for (var segment = 1; segment < route.Points.Count; segment++)
            {
                var a = route.Points[segment - 1]; var b = route.Points[segment];
                for (var earlier = 0; earlier < owner; earlier++)
                {
                    if (!routes.TryGetValue(edges[earlier].Id, out var other)) continue;
                    for (var k = 1; k < other.Points.Count; k++)
                    {
                        var c = other.Points[k - 1]; var d = other.Points[k];
                        var u = b - a; var v = d - c; var den = u.X * v.Y - u.Y * v.X;
                        if (Math.Abs(den) <= 1e-10 * Math.Max(1, u.Length * v.Length)) continue;
                        var w = c - a; var t = (w.X * v.Y - w.Y * v.X) / den; var s = (w.X * u.Y - w.Y * u.X) / den;
                        if (t <= 1e-7 || t >= 1 - 1e-7 || s <= 1e-7 || s >= 1 - 1e-7) continue;
                        var point = a + u * t;
                        var radius = Math.Min(edge.JumpSize, Math.Min(a.Distance(point), b.Distance(point)) * .45);
                        if (!point.IsFinite || radius < .5 || a.Distance(b) < 2 || jumps.Any(j => j.Segment == segment && j.Point.Distance(point) < radius + j.Radius)) continue;
                        jumps.Add(new(segment, point, radius));
                    }
                }
            }
        }
        return result;
    }
}
