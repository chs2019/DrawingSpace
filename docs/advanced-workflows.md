# Advanced workbench workflows

These workflows describe the 0.2.0-alpha.1 feature branch, not the current main-branch Pages deployment. Every edit below uses the document's transaction history.

## Open and export Visio packages

Use File → Open to select VSDX, VSTX, VDX or native JSON. VSSX adds its masters to the current document. Canceling file selection leaves the drawing untouched. A replacement is parsed and validated before the save/discard prompt; a failed import does not discard current edits. Use Developer → Import Diagnostics to inspect approximated or unsupported constructs.

File → VSDX exports a drawing, VSTX exports a template, and VSSX exports the document master library. Original catalog stencils are not automatically materialized as document masters. No-edit preserved package round trips and supported structural edits are covered by engine fixtures, but Microsoft Visio has not independently certified the exported files.

## ShapeSheet

Select one unlocked shape and open Developer → ShapeSheet. Enter a cell name and expression, then Apply formula. For example, `Width = 3 in` or `Height = Width/2`. Cell names support the implemented User/Prop/reference forms. A protected GUARD expression requires the explicit Override GUARD command. Restore inherited cell removes the local cell and reveals its template value. Formula errors are reported without inventing a successful result.

## Masters

Create master from shape captures one selected shape. Choose a document master and Insert to create an instance; imported multi-shape masters keep internal connectors and group hierarchy. Master text/size/fill edits propagate to instances except where an instance owns a local override. Reset an override in the Masters pane to inherit again. Numeric `Sheet.n!` references resolve within the relevant master instance before page-wide lookup.

Full multi-shape master authoring, nested-master inheritance and every Visio master update rule remain outside this increment.

## Containers and swimlanes

Select shapes, open Developer → Containers, and choose Container around selection. Moving the container moves its transitive members once. Use Fit to contents, Add swimlane and the horizontal/vertical layout commands for lane organization. Membership can be reassigned or removed. Membership locks and cycle validation are enforced. Arbitrary rotated/sheared container layout is not equivalent to Visio cross-functional flowchart semantics.

## Rich text

Open Developer → Rich Text for one shape. Select a range in Rich text content, then apply bold, italic, underline, strike, size or color. Apply text commits typed text; formatting commands reject unapplied typing rather than applying stale offsets. Paragraph commands operate on the paragraph at the selected offset. The canvas uses HarfBuzz shaping, Unicode bidi processing and line breaking from the attributed RichTextKit implementation. Actual glyph availability still depends on the supplied fonts.

SVG shape text is exported as outlines with accessible labels, not as editable text. Native JSON and Visio packages retain text content. PDF/PNG use the same Skia shape renderer. Connector-label text has a simpler layout path; full text-decoration and complex-label SVG fidelity is not claimed.

## Connector manipulation

Select a connector. Drag either endpoint circle onto a shape/cardinal port/custom connection point, or to empty canvas to detach it. Custom ports respect their incoming/outgoing flags. Alt-drag a segment to insert a waypoint, drag a square to move it, and Shift-click a square to remove it. Drag a label diamond to offset its label. Developer → Connections changes jump style/size and label position, or clears explicit waypoints.

Crossing ownership follows drawing order. Arc, gap and square bridges do not appear at shared endpoints or collinear overlaps. Crossing work has an explicit comparison budget; routing still reports obstacle fallbacks rather than guaranteeing congested-diagram routing parity.

## Local discussions

Review → New comment starts a local discussion on a shape. Reply to discussion and Resolve/Reopen discussion are undoable. Threads and timestamps are persisted in native JSON. They are not an authentication system, cloud conversation or multiplayer transport.

## Embedding

`DiagramSurface` remains usable without `DiagramWorkbench`. `EditorSession`, `MasterService`, `DrawingFileCodec`, `VisioReader/Writer`, `ConnectionEndpoints`, `LineJumpService` and `RichTextLayoutEngine` are usable without Uno UI. Storage hosts can retain `IWorkspaceStorage` or additionally implement `IBinaryWorkspaceStorage`. Borrowed rich-text layouts belong to their engine cache and must not be retained past cache eviction or clear/disposal.

Selected connectors expose hollow midpoint grips for inserting waypoints by pointer, pen, or touch. Existing waypoint squares remain draggable; Shift-click removes one. Alt-dragging a segment is the keyboard alternative.
