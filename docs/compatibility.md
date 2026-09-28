# Compatibility and parity boundary

DrawingSpace 0.2.0-alpha.1 is an independent Uno/Skia diagram editor. This ledger describes the source; the deployed site records its exact build in `build-info.json`. A feature with an implementation is not automatically a complete reproduction of Microsoft Visio.

| Area | Implemented | Remaining boundary |
| --- | --- | --- |
| Workspace | Visio-style ribbon, stencils, rulers, page tabs, task panes; Developer feature panes | Not pixel-identical; full Backstage/QAT/ribbon customization, floating panes and multi-window document UX absent |
| Controls | Custom buttons, ribbon, vector icons, gallery, palette, page strip and canvas | TextBox, ComboBox, Slider, ScrollViewer and ContentDialog still use Uno primitives |
| Files | Managed VSDX/VSSX/VSTX read/write, VDX read, binary file pickers, OPC validation, compound fill-rule/closed-subpath handling, preservation/diagnostics | Binary VSD unsupported; no independent Microsoft Visio certification; unsupported geometry/formulas may be preserved but not interpreted; no arbitrary lossless Office round-trip claim |
| ShapeSheet | Dimensional units, parser/evaluator, lazy conditions, references, cycle/budget diagnostics, GUARD/SETATREF subset, task-pane editing, transactional recalculation | Not the entire ShapeSheet function language, events, themes, action cells or every geometry section |
| Masters | Libraries, identity-safe import, multi-shape insertion with internal connectors/groups, per-instance Sheet references, connected-selection capture, component explorer editing, duplicate/rename/delete/detach, local overrides | Full nested-master semantics, separate free-form master canvas and live structural add/remove propagation remain incomplete |
| Editing | Selection, move/resize/rotate, shared eight-handle multi-selection resize and rotation, snapshot-based affine transforms, descendant-aware transactions, grouping, identity-safe graph clipboard/ungroup, custom geometry rendering/hit tests | Exhaustive imported/constrained-transform edge cases, arbitrary point editor and full object accessibility remain incomplete |
| Shape operations | Filled-area Union, Intersect, Subtract and XOR Combine; explicit primary operand; independent path copies; undo and retained connector glue | Conservative independent-shape replacement; no Fragment/Join/Trim/Offset; normalized Combine geometry rather than per-source Geometry sections; selection/order conventions differ; see [operation contracts](shape-operations.md) |
| Containers | Acyclic semantic membership, transitive transform closure, fit, membership lock, lane layout and task-pane tools | Full cross-functional flowchart rules and arbitrary affine lane layout are not reproduced |
| Connectors | Custom cardinal/normalized ports, direction flags, endpoint reattachment/detachment, waypoint insertion/move/removal, normal-constrained segment dragging with endpoint doglegs, label drag, arc/gap/square jumps | No guarantees for all congested routes; no junction topology or full line-jump priority parity; crossing analysis bounded |
| Text | HarfBuzz shaping, Unicode bidi/line breaks, text spans/paragraphs, range formatting UI, caret hit-testing and outlined SVG shape text | No full Visio text-control UX/IME certification, fonts for every script, complex connector-label parity, complete SVG text decoration fidelity or font upload |
| Export | Background-aware SVG/PNG, foreground-page PDF with backgrounds, embedded raster images, affine paths, connector jumps and text outlines | No tiled print preview/dialog parity, tagged PDF, editable text in outlined SVG or Office fidelity guarantee; compound nonzero native paths emit interchange diagnostics |
| Comments | Local threads, replies, resolve/reopen, undo and JSON persistence | No authenticated collaboration, transport, cloud storage, permissions or multiplayer conflict resolution |
| Data | Shape-data values/CSV and basic structural validation | No live database/Excel links, full data graphics, BPMN or engineering certification |
| Automation | Reusable .NET model/engine/editing API, read-only opt-in browser diagnostics | No Visio COM/VBA/VSTO compatibility, Office add-ins, plugin marketplace or scripting host |
| Platforms | Actual Uno WebAssembly and native desktop projects | No mobile-native targets, signing/installers or exhaustive GPU/device/accessibility certification |
| Performance | Shared immutable route scenes, spatial obstacle/hit/culling and ordinal-prefix crossing indexes, allocation-free affine bounds, changed-resource master refresh, indexed formula lookup and dirty-state caching | No million-shape or constant-time update claims; full-document history and whole-page route invalidation remain; see [routing benchmarks](performance.md) and [crossing benchmarks](shape-operations.md#reproduce-validation) |

## Input and resource limits

Binary picker input is capped at 32 MiB. The Visio parser applies its stricter package/expanded-part budgets. Native JSON uses the existing 32 MiB text limit, 256 pages, 4096 masters, 256 layers per page and 50,000 total objects. Explicit waypoint lists are limited to 4096 points; segment dragging accepts at most 4094 route vertices to reserve space for endpoint doglegs. Parsing and formula evaluation have independent recursion/operation budgets. These are safeguards, not performance guarantees.

Boolean operations accept at most 128 operands and 32,768 editable geometry segments, with a 131,072-point native intermediate path budget. Crossing analysis defaults to 262,144 stored route segments and 200,000 exact comparisons. Dense diagrams can exhaust these budgets and report incomplete analysis; the comparison counter excludes index and candidate-sort work.

PNG export is capped at 16,384 pixels per dimension and 64 million pixels total. Raster decoding is limited to 16 million pixels per image; the decoded image cache has a 64 MiB budget and a 32-image count cap. Runtime allocations outside that cache, including transient encoded/decoded buffers, are additional.

## Verification

Use the exact commit's engine, native compilation and browser acceptance reports. Unit tests include structured package fixtures, same-instance formula references, bundle identities, undo, ports, routes, affine selection snapshots, segment gestures, Unicode layout, Boolean areas and VSDX holes, original-ordinal spatial queries, exhaustive crossing equivalence and export contracts. Browser tests drive real pointer/keyboard/file-picker actions, including shared selection grips, segment dragging and Boolean editing. No simulated screenshot or diagnostic mutation substitutes for user input. Microsoft Visio interoperability, physical-device behavior and screenshot parity must not be inferred merely from successful compilation.

For interaction details and embedding examples, see [Selection transforms and connector editing](advanced-editing.md) and [Shape operations](shape-operations.md).
