using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Tests;

public sealed class RoutingSceneTests
{
    [Fact] public void SnapshotIsUnaffectedByLaterShapePortAndVisibilityMutations()
    {
        var page = new DiagramPage();
        var a = new Shape { Id = "a", X = 20, Y = 30 }; var b = new Shape { Id = "b", X = 400, Y = 30 };
        a.ConnectionPoints.Add(new() { Id = "p", Position = new(1, .2), Direction = new(1, 0) });
        page.Shapes.AddRange([a, b]); var scene = RoutingScene.Capture(page);
        var edge = new Connector { SourceId = "a", TargetId = "b", SourcePointId = "p", Kind = ConnectorKind.Straight };
        var router = new OrthogonalRouter(); var original = router.RouteSnapshot(scene, edge);
        a.X += 100; a.ConnectionPoints[0].Position = new(0, .7); page.Layers[0].Visible = false;
        Assert.Equal(original.Points, router.RouteSnapshot(scene, edge).Points);
        Assert.NotEqual(original.Points[0], router.Route(page, edge).Points[0]);
    }

    [Theory] [InlineData(0, false)] [InlineData(33, false)] [InlineData(127, true)] [InlineData(-80, true)]
    public void CapturedEndpointsMatchAffinePortResolver(double angle, bool flip)
    {
        var page = new DiagramPage();
        var shape = new Shape { Id = "a", X = 20, Y = 30, Rotation = angle, ShearX = .2, FlipY = flip };
        shape.ConnectionPoints.Add(new() { Id = "p", Position = new(.8, .3), Direction = new(.4, .8) }); page.Shapes.Add(shape);
        var router = new OrthogonalRouter(); var scene = RoutingScene.Capture(page); var end = new PointD(500, 280);
        foreach (var side in Enum.GetValues<PortSide>())
        foreach (var point in new string?[] { null, "p" })
        {
            var expected = ConnectionEndpoints.Resolve(shape, point, side, default, end);
            var route = router.RouteSnapshot(scene, new() { SourceId = "a", SourcePointId = point, SourcePort = side, End = end, Kind = ConnectorKind.Straight });
            Assert.Equal(expected.Position, route.Points[0]);
        }
    }

    [Fact] public void SharedSnapshotRoutesRespectVisibleObstaclesAndExplicitWaypoints()
    {
        var page = new DiagramPage(); page.Shapes.Add(new() { Id = "obstacle", X = 180, Y = 70, Width = 60, Height = 80 });
        var edge = new Connector { Start = new(20, 100), End = new(420, 100), Waypoints = [new(100, 20), new(300, 20)] };
        var router = new OrthogonalRouter(); var route = router.RouteSnapshot(RoutingScene.Capture(page), edge);
        Assert.True(route.IsObstacleFree);
        for (var i = 1; i < route.Points.Count; i++)
        {
            Assert.True(Math.Abs(route.Points[i].X - route.Points[i - 1].X) < 1e-7 || Math.Abs(route.Points[i].Y - route.Points[i - 1].Y) < 1e-7);
            Assert.False(OrthogonalRouter.CrossesInterior(route.Points[i - 1], route.Points[i], page.Shapes[0].WorldBounds.Inflate(router.Clearance)));
        }
    }

    [Fact] public void SparseBatchHasBoundedAllocationInsteadOfRepeatedWholePageScans()
    {
        var page = new DiagramPage();
        for (var i = 0; i < 5000; i++) page.Shapes.Add(new() { Id = "s" + i, X = i % 100 * 240, Y = i / 100 * 160, Width = 96, Height = 56 });
        var edge = new Connector { SourceId = "s0", TargetId = "s1", SourcePort = PortSide.East, TargetPort = PortSide.West };
        var router = new OrthogonalRouter(); var scene = RoutingScene.Capture(page); router.RouteSnapshot(scene, edge);
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) _ = router.RouteSnapshot(scene, edge);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - bytes, 0, 1_000_000);
    }
}
