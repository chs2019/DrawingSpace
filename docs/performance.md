# Performance: measured costs and mutation contracts

## Route batches

The renderer now captures one immutable `RoutingScene` per invalidated page route batch. The snapshot stores endpoint transforms/ports, layer visibility and obstacle bounds, not images or rich-text resources. A Morton-ordered bounding-volume hierarchy narrows each corridor query. A* considers the same bounded obstacle set and continues to report fallbacks; a clear aligned segment takes the optimal straight path without constructing the visibility grid. Visio export shares a routing snapshot across each page/master part as well.

```csharp
var router = new OrthogonalRouter();
var scene = RoutingScene.Capture(page);
foreach (var connector in page.Connectors)
{
    RouteResult route = router.RouteSnapshot(scene, connector);
    // Inspect IsObstacleFree; a fallback is not a route-quality guarantee.
}
```

A snapshot is immutable. Capture again after changing geometry, ports or layer visibility. The existing `Route(page, connector)` API remains available for isolated calls and builds its own snapshot. Sharing one snapshot is the important batch optimization; repeatedly calling the compatibility API does not achieve the measured batch behavior.

## Local benchmark evidence

Fixture: **20,000 shapes and 200 adjacent, cardinal-glued connectors** on a deterministic sparse grid. One warm-up and five measured batches on the same Debian 13 / .NET 10.0.12 environment. Each new batch includes snapshot/index construction. All outputs contain 400 vertices and pass the fixture's obstacle-free assertion.

| Metric | Before | After |
| --- | ---: | ---: |
| Median complete route batch | 1,683.2872 ms | 21.0722 ms |
| Median current-thread allocation per batch | 2,049,763,240 bytes | 6,836,608 bytes |

This is approximately **79.9× faster** and **99.67% less allocation for this fixture**, not a universal speedup or a browser frame-rate claim. The fixture particularly benefits from eliminating repeated page scans, allocation-free affine bounds and the clear-aligned-route fast path. Congested graphs, complex port directions and dense overlapping obstacles have different costs. These measurements exclude rendering, ShapeSheet recalculation, serialization, input and document transactions.

Raw results: [before](../benchmarks/results/routing-before.json), [after](../benchmarks/results/routing-after.json). The baseline was real code at `eb648913c3cf28cce247d9591f5c0bf825bf6427`, not a simulated slow implementation. To reproduce the baseline, run the same fixture there with `router.Route(page, edge)` inside the batch; the new code uses one `RoutingScene.Capture(page)` plus `RouteSnapshot` for every edge.

```bash
dotnet run --project tools/DrawingSpace.RoutingBenchmarks -c Release
# Existing transaction/reference/dirty-state workload:
dotnet run --project tools/DrawingSpace.Benchmarks -c Release
```

The Performance workflow publishes the routing JSON and exact source SHA. Timing is informational rather than a flaky hardware-dependent gate. Correctness and bounded-allocation regression tests remain part of the engine suite.

## Rendering and hit testing

`SpatialBoundsIndex` is reusable without Uno or Skia. It retains input ordinals, bulk-builds an immutable hierarchy and appends inclusive intersection matches to a caller-owned `List<int>`. The caller controls buffer reuse; query traversal itself does not allocate. Morton sorting is only a spatial ordering, never painter order.

For pages above 128 shapes the renderer caches a shape index by page, revision and count, retaining at most eight pages. Visible candidates are sorted back into source order, preserving container/connector/foreground painting. Pointer hit candidates use reverse painter order and exact path hit testing. Tests compare indexed hits against the uncached implementation and compare rendered output byte-for-byte for a clipped fixture. Affine rectangle transforms calculate four corners directly instead of allocating a corner array and LINQ enumerator.

Embedding hosts should pass their monotonically increasing scene revision:

```csharp
var hit = renderer.HitShape(page, worldPoint, tolerance: 2, revision: revision);
```

Changing geometry without changing the revision invalidates the cache contract. Omit the revision for an uncached hit, or call `ClearCache()` after external edits. The renderer itself remains UI-thread-confined; the standalone immutable index supports concurrent readers with separate result lists.

## Master and transaction costs

Master refresh indexes definitions once per pass and copies geometry, ports and rich text only when changed. Ordinary selection snapshots do not clone those resources. Imported graph clipboard operations preserve identities, formula contexts and layers; they still validate and serialize bounded document subsets.

The preceding clipboard/performance increment also indexes ShapeSheet scopes, shares only immutable bounded parsed expressions, caches dirty-state snapshots and reuses selection-projection buffers. It does not memoize mutable formula evaluation across revisions.

Transactions still use full-document history snapshots, and geometry changes still invalidate all page routes. The implementation is not an incremental million-object engine, constant-time editor, junction router or GPU-compute diagram model. These remaining costs are explicit rather than hidden behind the sparse routing measurement.
