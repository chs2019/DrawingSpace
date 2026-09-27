# Compatibility and parity boundary

DrawingSpace 0.2.0-alpha.1 is an independent Uno/Skia diagram editor. This ledger is for the feature branch. A feature with an implementation is not automatically a complete reproduction of Microsoft Visio.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workspace | Visio-style ribbon, stencils, rulers, page tabs, task panes; Developer feature panes | Not pixel-identical; full Backstage/QAT/ribbon customization, floating panes and multi-window document UX absent |
| Controls | Custom buttons, ribbon, vector icons, gallery, palette, page strip and canvas | TextBox, ComboBox, Slider, ScrollViewer and ContentDialog still use Uno primitives |
| Files | Managed VSDX/VSSX/VSTX read/write, VDX read, binary file pickers, OPC validation, preservation/diagnostics | Binary VSD unsupported; no independent Microsoft Visio certification; unsupported geometry/formulas may be preserved but not interpreted; no arbitrary lossless Office round-trip claim |
| ShapeSheet | Dimensional units, parser/evaluator, lazy conditions, references, cycle/budget diagnostics, GUARD/SETATREF subset, task-pane editing, transactional recalculation | Not the entire ShapeSheet function language, events, themes, action cells or every geometry section |
| Masters | Libraries, identity-safe import, multi-shape insertion with internal connectors/groups, per-instance Sheet references, editable root text/size/fill, local overrides | Full nested-master semantics, arbitrary multi-shape master authoring and all propagation rules remain incomplete |
| Editing | Selection, move/resize/rotate, alignment, grouping, nested affine model, custom geometry rendering/hit tests | Imported affine group resize/ungroup/clipboard edge cases, arbitrary point editor, Boolean tools and full object accessibility remain incomplete |
| Containers | Acyclic semantic membership, transitive move closure, fit, membership lock, lane layout and task-pane tools | Full cross-functional flowchart rules and arbitrary affine lane layout are not reproduced |
| Connectors | Custom cardinal/normalized ports, direction flags, endpoint reattachment/detachment, waypoint insertion/move/removal, label drag, arc/gap/square jumps | No guarantees for all congested routes; no full segment-drag/junction/line-jump priority parity; crossing analysis bounded |
| Text | HarfBuzz shaping, Unicode bidi/line breaks, text spans/paragraphs, range formatting UI, caret hit-testing and outlined SVG shape text | No full Visio text-control UX/IME certification, fonts for every script, complex connector-label parity, complete SVG text decoration fidelity or font upload |
| Export | Background-aware SVG/PNG, foreground-page PDF with backgrounds, embedded raster images, affine paths, connector jumps and text outlines | No tiled print preview/dialog parity, tagged PDF, editable text in outlined SVG or Office fidelity guarantee |
| Comments | Local threads, replies, resolve/reopen, undo and JSON persistence | No authenticated collaboration, transport, cloud storage, permissions or multiplayer conflict resolution |
| Data | Shape-data values/CSV and basic structural validation | No live database/Excel links, full data graphics, BPMN or engineering certification |
| Automation | Reusable .NET model/engine/editing API, read-only opt-in browser diagnostics | No Visio COM/VBA/VSTO compatibility, Office add-ins, plugin marketplace or scripting host |
| Platforms | Actual Uno WebAssembly and native desktop projects | No mobile-native targets, signing/installers or exhaustive GPU/device/accessibility certification |
| Performance | Route/text caches, viewport culling, bounded image decode/cache, input/geometry/history safeguards | No million-shape or constant-time update claims; full-document transactions and route recalculation still need optimization |

## Input and resource limits

Binary picker input is capped at 32 MiB. The Visio parser applies its stricter package/expanded-part budgets. Native JSON uses the existing 32 MiB text limit, 256 pages, 4096 masters, 256 layers per page and 50,000 total objects. Explicit waypoint lists are limited to 4096 points; parsing and formula evaluation have independent recursion/operation budgets. These are safeguards, not performance guarantees.

PNG export is capped at 16,384 pixels per dimension and 64 million pixels total. Raster decoding is limited to 16 million pixels per image; the decoded image cache has a 64 MiB budget and a 32-image count cap. Runtime allocations outside that cache, including transient encoded/decoded buffers, are additional.

## Verification

Use the exact commit's engine, native compilation and browser acceptance reports. Unit tests include structured package fixtures, same-instance formula references, bundle identities, undo, ports, routes, Unicode layout and export contracts. Browser tests drive real pointer/keyboard/file-picker actions. No simulated screenshot or diagnostic mutation substitutes for user input. Microsoft Visio interoperability, physical-device behavior and screenshot parity must not be inferred merely from successful compilation.
