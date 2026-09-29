using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Text;

// Retained old key algorithm on the same current model. This is not a whole-app benchmark.
static string LegacyKey(Shape shape) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ModelJson.Serialize(new Shape
{
    Text = shape.Text, Kind = shape.Kind, Width = shape.Width, Height = shape.Height, Style = shape.Style,
    TextSpans = shape.TextSpans, Paragraphs = shape.Paragraphs, TextBounds = shape.TextBounds, TextRotation = shape.TextRotation,
    Container = shape.Container, Id = "text-layout", Name = ""
}))));
static Measurement Measure(string name, Action action, int iterations)
{
    for (var i = 0; i < 30; i++) action();
    var samples = new List<Sample>();
    for (var sample = 0; sample < 7; sample++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) action();
        samples.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before));
    }
    return new(name, iterations, samples.OrderBy(s => s.Milliseconds).ElementAt(3).Milliseconds,
        samples.OrderBy(s => s.Bytes).ElementAt(3).Bytes, samples);
}
var shape = new Shape
{
    Text = "Progress: 90 — Owner: Alice", TextSpans = [new() { Start = 0, Length = 8, Bold = true }],
    Paragraphs = [new() { Start = 0, Alignment = ParagraphAlignment.Left }]
};
using var engine = new RichTextLayoutEngine();
var layout = engine.Layout(shape);
var legacyCache = new Dictionary<string, ShapeTextLayout> { [LegacyKey(shape)] = layout };
var legacy = Measure("Legacy JSON/UTF8/SHA256 cached lookup", () =>
{
    if (!ReferenceEquals(layout, legacyCache[LegacyKey(shape)])) throw new InvalidOperationException("Legacy lookup changed.");
}, 5000);
var optimized = Measure("Exact-input warm layout lookup", () =>
{
    if (!ReferenceEquals(layout, engine.Layout(shape))) throw new InvalidOperationException("Warm layout was not reused.");
}, 5000);
shape.Text += "!";
if (ReferenceEquals(layout, engine.Layout(shape))) throw new InvalidOperationException("Changed text was reused.");

var document = new DiagramDocument();
const int count = 2500;
var csv = new StringBuilder("Id,Progress,Owner\n");
for (var i = 0; i < count; i++)
{
    var key = i.ToString("D6");
    document.Pages[0].Shapes.Add(new() { Text = key, X = i % 50 * 160, Y = i / 50 * 100 });
    csv.Append(key).Append(",90,Alice\n");
}
var table = CsvDataTable.Parse(csv.ToString(), "Assets", "Id");
var session = new EditorSession(document);
var preview = Measure("Preview 2500 keyed data links", () =>
{
    if (session.PreviewDataRefresh(table).ChangedShapes != count) throw new InvalidOperationException("Missing keyed matches.");
}, 5);
session.ApplyDataRefresh(session.PreviewDataRefresh(table));
if (session.PreviewDataRefresh(table).ChangedShapes != 0) throw new InvalidOperationException("Repeat refresh is not a no-op.");
Console.WriteLine(JsonSerializer.Serialize(new
{
    Runtime = RuntimeInformation.FrameworkDescription, OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    Validation = "Reference reuse, exact invalidation, 2500 key matches and no-op repeat verified.",
    Measurements = new[] { legacy, optimized, preview }
}, new JsonSerializerOptions { WriteIndented = true }));
sealed record Sample(double Milliseconds, long Bytes);
sealed record Measurement(string Name, int Iterations, double MedianMilliseconds, long MedianBytes, List<Sample> Samples);
