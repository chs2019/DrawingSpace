using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.Skia;

var fixtures = new List<object>();
foreach (var segments in new[] { 1, 128, 512 })
{
    var points = new PointD[segments + 1]; var jumps = new List<LineJump>();
    for (var i = 1; i <= segments; i++)
    {
        var delta = (i % 4) switch { 1 => new PointD(100, 0), 2 => new PointD(0, 100), 3 => new PointD(-100, 0), _ => new PointD(0, 100) };
        points[i] = points[i - 1] + delta;
        jumps.Add(new(i, points[i - 1] + delta * .7, 4));
        jumps.Add(new(i, points[i - 1] + delta * .25, 4));
    }
    var edge = new Connector { LineJumps = LineJumpStyle.Arc }; var route = new RouteResult(points, true);
    using (var expected = ReferenceConnectorPath.Create(edge, route, jumps))
    using (var actual = SceneRenderer.ConnectorPath(edge, route, jumps))
        if (expected.ToSvgPathData() != actual.ToSvgPathData()) throw new InvalidOperationException("Connector geometry differs from the retained reference.");
    var reference = Measure(() => { using var path = ReferenceConnectorPath.Create(edge, route, jumps); });
    var optimized = Measure(() => { using var path = SceneRenderer.ConnectorPath(edge, route, jumps); });
    fixtures.Add(new { segments, jumps = jumps.Count, reference, optimized });
}
var source = CsvDataTable.FromRows("bench", "Id", new[] { "Id", "Owner", "Value" },
    Enumerable.Range(0, 10000).Select(i => new[] { i.ToString("D6"), i % 2 == 0 ? "Alice" : "Bob", (10000 - i).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
var view = TabularDataView.Create(source, "Alice", "Value", numeric: true);
if (view.RowOrdinals.Count != 5000 || !ReferenceEquals(view[0], source.Rows[9998])) throw new InvalidOperationException("Tabular view changed source order or identity.");
var sourceView = Measure(() => GC.KeepAlive(TabularDataView.Create(source, "Alice", "Value", numeric: true)), 5);
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    methodology = "7 warmed samples, median milliseconds and current-thread allocated bytes per operation. Connector path construction only, including pooled preparation and native path commands; excludes routing, rasterization and UI. Exact SVG path equivalence is required first. Source view shares source row objects and measures filtering/sorting only.",
    connectorPaths = fixtures, sourceRows = source.Rows.Count, sourceView
}, new JsonSerializerOptions { WriteIndented = true }));

static Measurement Measure(Action action, int repetitions = 20)
{
    for (var i = 0; i < 10; i++) action();
    var samples = new double[7]; var allocations = new double[7];
    for (var sample = 0; sample < samples.Length; sample++)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < repetitions; i++) action();
        samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds / repetitions;
        allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)repetitions;
    }
    var sorted = samples.Order().ToArray(); var sortedAllocations = allocations.Order().ToArray();
    return new(sorted[3], sortedAllocations[3], samples, allocations);
}
internal sealed record Measurement(double MedianMilliseconds, double MedianAllocatedBytes, double[] MillisecondSamples, double[] AllocationSamples);
