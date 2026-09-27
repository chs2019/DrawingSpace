# Selection transforms and connector segment editing

These operations use the existing Uno drawing surface, Skia adorners and transactional editing engine. No document mutation is delegated to browser test diagnostics.

## Resize and rotate a selection

Select two or more shapes, or select a group. Eight shared handles surround the selection instead of displaying an independently actionable resize handle on every member. Drag a side handle to change one selection dimension. Corner handles preserve the selection's proportions by default; Shift also constrains side handles. Drag the rotation handle above the selection to rotate the whole selection; hold Shift to snap to 15-degree angles.

An imported group's explicit affine anchor supplies its oriented selection frame when the full root group is selected. Other multi-selections use a world-axis-aligned bounding frame. The gesture applies an affine matrix to the captured geometry: rotations, reflections and shear are retained. Transforming a shape also includes its transitive semantic-container and imported formula-frame descendants. Internal connectors and explicitly selected connectors participate in the same transaction.

Escape, loss of pointer capture, a page/document replacement, an input error, or cancellation restores the pre-gesture document. Mouse/pen/touch release commits one history entry regardless of the number of pointer previews. A click without movement does not create a resize or rotation edit. The final release position is processed when an input backend coalesces the final motion.

Locked selected objects or descendants reject the transform rather than moving an unlocked subset of a semantic unit. Existing ShapeSheet GUARD and dependency rules still apply: a constrained shape can deliberately resist a direct manipulation. This is not a replacement for a complete constraint solver.

## Edit an orthogonal connector

Select one connector without selecting shapes. Its endpoint handles reconnect/detach; its square waypoint handles move existing control points; its label diamond moves the label.

Drag an orthogonal segment or its rectangular midpoint grip to move that segment perpendicular to its direction. Interior segment vertices move together. Dragging an endpoint segment inserts doglegs while leaving the endpoint attached and retaining its exit direction. Grid snapping uses the segment's absolute coordinate, not the pointer's tangential movement. Alt temporarily bypasses snapping during an already active segment gesture.

Hold Alt **before pressing** to insert an individual waypoint instead of dragging the whole segment. Shift-click an existing waypoint to remove it. Straight/nonorthogonal polylines retain circular midpoint insertion grips. Undo restores the previous route in one step; Escape cancels an in-progress edit.

The segment editor produces waypoint constraints for the existing obstacle-aware router. It does not disable routing around obstacles, promise a clear route for an impossible/congested diagram, or implement connector junction topology. Endpoint glue IDs are unchanged by segment dragging.

## Reuse in a .NET host

The editing operations do not require Uno. A host-owned gesture can use the immutable geometry snapshot directly:

```csharp
using DrawingSpace.Core;
using DrawingSpace.Editing;

// Session selection has already been populated by the host.
var snapshot = session.CaptureSelectionTransform();
session.Begin("Resize selection");
try
{
    // Each preview is absolute relative to the captured selection, not incremental.
    var pivot = snapshot.Bounds.Center;
    var transform = MatrixD.Around(pivot, MatrixD.Scale(1.25, 1.10));
    snapshot.Apply(transform);
    session.Preview();
    session.Commit();
}
catch
{
    session.Cancel();
    throw;
}
```

For a single command, use `session.ResizeSelection(handle, start, current, preserveAspect)` or `session.RotateSelection(degrees, pivot)`. `SelectionTransformSnapshot.Capture(page, shapes, connectors)` is available separately for hosts with their own transaction layer.

For a routed connector, construct `DrawingSpace.Routing.ConnectorSegmentEditor(route.Points, zeroBasedSegmentIndex)`, call `Offset(pointerStart, pointerCurrent, gridSize)` and pass `CreateWaypoints(offset)` to the existing session waypoint API. Capture once at pointer press; never recapture from the previous preview.

## State and allocation contract

A selection snapshot stores transform scalars and connector geometry. It does not clone or replace embedded image bytes, custom paths, rich text, formula dictionaries, or master identities. A preview performs reference/lock/range checks for all captured targets before writing any geometry. A removed/replaced target makes the snapshot invalid; discard it when replacing the document or ending the gesture.

Shape dimensions are constrained to the document's supported range. Segment routes are capped at 4,094 input vertices so endpoint doglegs can still fit within the 4,096-waypoint budget. Non-finite, singular and out-of-range transforms are rejected. These limits do not establish a large-document performance guarantee.

## Regression coverage

`SelectionTransformSnapshotTests` checks affine/reflected geometry, repeated previews, exact identity restoration, transitive descendants, locks, atomic failure, resource/identity preservation, glue and connector offsets, undo/redo and cancel. `ConnectorSegmentEditorTests` covers interior/end segments, both orientations, snapping, fixed-route previews, input budgets and routing/undo integration. `PointFormattingTests` prevents recursive record formatting from overflowing the stack when geometry is logged.

`npm run test:browser:advanced` exercises the actual Uno controls with pointer and keyboard input in an isolated browser context. `npm run test:browser` runs both the existing workspace/interchange suite and this advanced suite, and fails if either suite fails. Consult the exact commit's CI reports for execution results; the presence of tests is not a passing result.

## Remaining boundary

This tranche does not complete imported-group ungroup/clipboard semantics, arbitrary multi-shape master authoring, geometry Boolean operations, connector junctions, all line-jump priority rules, a Visio-complete ShapeSheet interpreter, binary VSD, multiplayer service infrastructure, add-ins, or pixel-identical Microsoft Visio UI. See [Compatibility](compatibility.md).
