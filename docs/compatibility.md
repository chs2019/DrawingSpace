# Compatibility and parity boundary

DrawingSpace 0.1.0-alpha.1 is a functional, independent diagram editor with a Visio-style desktop workspace. The table distinguishes implemented behavior from absent or limited behavior. A similarly named command is not evidence of complete Visio compatibility.

| Area | Implemented | Not implemented / limited |
| --- | --- | --- |
| Workspace | Blue title bar, ribbon tabs/groups, stencil pane, rulers, page tabs, status zoom, task panes | Not pixel-identical; no full Backstage view, ribbon customization, QAT customization, floating panes, or multi-window document UX |
| Controls | Custom buttons, ribbon, vector icons, gallery, palette, page strip, editor | TextBox, ComboBox, Slider, ScrollViewer and ContentDialog remain Uno controls, not complete custom replacements |
| Shapes | 43 original stencil masters; 29 geometry kinds; label/style/data/comments | No arbitrary user path editor, ShapeSheet, geometry formula language, proprietary stencil import, or complete built-in Visio libraries |
| Editing | Move, eight-handle resize, rotate, inline label edit, selection, undo/redo, clipboard, alignment, distribution, stacking | No full text rich formatting, nested groups, affine group transforms, shape replacement semantics, geometry Boolean operations, or object-level accessibility tree |
| Connectors | Glued shape IDs/cardinal ports, straight/orthogonal paths, arrows, labels, AutoConnect insertion | No complete Visio routing parity, line jumps, endpoint reattachment handles, user-editable routing waypoints, arbitrary connection points, or routing guarantees in congested diagrams |
| Containers | Container and lane-like geometry, ordering, labels | No semantic containment, automatic membership, moving/resizing children with containers, or cross-functional flowchart lane management |
| Organization | Original editable template and directed auto-layout | No HR import wizard, synchronized organization data, role-based expansion, or complete organization-chart rules |
| Documents | Named pages, layer flags, native JSON validation, local recovery | No VSD/VSDX/VSSX import/export, XML ShapeSheet, legacy formats, master-instance inheritance, background pages, or lossless Office round-trip |
| Text | Unicode strings, word/grapheme-aware wrapping, font size, color, bold/italic, four bundled fallback styles | No HarfBuzz shaping integration, full bidirectional/complex-script fidelity, rich text runs, paragraph formatting, custom font upload, or font embedding in SVG |
| Data | Editable key/value shape data, CSV export, local comments, basic validation | No database/Excel linking, live data graphics, external data refresh, BPMN validation, engineering-rule certification, or threaded collaboration |
| Export | SVG/PNG current page, PDF all pages, shape-data CSV | No print preview, tiled printing, export dialogs with complete resolution/options, accessible tagged PDF, or Office export fidelity guarantee |
| Platforms | Real Uno browser app plus Windows/macOS/Linux desktop host builds | No Android/iOS native targets; touch/pinch paths are implemented but not a physical-device certification; no native signing/installers |
| Collaboration | Local editing and recovery | No authentication, cloud synchronization, multiplayer, permissions, or sharing service |
| Automation | Reusable .NET editing API and read-only browser test diagnostics | No Visio COM/VBA, VSTO, Office add-ins, plugin marketplace, or scripting engine |
| Performance | Viewport culling, route/font caches, bounded history/export allocation | No claim of million-shape performance; large documents need indexing, incremental routes and delta history |

## File and model limits

Native JSON is limited to 32 MiB of text, 256 pages, 256 layers per page, and 50,000 objects per drawing. These are input safeguards, not performance guarantees. Page size is 24–100,000 drawing pixels; shape dimensions are 1–100,000 pixels. PNG export is bounded to 16,384 pixels per dimension and 64 million pixels total.

The router inspects a bounded set of nearby obstacles and reports fallbacks rather than silently claiming every route is clear. The diagram validator checks basic connection and page conditions only. It is not a substitute for a domain-specific design review.

## Fidelity priorities

The next major compatibility areas are a properly scoped VSDX package reader/writer with round-trip fixtures, a ShapeSheet-compatible formula model, semantic master instances and containers, rich text shaping, endpoint reattachment and line jumps, complete keyboard/accessibility coverage, and broader visual regression tests. These are separate engineering deliverables, not features already present in this alpha.
