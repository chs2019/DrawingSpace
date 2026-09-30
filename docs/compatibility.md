# Compatibility and parity boundary

DrawingSpace 0.2.0-alpha.1 is an independent Uno/Skia diagram editor. This ledger describes the source; the deployed site records its exact build in `build-info.json`. An implementation is not automatically a complete reproduction of Microsoft Visio.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workspace | Visio-style ribbon, stencils, rulers, page tabs, task panes; Developer feature panes | Not pixel-identical; full Backstage/QAT/ribbon customization, floating panes and multi-window document UX absent |
| Controls | Custom buttons, ribbon, vector icons, gallery, palette, page strip, source preview and canvas | TextBox, ComboBox, Slider, ScrollViewer and ContentDialog still use Uno primitives |
| Files | Managed VSDX/VSSX/VSTX read/write, VDX read, binary pickers, OPC validation, compound fill-rule/subpath handling, preservation/diagnostics | Binary VSD unsupported; no independent Microsoft Visio certification; unsupported geometry/formulas may be preserved but not interpreted; no arbitrary lossless Office round-trip claim |
| ShapeSheet | Dimensional units, parser/evaluator, lazy conditions, references, cycle/budget diagnostics, GUARD/SETATREF subset, task-pane editing, transactional recalculation | Not the entire ShapeSheet language, events, themes, action cells or every geometry section |
| Masters | Libraries, identity-safe import, multi-shape insertion with internal connectors/groups, per-instance Sheet references, connected-selection capture, component explorer editing, duplicate/rename/delete/detach, local overrides | Full nested-master semantics, separate master canvas and live structural add/remove propagation incomplete |
| Editing | Selection, move/resize/rotate, shared handles, snapshot-based affine transforms, descendant-aware transactions, grouping, identity-safe graph clipboard/ungroup, custom geometry rendering/hit tests | Exhaustive imported/constrained-transform edge cases, point editor and full object accessibility incomplete |
| Shape operations | Filled-area Union, Intersect, Subtract and XOR Combine; primary operand; independent path copies; undo and retained glue | Independent-shape replacement; no Fragment/Join/Trim/Offset; normalized Combine rather than per-source Geometry sections; selection/order conventions differ; see [operation contracts](shape-operations.md) |
| Containers | Acyclic semantic membership, transitive transforms, fit, membership lock, lane layout and tools | Full cross-functional flowchart rules and arbitrary affine lane layout not reproduced |
| Connectors | Custom ports/directions, reattachment, waypoints, segment dragging/doglegs, label drag, arc/gap/square jumps, pooled single-pass jump-path construction | No guarantee for all congested routes; no junction topology or full jump priority parity; crossing analysis bounded |
| Text | HarfBuzz shaping, Unicode bidi/line breaks, spans/paragraphs, range formatting UI, caret hit tests and outlined SVG shape text | No full text-control UX/IME certification, all-script fonts, complex connector labels, complete SVG decorations or font upload |
| Export | Background-aware SVG/PNG, foreground-page PDF with backgrounds, images, affine paths, connector jumps and text outlines | Graphic overlays included in SVG/PNG/PDF, not Visio packages. No tiled print/tagged PDF/editable outlined text or Office fidelity guarantee |
| Comments | Local threads, replies, resolve/reopen, undo and JSON persistence | No authenticated collaboration, transport, cloud storage, permissions or multiplayer conflict resolution |
| Data | Keyed CSV/TSV and read-only XLSX worksheets, real pickers, worksheet/header selection, three-way refresh, missing-row/conflict reports, unlink/undo, saved baselines, bounded filtering/stable numeric/text sort, four graphic families | No formatted Excel values/calculation/table-range selection, database/SharePoint provider, embedded source table, row dragging, categorical graphics/legends, master propagation or Visio recordsets; see [data](data-features.md) and [Excel](excel-data.md) |
| Automation | Reusable .NET model/engine/editing APIs, read-only opt-in browser diagnostics | No COM/VBA/VSTO compatibility, Office add-ins, marketplace or scripting host |
| Platforms | Actual Uno WebAssembly and native desktop projects | No mobile-native targets, signing/installers or exhaustive GPU/device/accessibility certification |
| Performance | Shared route scenes, spatial obstacle/hit/culling/prefix-crossing indexes, affine bounds, changed-resource master refresh, indexed formulas/dirty state, exact-input text LRU, bounded graphics, allocation-free cached connector hits, pooled jump ordering and shared-row source views | No million-shape or constant-time claims; full-document history and whole-page route invalidation remain; see [routing](performance.md), [crossing](shape-operations.md#reproduce-validation) and [source/path](excel-data.md#renderer-and-validation) benchmarks |

## Input and resource limits

Drawing picker input: 32 MiB. Visio applies stricter expanded-part limits. Native JSON retains its 32 MiB text limit, 256 pages, 4096 masters, 256 layers/page and 50,000 total objects. Waypoint lists: 4096 points; segment dragging accepts 4094 route vertices to reserve endpoint doglegs. Formula/parse recursion and work have separate bounds.

Boolean operations: 128 operands, 32,768 editable segments, 131,072 native intermediate points. Crossing analysis: 262,144 stored segments and 200,000 exact comparisons by default. Dense diagrams may report incomplete analysis. Connector path construction accepts at most 262,144 supplied jumps; it does not remove crossing-analysis budgets.

XLSX: 8 MiB compressed, 2048 entries, 16 MiB per expanded part, 64 MiB expanded/read budget, 1 MiB metadata XML; bounded selected-table scans. Worksheet values are stored/cached values, not Excel formatting or evaluated formulas. DTDs, path traversal, macros and external selected-part relationships are rejected. See the detailed [Excel contract](excel-data.md).

PNG: 16,384 pixels/dimension and 64 million pixels. Image decoding: 16 million pixels/image; decoded cache: 64 MiB and 32 images. Transient buffers and other runtime allocations are additional.

## Verification

Use the exact commit's engine, desktop and browser reports. Tests cover package fixtures, references, identities, undo, ports/routes, affine snapshots, gestures, Unicode, Boolean/VSDX holes, spatial/crossing equivalence and export contracts. XLSX tests cover strict/transitional namespaces, relationships, typed/sparse cells, cached formulas, package limits and malformed data. Connector rendering compares exact path commands with a retained reference and checks managed allocation reduction. Browser tests use actual pointer/keyboard/file-picker operations, including Excel selection, source sorting/filtering and keyed refresh.

Compilation is not independent Microsoft Visio certification, physical-device validation or screenshot parity. No diagnostic mutation or simulated screenshot substitutes for user input. See [advanced editing](advanced-editing.md), [shape operations](shape-operations.md) and [Excel sources](excel-data.md).
