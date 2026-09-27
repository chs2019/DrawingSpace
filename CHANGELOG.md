# Changelog

## 0.2.0-alpha.1 — feature branch

- Integrated managed VSDX/VSTX opening and export, VSSX library import/export, and VDX opening with bounded native/browser binary file selection and import diagnostics.
- Added Developer task panes for ShapeSheet, document masters, semantic containers/swimlanes, selected-range rich text and connector settings.
- Preserved internal connectors and nested groups when instantiating masters; remapped imported library identities and scoped numeric Sheet references to each instance.
- Wired the HarfBuzz/Unicode rich-text engine into the Skia surface and vector shape export. Shape text is outlined in SVG; accessible labels are retained.
- Added affine custom geometry and raster-image rendering, bounded decoded-image caching, background-aware PNG/SVG/PDF export and foreground-only PDF page enumeration.
- Added endpoint reattachment/detachment, Alt-drag waypoint insertion, Shift-click waypoint deletion and connector label dragging. Added deterministic arc/gap/square line jumps.
- Added local discussion replies/resolution, undo and native JSON persistence. No multiplayer synchronization is implied.
- Fixed text-engine compilation and included it in the engine test graph. Added regression coverage for the new model, rendering and interaction contracts.
- Prevented marking a newer document revision as saved when a native save operation finishes late.

This is not a complete Visio implementation. See the compatibility boundary and the exact commit's CI reports. The public Pages site continues to reflect its verified main-branch build until this branch is merged and deployed.

## 0.1.0-alpha.1 — 2026-09-27

Initial independent Uno Platform and SkiaSharp diagramming implementation.

- Added nine reusable packages and browser/native application hosts.
- Added versioned diagram documents, pages, layers, shape styles/data/comments, and validated JSON persistence.
- Added original shape masters and editable flowchart, organization and network examples.
- Added transactional history, selection, transforms, clipboard, arrangement, automatic layout and basic validation.
- Added glued connectors, deterministic obstacle-aware orthogonal routing and AutoConnect insertion.
- Added custom ribbon, command controls, iconography, stencils, page strip and drawing surface.
- Added native JSON files, SVG/PNG/PDF and data CSV export, plus browser/native recovery.
- Added engine/renderer tests, pointer-driven browser acceptance tests, desktop compilation matrix, verified Pages deployment and release/package workflows.
- Corrected browser font fallback by loading explicit packaged static typefaces and using Uno's font manifest for UI controls.

See `docs/compatibility.md` for the current scope and omissions. Full Visio compatibility is not implied by this release.
