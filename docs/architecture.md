# Architecture

## Dependency direction

```text
Core → Documents → Routing → Editing
              ├── Stencils       │
              └── Skia ──────────┤
                     Controls → Editor → Workbench → App
```

The arrows indicate layering, not every individual project reference; the authoritative dependency list is in each project file. Document and editing code has no Uno dependency. The app owns platform entry points, file pickers, browser JavaScript interop, and recovery initialization. No browser JavaScript performs diagram layout or rendering.

## Mutation contract

`EditorSession` is the mutation boundary. An interaction starts with `Begin(name)`, mutates the live model while emitting `Preview()`, and finishes with `Commit()` or `Cancel()`. `Execute(name, action)` wraps discrete commands with rollback on failure. A drag is one undo entry, not one entry per pointer event. Validation occurs before a transaction becomes history. History is bounded by both entry count and approximate serialized memory.

Undo/redo replaces model instances. Controls must resolve shapes by stable ID after a document change rather than retaining object references indefinitely. Each history entry retains the active page and selection. Inserting new work after undo discards the redo branch. Page operations participate in the same history.

## Rendering and hit testing

`ShapeGeometry` creates the same original Skia paths for the editor, stencil thumbnails, hit tests, and SVG export. Shape-local geometry is rotated about the shape center. The renderer culls shapes outside the visible world rectangle; connector routes are cached by page identity and session revision. Transient selections, resize handles, marquee rectangles, port indicators, and alignment guides are drawn in screen space so their hit targets do not shrink with zoom.

The engine is not a retained million-object scene graph. Moving a shape invalidates page routes, and selection/hit tests use linear scans. The visibility router bounds its obstacle set. These choices are explicit alpha trade-offs, not large-diagram scalability claims.

## Connector routing

Attached endpoints are shape IDs plus cardinal ports, not copied endpoint coordinates. A route recomputes endpoints after movement or resizing. Automatic ports face the opposite endpoint; explicit ports retain their side. Orthogonal routing uses a rectilinear visibility grid and A* with distance and bend costs. A deterministic fallback is returned when obstacle avoidance cannot be established; `IsObstacleFree` is exposed to validation. Rotated endpoint escape segments can be non-orthogonal.

## Fonts

The host configures the UI with Uno's Open Sans font manifest and loads four static Open Sans faces into the Skia renderer. This avoids platform-dependent monospace fallbacks and preserves real bold/italic variants. Font installation is a host concern; `SceneRenderer.SetTypefaces` transfers ownership of the supplied typefaces to the renderer. The simple text layout is not a full rich-text or complex-script shaping engine.

## Persistence and services

`DocumentCodec` uses source-generated System.Text.Json metadata for browser trimming compatibility. It validates the native format before the current document is replaced. Exporters respect layer visibility and export inclusion. SVG is written with an XML writer; labels are not inserted as executable markup. PNG allocation is bounded. PDF uses SkiaSharp's document API and converts from 96 drawing pixels to 72 points per inch.

`IWorkspaceStorage` separates the reusable workbench from host storage. Web recovery is transactional IndexedDB; native recovery writes a temporary file before replacement. Save/open are explicit user operations. Browser clipboard failure falls back to an application-local copy buffer.

## Testing and deployment

Unit tests exercise headless geometry, documents, routing, history, and native Skia export. Browser tests observe read-only diagnostics and send actual pointer/keyboard events. CI publishes screenshots and reports. Pages consumes only a successful main-branch Build artifact and checks its recorded commit before deployment; public-site tests verify the deployed app independently of the local test server.


## Feature-branch extensions

The model now carries ShapeSheet cells, local overrides, master/template/instance identities, affine frames, nested group anchors, semantic containment, rich-text spans/paragraphs, custom ports and preserved Visio package metadata. `MasterBundle` makes insertion of shapes, connectors and groups one transaction. Formula scope resolves numeric master Sheet references within an instance before searching a page.

`DrawingSpace.Visio` implements bounded file detection and OPC interchange. `IBinaryWorkspaceStorage` adds byte-based file picking without changing the existing storage contract. `DrawingSpace.Text` owns a bounded layout LRU; `SceneRenderer` borrows layouts and owns font/image/render caches. Text-layout cache keys omit world translation but include text/style/paragraph/box state. Affine geometry and background composition are shared by the editor and exported pages.

The canvas captures an immutable gesture snapshot for each edited shape/connector and updates a document preview during pointer moves. Endpoint/waypoint/label changes commit once on release; cancellation restores the transaction snapshot. Browser diagnostics expose read-only model observations and control bounds, including popup contents, only with `?test=1`.
