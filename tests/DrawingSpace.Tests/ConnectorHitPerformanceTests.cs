using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;

namespace DrawingSpace.Tests;

public sealed class ConnectorHitPerformanceTests
{
    [Fact] public void ReversePainterOrderAndCachedHitAllocateNoIteratorChains()
    {
        var page = new DiagramPage();
        page.Connectors.Add(new() { Id = "back", Kind = ConnectorKind.Straight, Start = new(0, 10), End = new(100, 10) });
        page.Connectors.Add(new() { Id = "front", Kind = ConnectorKind.Straight, Start = new(0, 10), End = new(100, 10) });
        using var renderer = new SceneRenderer();
        for (var i = 0; i < 1000; i++) renderer.HitConnector(page, new(50, 10), 0, 1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) renderer.HitConnector(page, new(50, 10), 0, 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal("front", renderer.HitConnector(page, new(50, 10), 0, 1)!.Id);
        Assert.Null(renderer.HitConnector(page, new(50, 20), 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.HitConnector(page, new(0, 0), 0, double.NaN));
    }
}
