using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class LineJumpFastPathTests
{
    private static Connector Edge(string id, LineJumpStyle style = LineJumpStyle.Arc) => new() { Id = id, LineJumps = style, JumpSize = 5 };
    private static RouteResult Route(params PointD[] points) => new(points, true);

    [Fact] public void OneEffectiveRouteSkipsTheSpatialTreeButChargesSegments()
    {
        var routes = new Dictionary<string, RouteResult> { ["a"] = Route(Enumerable.Range(0, 4097).Select(i => new PointD(i % 2 * 100, 0)).ToArray()) };
        var result = LineJumpService.Analyze(new[] { Edge("missing"), Edge("a") }, routes);
        Assert.False(result.SpatialIndexBuilt); Assert.Equal(4096, result.IndexedSegments);
        Assert.Equal(0, result.SpatialQueries); Assert.Empty(result.Jumps["a"]);
        Assert.True(LineJumpService.Analyze(new[] { Edge("a") }, routes, maximumSegments: 4095).BudgetExceeded);
    }

    [Fact] public void LaterNonJumpingOwnerDoesNotRequireAnIndex()
    {
        var routes = new Dictionary<string, RouteResult>
        { ["a"] = Route(new(0, 50), new(100, 50)), ["b"] = Route(new(50, 0), new(50, 100)) };
        var result = LineJumpService.Analyze(new[] { Edge("a"), Edge("b", LineJumpStyle.None) }, routes);
        Assert.False(result.SpatialIndexBuilt); Assert.Equal(2, result.IndexedSegments);
        Assert.All(result.Jumps.Values, jumps => Assert.Empty(jumps));
    }

    [Fact] public void LaterJumpingOwnerStillGetsItsBridgeOverANonJumpingOwner()
    {
        var routes = new Dictionary<string, RouteResult>
        { ["a"] = Route(new(0, 50), new(100, 50)), ["b"] = Route(new(50, 0), new(50, 100)) };
        var result = LineJumpService.Analyze(new[] { Edge("a", LineJumpStyle.None), Edge("b") }, routes);
        Assert.True(result.SpatialIndexBuilt); Assert.Equal(new PointD(50, 50), Assert.Single(result.Jumps["b"]).Point);
        Assert.Empty(result.Jumps["a"]);
    }

    [Fact] public void FastPathDoesNotBypassInvalidRouteOrDuplicateIdentityChecks()
    {
        var routes = new Dictionary<string, RouteResult> { ["a"] = Route(new(0, 0), new(double.NaN, 0)) };
        Assert.Throws<ArgumentException>(() => LineJumpService.Analyze(new[] { Edge("a") }, routes));
        Assert.Throws<ArgumentException>(() => LineJumpService.Analyze(new[] { Edge("a"), Edge("a") }, routes));
    }
}
