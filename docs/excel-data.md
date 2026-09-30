# Excel worksheet data sources

DrawingSpace reads ordinary, unencrypted `.xlsx` workbooks directly in the browser
and desktop. The managed reader uses .NET ZIP/XML APIs and adds no Excel, COM,
Office automation, database-driver or third-party spreadsheet dependency.

## Workflow

1. Open **Data → Link Data → Open Excel** and select the workbook.
2. Choose **Excel worksheet** and **Excel header row**. The selected row contains
   unique column names; data occupies the same contiguous columns below it.
3. Choose the **Key column**, **Source identity**, and shape **Match field**.
4. Select **Preview Refresh**, inspect the source and warnings, then **Apply Refresh**.

The default source identity is the workbook filename without its extension plus
worksheet name. Choose the same explicit source identity when a file is renamed.
Choosing another worksheet restores the default identity for that sheet so two
unrelated worksheets are not accidentally treated as one source. Source identity
is editable; keys are exact ordinal strings, never row numbers.

The reader follows OPC workbook and worksheet relationships; it does not assume
`sheet1.xml` or depend on the order of ZIP entries. Both transitional and strict
SpreadsheetML namespaces are accepted. Hidden worksheets are marked in the list;
choosing one is explicit and produces a warning. Hidden data rows are included.

The existing three-way refresh engine handles these sources, including local
conflicts, missing rows, protected/ShapeSheet-owned fields, stale preview rejection,
undo/redo, and persisted accepted baselines. A shape may be renamed and rows may be
reordered without changing an established source link. A view filter/sort changes
only source presentation, not which rows participate in refresh.

## Stored values, not an Excel calculation engine

Shared strings, inline/rich-run strings, numbers, Booleans, ISO date-typed strings,
and saved formula results are read. Phonetic annotations are excluded from text.
A key stored as text `0001` stays `0001`.

Excel number/date display formats are **not** evaluated. Numeric date values stay
serial numbers in the workbook's date system; custom numeric formats such as
`0000` do not create leading-zero string identifiers. Store identifiers as text.
Use CSV exported by Excel when its displayed formatting is required.

Formulas are never evaluated. A formula with a saved result imports that cached
result with an explicit warning that it may be stale. Recalculate and save the
workbook in Excel before linking when freshness matters. Formula cells without
usable cached results and Excel error cells reject the selected table rather
than silently supplying a fabricated value.

Merged cells, duplicate keys/headers, mismatched cell addresses, duplicate or
unordered row/cell coordinates, values beyond the header columns, and oversized
inputs are rejected. Completely empty data rows are skipped. A row containing
other values but no key is invalid. `dimension`/used-range declarations are not
expanded; a sparse worksheet cannot allocate millions of empty rows.

## Source browsing

The reusable `DataPreviewGrid` realizes at most five rows and three columns.
**Data filter** plus Enter or **Filter** applies ordinal-ignore-case substring
filtering across columns. Header buttons toggle ascending/descending order.
**Numeric sort** explicitly selects invariant finite numeric comparisons;
otherwise sorting is textual, so keys are not implicitly coerced. Equal values
retain original source order in either direction; nonnumeric numeric-sort values
remain last. **Clear** clears filtering and ordering.

The view is a row-ordinal array, not cloned row dictionaries. Source rows, keys,
baselines and the pending refresh plan are unchanged by these controls. The pane
states explicitly that refresh still uses every source row.

## Reusable APIs

```csharp
var workbook = XlsxDataWorkbook.Open(bytes);
foreach (var sheet in workbook.Worksheets)
    Console.WriteLine($"{sheet.Name} (hidden: {sheet.Hidden})");

var imported = workbook.ReadTable("Assets", "Plant assets", "AssetId", headerRow: 3);
var view = TabularDataView.Create(imported.Table, filter: "pump",
    sortColumn: "Utilization", numeric: true);

var plan = session.PreviewDataRefresh(imported.Table, matchField: "AssetId");
session.ApplyDataRefresh(plan);
```

`CsvDataTable.FromRows` creates the same immutable, validated keyed snapshot from
other providers without encoding and parsing intermediate CSV. Input row/header
collections are defensively copied; the view shares the immutable row objects.
`IExcelWorkspaceStorage` is optional and does not break CSV or drawing-file hosts.

## Bounds and package handling

Compressed workbook: 8 MiB; at most 2,048 ZIP entries, 16 MiB per expanded part,
64 MiB aggregate expanded/read bytes, and 1 MiB per catalog/relationship XML part.
At most 256 worksheet descriptors are read. ZIP aliases, noncanonical part paths,
path traversal and external selected-part relationships are rejected. XML DTDs
and external entity resolution are prohibited. No files are extracted to disk.

The selected worksheet has the shared table limits: 10,000 data rows, 128 columns,
250,000 cells and 4,194,304 decoded UTF-16 code units; each value has at most 4,096
code units. Scanned physical rows are limited to 11,024 and scanned cells to
250,000, including those before the header. Sparse coordinates may use ordinary
Excel row/column bounds without dense expansion. Shared strings are separately
bounded and loaded only when selected cells reference them.

Only normal XLSX workbook content types are accepted. Binary XLS/XLSB,
macro-enabled/encrypted workbooks, Excel tables/named-range selection, formatted
number/date conversion, live Excel synchronization, formula calculation,
PowerQuery, database/SharePoint providers and embedded source-table persistence
remain outside this increment. External links/connections are not fetched or run.

## Renderer and validation

Connector jump path construction now orders relevant jumps once in pooled scratch
storage, then traverses the route once, instead of filtering and sorting all
jumps for each segment. Equal-distance order and all three bridge styles retain
the previous output. The returned Skia path is caller-owned; rented arrays are
returned on success or failure. This does not change routing/crossing ownership,
whole-document history or route-cache invalidation.

Engine tests compare exact SVG path commands with a retained reference for
shuffled jumps, all bridge styles and equal-distance cases. An allocation
regression test checks the warmed reduction without a hardware-specific time gate.
`tools/DrawingSpace.SourceBenchmarks` reports raw warmed samples for path
construction and a 10,000-row source view. These are component measurements, not
frame-rate or whole-application claims.

The Excel browser suite uses actual pointer/keyboard/file-picker input and checks
worksheet/header selection, filtering, numeric sorting, keyed refresh after row
reordering and label edits, no-op refreshes, undo/redo, vector graphics, and native
persistence. It runs with every Build and again in public Pages verification.
