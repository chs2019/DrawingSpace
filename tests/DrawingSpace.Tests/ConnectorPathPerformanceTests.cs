using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.Skia;
using SkiaSharp;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class ConnectorPathPerformanceTests
{
    [Theory]
    [InlineData(LineJumpStyle.None)]
    [InlineData(LineJumpStyle.Arc)]
    [InlineData(LineJumpStyle.Gap)]
    [InlineData(LineJumpStyle.Square)]
    public void PooledPathMatchesRetainedReferenceForUnorderedJumps(LineJumpStyle style)
    {
        for (var seed = 0; seed < 16; seed++)
        {
            var (edge, route, jumps) = Fixture(64, style);
            var random = new Random(seed);
            for (var i = jumps.Length - 1; i > 0; i--) { var j = random.Next(i + 1); (jumps[i], jumps[j]) = (jumps[j], jumps[i]); }
            var before = jumps.ToArray();
            using var expected = Reference(edge, route, jumps);
            using var actual = SceneRenderer.ConnectorPath(edge, route, jumps);
            Assert.Equal(expected.ToSvgPathData(), actual.ToSvgPathData());
            Assert.Equal(before, jumps);
        }
    }

    [Fact]
    public void OutOfRangeJumpsAreIgnoredAndEqualDistanceOrderIsPreserved()
    {
        var edge = new Connector { LineJumps = LineJumpStyle.Arc };
        var route = new RouteResult([new(0, 0), new(100, 0)], true);
        LineJump[] jumps = [new(1, new(50, 0), 4), new(100, new(0, 0), 4), new(1, new(50, 0), 2), new(-1, new(0, 0), 4)];
        using var expected = Reference(edge, route, jumps);
        using var actual = SceneRenderer.ConnectorPath(edge, route, jumps);
        Assert.Equal(expected.ToSvgPathData(), actual.ToSvgPathData());
    }

    [Fact]
    public void WarmPooledPathAllocatesLessThanPerSegmentLinqReference()
    {
        var (edge, route, jumps) = Fixture(128, LineJumpStyle.Arc);
        for (var i = 0; i < 20; i++)
        { using var a = Reference(edge, route, jumps); using var b = SceneRenderer.ConnectorPath(edge, route, jumps); }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++) { using var path = Reference(edge, route, jumps); }
        var reference = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++) { using var path = SceneRenderer.ConnectorPath(edge, route, jumps); }
        var optimized = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.True(optimized < reference / 4, $"Reference {reference} bytes; optimized {optimized} bytes.");
    }

    [Fact]
    public void InvalidJumpGeometryIsRejectedWithoutPoisoningSubsequentCalls()
    {
        var (edge, route, _) = Fixture(1, LineJumpStyle.Square);
        Assert.Throws<ArgumentException>(() => SceneRenderer.ConnectorPath(edge, route, [new(1, new(1, 0), double.NaN)]));
        using var path = SceneRenderer.ConnectorPath(edge, route, [new(1, new(50, 0), 4)]);
        Assert.False(path.IsEmpty);
    }

    private static (Connector Edge, RouteResult Route, LineJump[] Jumps) Fixture(int segments, LineJumpStyle style)
    {
        var points = new PointD[segments + 1]; var jumps = new List<LineJump>();
        for (var i = 1; i <= segments; i++)
        {
            var delta = (i % 4) switch { 1 => new PointD(100, 0), 2 => new PointD(0, 100), 3 => new PointD(-100, 0), _ => new PointD(0, 100) };
            points[i] = points[i - 1] + delta;
            jumps.Add(new(i, points[i - 1] + delta * .7, 4));
            jumps.Add(new(i, points[i - 1] + delta * .25, 4));
        }
        return (new() { LineJumps = style }, new(points, true), jumps.ToArray());
    }

    // Retained pre-change rendering algorithm, intentionally including its per-segment LINQ work.
    internal static SKPath Reference(Connector edge, RouteResult route, IReadOnlyList<LineJump>? jumps)
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
