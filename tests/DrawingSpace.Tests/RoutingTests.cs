using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Tests;

public sealed class RoutingTests
{
    [Fact] public void RoutesAroundObstacles()
    {
        var page = new DiagramPage();
        page.Shapes.AddRange([new() { Id = "a", X = 20, Y = 100, Width = 100, Height = 60 }, new() { Id = "b", X = 460, Y = 100, Width = 100, Height = 60 }, new() { X = 240, Y = 60, Width = 100, Height = 160 }]);
        var route = new OrthogonalRouter().Route(page, new() { SourceId = "a", TargetId = "b" });
        Assert.True(route.IsObstacleFree); Assert.True(route.Points.Count >= 4);
        Assert.Equal(new PointD(120, 130), route.Points[0]); Assert.Equal(new PointD(460, 130), route.Points[^1]);
        for (var i = 1; i < route.Points.Count; i++) Assert.True(Math.Abs(route.Points[i].X - route.Points[i - 1].X) < 1e-7 || Math.Abs(route.Points[i].Y - route.Points[i - 1].Y) < 1e-7);
    }
    [Fact] public void MovingSourceRecomputesAttachment()
    {
        var page = new DiagramPage(); var source = new Shape { Id = "a" }; page.Shapes.Add(source); page.Shapes.Add(new() { Id = "b", X = 400 });
        var connector = new Connector { SourceId = "a", TargetId = "b" }; var router = new OrthogonalRouter(); var first = router.Route(page, connector).Points[0];
        source.Y += 100; var next = router.Route(page, connector).Points[0]; Assert.NotEqual(first, next);
    }
    [Fact] public void StraightRouteHasTwoPoints()
    {
        var result = new OrthogonalRouter().Route(new(), new() { Start = new(1, 2), End = new(300, 200), Kind = ConnectorKind.Straight }); Assert.Equal(2, result.Points.Count);
    }
    [Fact] public void SimplificationRetainsReversal()
    {
        var simplified = OrthogonalRouter.Simplify([new(0, 0), new(10, 0), new(0, 0)]); Assert.Equal(3, simplified.Count);
    }
    [Fact] public void SimplificationRemovesCollinearAndDuplicatePoints()
    {
        var simplified = OrthogonalRouter.Simplify([new(0, 0), new(0, 0), new(10, 0), new(20, 0), new(20, 10)]); Assert.Equal(3, simplified.Count);
    }
    [Fact] public void SelfLoopRemainsNonzero()
    {
        var page = new DiagramPage(); page.Shapes.Add(new() { Id = "a", X = 100, Y = 100 });
        var result = new OrthogonalRouter().Route(page, new() { SourceId = "a", TargetId = "a" }); Assert.True(result.Length > 100);
    }
}
