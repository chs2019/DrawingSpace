# Linked data and data graphics

DrawingSpace provides ordinal-keyed CSV/TSV and XLSX worksheet source snapshots,
safe refresh plans, four data-graphic families, and a bounded sortable/filterable
external-data preview. These are reusable .NET APIs and real Uno controls, not
Visio COM DataRecordsets or a replacement for every Visio database provider.

## Import and link

Open **Data → Open CSV** for a UTF-8 CSV/TSV file, or **Data → Link Data** to paste
text. In the External Data pane, **Open Excel** imports an XLSX worksheet with
explicit worksheet/header-row selection; see [Excel sources](excel-data.md).
Choose a stable source identity and unique key column. Match unlinked shapes by
`$text`, `$name`, `$id`, or an existing shape-data property. Choose comma, semicolon
or tab explicitly for delimited text; TSV files default to tab.

**Preview Refresh** parses without editing the drawing. The grid realizes at most
five rows and three columns, with navigation, view-only filtering and stable
textual/numeric sorting. **Apply Refresh** performs one undoable transaction.
Multiple shapes can link to one row. A text key `0001` stays a string. Row reordering
and later label changes do not change an established link. Source view operations
never alter the source data or the pending refresh's full-source scope.

Duplicate/empty source keys, malformed quoting, inconsistent row widths, invalid
UTF-8 files and resource-limit violations reject CSV/TSV input. CSV formulas are
literal strings, never executed. Excel imports stored values and saved formula
caches, not formatted display values or recalculated results, with explicit
warnings. Existing CSV export protection for formula prefixes remains in place.

## Three-way refresh and conflict resolution

For each field, B is the last accepted import, L current local state, and R the new
source value. L = B imports R; L = R accepts that shared value and baseline; R = B
retains the local edit, including local deletion. Otherwise a conflict retains L
and its prior baseline. **Overwrite local conflicts** is explicit and requires a
fresh preview.

Missing source rows never delete shapes. Removed columns retain values/baselines
for later reconciliation. Locked shapes/layers are reported, not changed. Links
to other source identities are ignored. **Unlink Data** removes the link, not data.
Local/inherited ShapeSheet `Prop` cells retain field ownership: refresh never
silently overwrites them, even with conflict overwrite enabled. Ordinary linked
data can drive `Width = (Prop.Progress / 100) * 2 in`; undo includes recalculation.

Plans are session/document/page/revision-bound and preflight all targets before
writing. Selected-only plans retain their selection. Unnotified changes to matched
target data, labels, links or locks reject application. Hosts must still notify
normal model edits; arbitrary unnotified edits to unmatched objects are outside
the transaction contract.

Native JSON retains source identity, row key, baseline and graphic rules. Complete
source tables and credentials are not embedded; choose/paste the source again to
refresh after reopening. These are additive alpha-format fields; old builds do
not retain them when resaving.

## Four graphic families

**Color by Value** overrides the evaluated fill while retaining `Shape.Style.Fill`.
**Data Bars** draw a bounded track, numeric fill and label. **Icon Sets** use original
vector red-cross, amber-warning and green-check badges. **Text Callouts** draw
shaped text labels containing literal field values.

Numeric families use minimum/maximum and three equal bands. **Lower is better**
reverses color/state assessment without reversing bar length. Values clamp;
missing/nonnumeric data suppress numeric graphics. Reusable rules expose colors,
label, font size and normalized bounds. The ribbon replaces only the chosen family;
others remain. Removing rules restores the base appearance and supports undo.
Graphics follow the complete owner affine transform and expand culling bounds.
They are presentation, not separately selectable document shapes.

Skia/SVG/PNG/PDF share projected vector/text geometry. VSDX exports data values and
evaluated fill, not source links or bar/icon/text overlays; diagnostics state this.
Native JSON preserves rules. Visio DataRecordset/data-graphic interchange is not
claimed.

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

`DataGraphicProjection` produces local-space shapes independently of Uno/Skia.
`DataPreviewGrid` is a reusable Uno control, while `TabularDataView` is headless.
Optional `ITabularWorkspaceStorage` and `IExcelWorkspaceStorage` preserve existing
storage hosts. `CsvDataTable.FromRows` supports future providers without CSV
serialization overhead.

## Budgets and performance

CSV/TSV picker: 4 MiB UTF-8 bytes. Table/parser: 4,194,304 UTF-16 code units,
10,000 rows, 128 columns, 250,000 cells, 4,096 code units per cell and 256-code-unit
identifiers. XLSX adds explicit package/XML/scanning budgets listed in
[Excel sources](excel-data.md#bounds-and-package-handling).

Refresh charges 250,000 matched shape-field work units, including current/baseline
cells. Detailed issues cap at 2,048 while TotalIssues retains the count. Shapes
accept at most eight graphics. Source lookup is key-indexed. Views sort row
ordinals and share rows rather than copying dictionaries. Numeric sort values are
parsed once per query, not repeatedly inside the comparer.

Graphic projections cache at most 256 owners, compare exact rule/data inputs and
exclude translation. Text layouts use a bounded exact-input LRU without repeated
JSON/UTF8/SHA256 key construction; spans/paragraphs compare by value. Warm text
cache and cached connector-hit tests have zero-managed-allocation regressions.
Pooled connector path construction avoids per-segment LINQ sorting; see the
source/path benchmarks. Full-document history and whole-page routing invalidation
remain; no million-shape throughput, constant-time update or frame-rate claim is made.

## Validation and remaining scope

Run engine tests, both Uno targets and `npm run test:browser`. Browser tests use
real pointer/keyboard/file-picker input and read-only diagnostics. Performance CI
runs DataBenchmarks and SourceBenchmarks alongside routing/crossing benchmarks.
All measurements retain raw samples and distinguish component timings from the
application.

Remaining: database/SharePoint providers, formatted Excel values, Excel table/range
selection, embedded source tables, authenticated/scheduled refresh, full grid
virtualization and row dragging, categorical conditions, all Visio graphic
presets/legends, master-rule propagation, data-graphic package interchange and
exact UI fidelity. Source links/graphics prevent destructive Boolean replacement;
independent path copies are the non-destructive alternative.
