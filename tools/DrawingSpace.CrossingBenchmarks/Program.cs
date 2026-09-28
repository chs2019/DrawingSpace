using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

// Both implementations receive exactly the same immutable fixture. Geometry equivalence
// is a hard check; timings are information, never hardware-dependent pass/fail gates.
var reports = new List<object>();
foreach (var fixture in new[] { Sparse(4000), Dense(320), Folded(4096) })
{
    LineJumpResult Baseline() => Exhaustive(fixture.Edges, fixture.Routes);
    LineJumpResult Indexed() => LineJumpService.Analyze(fixture.Edges, fixture.Routes, int.MaxValue);
    var expected = Baseline(); var actual = Indexed();
    if (actual.BudgetExceeded || fixture.Edges.Any(e => !expected.Jumps[e.Id].SequenceEqual(actual.Jumps[e.Id])))
        throw new InvalidOperationException("Crossing output differs for fixture " + fixture.Name);
    for (var warmup = 0; warmup < 2; warmup++) { Baseline(); Indexed(); }
    var samples = new List<Measurement>();
    for (var iteration = 0; iteration < 5; iteration++)
    {
        // Alternate measurement order so one implementation does not always run first.
        if (iteration % 2 == 0) { samples.Add(Measure("exhaustive", Baseline)); samples.Add(Measure("indexed-prefix", Indexed)); }
        else { samples.Add(Measure("indexed-prefix", Indexed)); samples.Add(Measure("exhaustive", Baseline)); }
    }
    reports.Add(new { fixture.Name, Connectors = fixture.Edges.Length, Segments = fixture.Routes.Values.Sum(r => Math.Max(0, r.Points.Count - 1)), Samples = samples });
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    Runtime = RuntimeInformation.FrameworkDescription,
    OS = RuntimeInformation.OSDescription,
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    Commit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
    Fixtures = reports,
    Note = "Same-process warmed comparison with the retained unbounded exhaustive reference. Includes index construction and crossing analysis; excludes routing, rendering, input, ShapeSheet and history. Current-thread allocation, not peak process memory. Informational timings; exact output equality is checked before measurement."
}, new JsonSerializerOptions { WriteIndented = true }));

static Measurement Measure(string implementation, Func<LineJumpResult> analyze)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
    var result = analyze();
    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    return new(implementation, elapsed, allocated, result.Comparisons, result.SpatialQueries,
        result.Candidates, result.IndexedSegments, result.Jumps.Values.Sum(j => j.Count));
}

static Fixture Sparse(int count)
{
    var edges = Enumerable.Range(0, count).Select(i => new Connector { Id = "s" + i, LineJumps = LineJumpStyle.Arc }).ToArray();
    var routes = edges.Select((e, i) => (e.Id, Route: new RouteResult(new PointD[] { new(i * 20, 0), new(i * 20 + 10, 10) }, true)))
        .ToDictionary(p => p.Id, p => p.Route);
    return new("separated-segments", edges, routes);
}

static Fixture Dense(int count)
{
    var edges = Enumerable.Range(0, count).Select(i => new Connector { Id = "d" + i, LineJumps = LineJumpStyle.Arc, JumpSize = 2 }).ToArray();
    var routes = new Dictionary<string, RouteResult>();
    for (var i = 0; i < count; i++)
    {
        var position = 4d * (i / 2 + 1); var extent = 4d * (count / 2 + 1);
        routes[edges[i].Id] = i % 2 == 0
            ? new(new PointD[] { new(0, position), new(extent, position) }, true)
            : new(new PointD[] { new(position, 0), new(position, extent) }, true);
    }
    return new("crossing-grid", edges, routes);
}

static Fixture Folded(int count)
{
    var edge = new Connector { Id = "folded", LineJumps = LineJumpStyle.Arc };
    var points = Enumerable.Range(0, count + 1).Select(i => i % 2 == 0 ? new PointD(0, 0) : new PointD(100, 100)).ToArray();
    return new("self-overlap-without-earlier-owner", new[] { edge }, new() { [edge.Id] = new(points, true) });
}

static LineJumpResult Exhaustive(IReadOnlyList<Connector> edges, IReadOnlyDictionary<string, RouteResult> routes)
{
    var result = new Dictionary<string, IReadOnlyList<LineJump>>(StringComparer.Ordinal); var comparisons = 0;
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
                    comparisons++;
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
    return new(result, false) { Comparisons = comparisons };
}

internal sealed record Fixture(string Name, Connector[] Edges, Dictionary<string, RouteResult> Routes);
internal sealed record Measurement(string Implementation, double Milliseconds, long AllocatedBytes,
    int Comparisons, int SpatialQueries, long Candidates, int IndexedSegments, int Jumps);
