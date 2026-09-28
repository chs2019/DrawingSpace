# Shape operations and crossing performance

## Filled-vector operations

Select two to 128 independent filled vector shapes and open **Developer → Shape Operations**. Union keeps their combined filled area; Intersect keeps the common area; Subtract removes every later operand from the primary operand; Combine computes exclusive-or, leaving overlapping areas unfilled. **Primary Shape** chooses the subtraction base and source of text/formatting. The default is the backmost selected shape in the session's ordered shape list, not a guarantee of first-click order.

The result is an editable normalized compound path, not an image. Holes stay within the same geometry figure. Rotation, reflection and shear are applied before computing the world-space area. Text, style, data, hyperlinks and local discussions come from the primary shape. Rational conics are approximated by at most 32 quadratic segments per conic; they are not stored as exact rational curves.

Replacement retains the primary runtime and Visio identity. Incident connectors are retained and reattached through custom ports representing their prior resolved endpoint positions and directions. Ports may lie inside or outside the new outline: preserving a previous glue location is not boundary projection. Undo restores the full original graph, not just the visible outline. An empty result is deliberately non-destructive and does not create a history entry.

**Create Path Copy** makes a separate filled-outline copy, leaving the source and its live relationships unchanged. Use this before operating on a master instance or formula-driven shape. Stroke-only annotations, text boxes, images and group anchors are not filled-area operands. Unfilled details are excluded.

## Transaction and safety contract

`ShapeBooleanGeometry.Compute(orderedShapes, operation)` does not mutate the input or retain native resources in the result. `CreateArea` returns a caller-owned `SKPath`; dispose it. `CreatePathCopy` returns a native document shape.

`EditorSession.GetGeometryOperands(orderedIds)` validates independent operands. `ReplaceShapesWithGeometry` applies a precomputed vector result as one transaction, including connector reattachment. Live group/master/formula frames, direct ShapeSheet cells, locked objects/layers, locked container membership, different container memberships, and detected external shape-formula references reject replacement rather than silently discarding those dependencies. This is a conservative command, not arbitrary live-shape materialization.

Operands are capped at 128, editable segments at 32,768, and native intermediate path points at 131,072. Document coordinate and connection-point budgets still apply. Skia operations run synchronously; the input limits are not a strict wall-clock timeout.

## Visio interchange and fill rules

Compound Visio geometry uses alternate filling on import. Explicit DrawingSpace `FillRule` metadata preserves native nonzero-winding figures on a DrawingSpace round trip. Export emits `NonZeroFillRule` diagnostics for compound nonzero figures that may differ in consumers ignoring that extension. Normalize such figures with Create Path Copy for filled-outline interchange.

Tests explicitly export and reimport subtraction holes with DrawingSpace metadata both enabled and disabled. Additional regressions cover explicit native fill rules and closure of every completed subpath. These are codec tests, not independent certification against an installed Microsoft Visio application.

## Compatibility distinctions

The available operations reproduce filled-area editing, not every Visio Operations command. Fragment, Join, Trim, Offset, and a general path-control-point editor remain absent. Combine produces a normalized compound result rather than retaining a separate Visio Geometry section for each source operand. Successful replacement selects the result; empty results preserve the operands. Microsoft Visio documents different selection behavior and first-selected-shape inheritance. See Microsoft's [Subtract](https://learn.microsoft.com/en-us/office/vba/api/visio.selection.subtract), [Combine](https://learn.microsoft.com/en-us/office/vba/api/visio.selection.combine), and [Join](https://learn.microsoft.com/en-us/office/vba/api/visio.selection.join) contracts.

## Spatial crossing analysis

`LineJumpService.Analyze` bulk-builds a bounding-volume hierarchy over route segments. Queries use an exclusive original-ordinal prefix, so segments belonging to the same or later connectors never enter the candidate list. Original ordinals are sorted before exact intersection testing to retain painter-order ownership and overlapping-bridge suppression. Routes without an earlier owner issue no spatial queries. Disabled jump styles can still be crossed by later lines.

`LineJumpResult` exposes `Comparisons`, `IndexedSegments`, `SpatialQueries`, `Candidates`, and `BudgetExceeded`. Segment storage defaults to 262,144 entries, with a maximum supported limit of one million; exact comparisons default to 200,000. Dense diagrams can still exhaust the budget. Query/sort work is not included in the exact-comparison counter, and index construction is not allocation-free.

`SpatialBoundsIndex.Query(area, results, maximumOrdinalExclusive)` is reusable without Uno. The prefix refers to original input ordinals, not spatial-sort order. It appends to caller-owned storage and never clears or sorts existing entries. Rebuild the immutable index after geometry changes.

## Reproduce validation

```bash
dotnet test tests/DrawingSpace.Tests -c Release
dotnet run --project tools/DrawingSpace.CrossingBenchmarks -c Release
# With the production Uno browser build served at the documented URL:
node scripts/boolean-test.mjs
```

The crossing benchmark checks exact output equality with the retained exhaustive reference, warms both paths, alternates measurement order, and records five samples per implementation for separated segments, a crossing grid, and a folded self-overlapping route. It includes index construction and crossing analysis but excludes routing, rendering, input, ShapeSheet and history. Current-thread allocations are not peak memory. Raw results and the checked-out source commit are uploaded by the Performance workflow; timings are informational rather than machine-dependent CI gates.

The browser suite uses real pointer input and file pickers to test all four operations, holes in canvas hit-testing, undo/redo, path copies, empty-result history and VSDX import/export. It runs alongside the existing browser, advanced-editing and master-authoring suites on Build and on the public Pages deployment. See [compatibility](compatibility.md) for the application-wide boundary.
