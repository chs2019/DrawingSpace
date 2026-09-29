# Linked data and data graphics

DrawingSpace provides an ordinal-keyed CSV/TSV source snapshot, safe refresh plans,
four data-graphic families, and a bounded external-data preview. These are reusable
.NET APIs and real Uno controls. They are not Visio COM DataRecordsets or a full
replacement for Visio's database providers and graphic styles.

## Import and link

Open **Data → Open CSV** to select a UTF-8 CSV or TSV file, or **Data → Link Data**
to paste a source. Choose a stable source identity and unique key column. Set the
matching field to `$text`, `$name`, `$id`, or an existing shape-data property.
Choose comma, semicolon or tab explicitly; TSV files default to tab.

**Preview Refresh** parses the source without changing the drawing. The preview
grid realizes at most five rows and three columns at a time, with row and column
navigation. **Apply Refresh** performs one undoable transaction. Multiple shapes
can link to one row. A displayed `0001` key remains a string, not the number 1.
Row reordering and later shape-label changes do not change an established link.

Duplicate/empty source keys, malformed quoted fields, inconsistent row widths,
invalid UTF-8 files and resource-limit violations reject the source. CSV formulas
are data strings and are never executed. Existing CSV export protection remains
in place for spreadsheet formula prefixes.

## Three-way refresh and conflict resolution

For each field, B is the last accepted imported value, L is current local state,
and R is the new source value.

- L = B: import R.
- L = R: accept the shared value and update the baseline.
- R = B: retain the local edit, including a locally deleted property.
- Otherwise: report a conflict and retain L and the prior baseline.

**Overwrite local conflicts** is explicit and applies only when a fresh preview is
created. Removed source rows never delete shapes. Removed columns retain their
values/baselines so a later reintroduced column can still be reconciled. Locked
shapes/layers are reported rather than edited. Links to other source identities
are ignored. **Unlink Data** removes the link, not the displayed data values.
Local or inherited ShapeSheet `Prop` cells retain ownership of their fields:
refresh reports those fields and never silently overwrites their cells, even
with local-conflict overwrite enabled. Ordinary linked data can drive formulas
such as `Width = (Prop.Progress / 100) * 2 in`; recalculation and undo include both.

Plans are session/document/page/revision-bound and validate their targets before
any write. Selected-only plans also retain their selection. Unnotified changes
to matched targets' data, labels, links or locks reject application. Model hosts
must still notify normal document edits; arbitrary unnotified changes to
unmatched objects are outside the transaction contract.

Native JSON retains source identity, row key, baseline and graphic rules. Source
tables and credentials are not embedded; choose/paste the source again to refresh
after reopening. These are additive alpha-format fields: old DrawingSpace builds
will not retain them when resaving.

## Four graphic families

**Color by Value** overrides the evaluated fill while preserving `Shape.Style.Fill`.
**Data Bars** draw a bounded track, numeric fill and label.
**Icon Sets** use original vector red-cross, amber-warning and green-check badges.
**Text Callouts** draw shaped text labels containing literal field values.

The numeric families use minimum/maximum and three equal bands. **Lower is better**
reverses the color/state assessment without reversing the bar's numeric length.
Out-of-range values clamp; missing and nonnumeric fields suppress numeric graphics.
The reusable rule exposes colors, label, font size, and normalized bounds.

The Data ribbon replaces only the chosen graphic family; other families remain.
Removing graphics restores the base appearance and supports undo. Graphics follow
the owner's complete affine transform. Expanded bounds prevent viewport culling
from hiding visible graphics when the owner's base outline is offscreen.
Graphics are presentation, not separately selectable document shapes.

Skia, SVG, PNG and PDF use the same projected vector/text geometry. VSDX exports
shape-data values and evaluated fill, **not** source links or bar/icon/text overlays;
an explicit compatibility diagnostic is emitted. Use native JSON to preserve rules.
There is no claim of Visio DataRecordset/data-graphic interchange.

## Reusable APIs

```csharp
var source = CsvDataTable.Parse(csvText, "Assets", "Id");
var plan = session.PreviewDataRefresh(source, matchField: "$text");
foreach (var issue in plan.Issues)
    Console.WriteLine($"{issue.ShapeId}: {issue.Field}: {issue.Reason}");
session.ApplyDataRefresh(plan);

session.SetSelectedDataGraphic(new ShapeDataGraphic
{
    Kind = DataGraphicKind.DataBar,
    Field = "Progress",
    Minimum = 0,
    Maximum = 100,
    Bounds = new RectD(0, 1.08, 1, .3)
});
```

`DataGraphicProjection` produces ordinary local-space shapes independently of Uno
and Skia. `DataPreviewGrid` is a standalone reusable Uno control.
`ITabularWorkspaceStorage` is optional, preserving existing storage implementations.

## Budgets and performance

Picker input: 4 MiB UTF-8 bytes. Parser: 4,194,304 UTF-16 code units, 10,000 rows,
128 columns, 250,000 cells, 4,096 code units per cell, and 256-code-unit identifiers.
Refresh: 250,000 matched shape-field work units, charging existing local/baseline
cells as well as incoming values. Detailed issues are capped at 2,048 while
`TotalIssues` retains the count. At most eight graphics per shape are accepted.

The source is key-indexed. Cached graphic projections are bounded to 256 owners
and compare rule/data inputs exactly, excluding translation. Text layout uses a
bounded LRU keyed by shape identity with immutable, exact snapshots of all layout
inputs. It no longer serializes JSON, encodes UTF-8 or computes SHA256 on each
cache hit. Spans and paragraphs are compared by value; mutable models cannot
silently reuse a stale layout. Warm text-cache hits and cached connector hit tests
have zero-managed-allocation regression checks.

Full-document history and whole-page routing invalidation remain. These changes
do not establish million-shape throughput, constant-time updates, or a specific
frame rate.

## Validation and remaining scope

Run `dotnet test tests/DrawingSpace.Tests -c Release`, both Uno target builds, and
`npm run test:browser`. The data browser suite uses real pointer/keyboard/file
pickers, with read-only diagnostics. Performance CI runs
`dotnet run --project tools/DrawingSpace.DataBenchmarks -c Release`.

The benchmark compares retained JSON/UTF8/SHA256 key lookup with complete optimized
warm lookup on the same current model and reports raw samples/current-thread
allocation. It also checks 2,500 keyed matches and no-op repeat refresh. It is not
a whole-application benchmark or a historical binary comparison.

Remaining: XLSX/database/SharePoint providers, embedded source tables, authenticated
or scheduled refresh, a full sortable/filterable external-data grid with row
dragging, arbitrary categorical conditions, all Visio graphic presets/legends,
master rule propagation, data graphics in Visio package interchange, and exact UI
fidelity. Source links or graphics prevent destructive Boolean replacement;
independent path copies remain the non-destructive alternative.
