using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

var page = new DiagramPage { Width = 30_000, Height = 40_000 };
for (var i = 0; i < 20_000; i++)
    page.Shapes.Add(new Shape { Id = "s" + i, X = i % 100 * 240 + 32, Y = i / 100 * 160 + 32, Width = 96, Height = 56, Text = "" });
for (var i = 0; i < 200; i++)
    page.Connectors.Add(new Connector { SourceId = "s" + i * 100, TargetId = "s" + (i * 100 + 1), SourcePort = PortSide.East, TargetPort = PortSide.West });
var router = new OrthogonalRouter();
int Batch()
{
    var scene = RoutingScene.Capture(page);
    var count = 0;
    foreach (var edge in page.Connectors)
    {
        var route = router.RouteSnapshot(scene, edge);
        if (!route.IsObstacleFree) throw new InvalidOperationException("Benchmark fixture has a blocked route.");
        count += route.Points.Count;
    }
    return count;
}
Batch();
var measurements = new List<object>();
for (var i = 0; i < 5; i++)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    var vertices = Batch();
    var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    measurements.Add(new { milliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, vertices });
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    shapeCount = page.Shapes.Count, connectorCount = page.Connectors.Count, measurements,
    note = "Includes one complete route batch. Deterministic sparse fixture; excludes rendering, ShapeSheet, transactions and input. Informational, not a timing gate." }, new JsonSerializerOptions { WriteIndented = true }));
