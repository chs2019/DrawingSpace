using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

// Informational microbenchmarks; correctness/allocation budgets live in the test suite.
// No network, GUI, Skia native dependency, timing thresholds, or application data is used.
var results = new List<Measurement>();
var page = new DiagramPage();
for (var i = 0; i < 20_000; i++) page.Shapes.Add(new() { Id = "shape" + i, X = i % 200 * 4, Y = i / 200 * 4 });
var document = new DiagramDocument { Pages = [page] };
var session = new EditorSession(document);
session.Execute("Make dirty", () => document.Title = "Benchmark edited");
var sink = 0;
Measure("dirty-cached-20000-shapes", 10_000, () => { if (session.IsDirty) sink++; });
var snapshot = SelectionTransformSnapshot.Capture(page, page.Shapes.Take(4));
var step = 0;
Measure("transform-4-of-20000", 10_000, () => snapshot.Apply(MatrixD.Translation(++step % 100, 10)));
var scope = new ShapeSheetScope(document, page);
Measure("sheet-lookup-20000-shapes", 10_000, () => { if (scope.FindSheet(page.Shapes[0], "Sheet.shape19999") is not null) sink++; });

// Directly measures the previous getter's algorithm, not a synthetic constant-time baseline.
// Keep iterations bounded: it really serializes the complete 20,000-shape document each time.
var saved = DocumentCodec.Save(document);
Measure("dirty-previous-serialization-algorithm", 10, () => { if (DocumentCodec.Save(document) == saved) sink++; });
GC.KeepAlive(sink);
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    shapeCount = page.Shapes.Count,
    samples = 7,
    note = "Headless microbenchmarks exclude capture, transactions, ShapeSheet recalc, routing, rendering and browser input. Timings are informational.",
    results
}, new JsonSerializerOptions { WriteIndented = true }));

void Measure(string name, int iterations, Action action)
{
    for (var i = 0; i < Math.Min(iterations, 20); i++) action();
    var times = new double[7]; var allocations = new long[7];
    for (var sample = 0; sample < times.Length; sample++)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var timestamp = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) action();
        times[sample] = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds / iterations;
        allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations;
    }
    Array.Sort(times); Array.Sort(allocations);
    results.Add(new(name, iterations, times[3], allocations[3]));
}

internal sealed record Measurement(string Name, int IterationsPerSample, double MedianMillisecondsPerOperation, long MedianAllocatedBytesPerOperation);
