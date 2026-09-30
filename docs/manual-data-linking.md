# Manual source-row linking, creation and navigation

## Link and navigate

Open **Data > Open CSV**, or **Data > Link Data > Open Excel**, and preview the source.
Select shapes on the drawing. Click a source row or its row-header button, then
**Row Actions > Link to Selected Shapes**. Inspect the preview and use **Apply Refresh**
to commit. The row key is an exact, case-sensitive string; the visible sort order,
filter, row number, and shape label cannot change its identity.

One row can link several selected shapes. Each shape currently retains one source-row
link. An existing different link is skipped with a warning unless **Replace existing
links** is enabled. That permission is separate from **Overwrite local conflicts**.
Without overwrite permission, existing local values are retained and conflicts are
reported. A new row never borrows the previous row's accepted import baseline.

**Linked Shapes** selects visible linked shapes on the active page and fits the view.
**Show Linked Row** reveals the row for one selected shape. If a filter hides the row,
this explicit navigation clears that filter but retains the sort order. The link count
includes hidden and locked shapes on the current page; navigation only selects visible
shapes. It deliberately selects linked identities, not every member of a group. To
select just one shape from an overlapping selected pair, first click blank canvas;
pressing an already-selected shape retains the multiple selection for dragging.

**Unlink Row** removes links from editable shapes on the active page while retaining
all shape-data values and data graphics. Locked shapes remain linked. Unlinking is one
undoable operation. An unlinked row is a no-op and creates no document revision/history.

## Create a shape from a source row

Choose an exact row, then **Row Actions > Create Shape from Row**. DrawingSpace creates
a new 144 x 64 rectangle at the viewport center, snapped using the current grid settings.
Its label is the exact key, its shape data is an independent copy of all source fields,
and its accepted refresh baseline is stored immediately. Existing shapes are not
relinked or overwritten. The new shape is selected. One Undo removes the shape and
binding together; Redo restores the same shape identity and accepted values.

The command chooses the first visible, unlocked layer. It refuses creation when no
such layer exists. It does not invent formulas, inherit an arbitrary selected master,
or apply an arbitrary graphic. Add any of the existing data-graphic families afterward.

Embedding hosts can choose a label column, layer, explicit centers and multiple rows:

```csharp
var shape = session.CreateShapeFromDataRow(table, "0002", new(240, 160),
    labelColumn: "Name", layerId: "equipment");

var shapes = session.CreateShapesFromDataRows(table,
    [new("0001", new(160, 320)), new("0002", new(360, 320))],
    labelColumn: "Name");
```

Batch creation preflights every key, coordinate and target layer before changing the
document. Repeated keys are allowed and create independent shapes. Empty input is a
no-op. Limits are 1,024 placements and 250,000 imported shape-field values per operation,
in addition to the normal document and source budgets. Invalid input cannot partially
create an earlier valid row. Existing active gestures are neither committed nor cancelled.
The returned shape references belong to the current document; resolve their IDs again
after undo, redo or document replacement.

## Retained and reusable source browsing

`TabularDataCursor` owns source-view state independently of Uno controls: filter, sort,
numeric mode, paging, and selected key. The workbench reuses it while the immutable
source snapshot is unchanged, including document edits, undo/redo and pane recreation.
Providing/re-parsing a new source creates a new cursor. A filtered-out selection stays
selected by key, not by the new row at its old displayed position.

Arrow Up/Down move one row; Page Up/Down move five rows; Home/End select the first/last
row of the filtered view. Focus follows the selected header when a row page is rebuilt.
These keys do not clear filters or change the drawing. An absent/hidden selection starts
at the current page's first visible row. Empty views remain empty. `MoveSelection` and
`SelectVisibleRow` reuse a cached view index and run in O(1) without managed allocation.
Selecting an arbitrary key and changing the query may scan the view once to locate it;
filtering and sorting retain their existing costs. `SelectedViewIndex` is -1 for a hidden
or absent key. Failed queries retain the previous view and cached index.

`DataPreviewGrid` accepts the cursor and a `DataRowLinkIndex`. The index scans the active
page once per pane construction and stores read-only shape identities per exact key;
it does not search every shape for every displayed cell. Rebuild the index after document
edits, undo, page switches or direct model mutations. The grid realizes only five rows,
three data columns and one row-header column. Long cell strings remain display-clipped.

```csharp
var cursor = new TabularDataCursor(table);
cursor.SelectKey("0002");
cursor.Query("pump", "Progress", descending: true, numeric: true);
var links = new DataRowLinkIndex(session.Page, table);
var plan = session.PreviewLinkDataRow(table, cursor.SelectedKey!,
    overwriteConflicts: false, replaceExistingLinks: false);
session.ApplyDataRefresh(plan);
```

Refresh plans remain session/document/page/revision-bound. The complete original selection,
shape instance identities, locks, data, labels, and bindings are checked before writing.
ShapeSheet-owned fields are not overwritten. Resource limits, source-row validation,
three-way conflict resolution, transaction recalculation, and native persistence use
the existing data-refresh implementation rather than a second refresh mutation path.

## Performance scope

An unfiltered, unsorted `TabularDataView` represents the identity ordinal sequence
without allocating or populating an integer array proportional to source row count.
Filtered/sorted views continue sharing source row objects and retaining stable ordering.
The headless allocation regressions cover 100 views of a 10,000-row source and 20,000
cursor moves against an existing 10,000-row view. These measure current-thread managed
allocations, not total application/native/GPU memory or control construction.

Connector crossing analysis skips spatial-tree construction when no eligible later
connector can own a bridge. Route validation and segment budgets still execute first.
`LineJumpResult.SpatialIndexBuilt` exposes that branch for deterministic regressions.
The optimization does not skip real later-owner intersections or change bridge ownership.
No whole-application frame-rate or hardware-GPU speedup is inferred from these tests.

## Validation and compatibility boundaries

Headless tests cover linking, separate replacement/overwrite permissions, stale plans,
undo, locks, no-ops, independent baseline cloning, cursor navigation, source-row identity,
allocation bounds, creation preflight and crossing fast-path safeguards. Ten manual-data
browser scenarios use actual pointer/keyboard/file-picker input and are included in both
Build and public Pages checks. The suite retains content/history assertions during canvas
selection; the editor's renderer revision may advance for an empty pointer transaction.

This implements explicit linking, linked-row navigation and command-driven creation
inspired by [Microsoft's linking guidance](https://support.microsoft.com/en-us/visio/tips-and-tricks-for-linking-data)
and [linked shape creation API](https://learn.microsoft.com/en-us/office/vba/api/visio.page.droplinked),
not every Visio DataRecordset API or Visio's full drop workflow. Dragging rows onto the
canvas, selected-stencil/multi-shape-master creation, multiple simultaneous providers per
shape, live database/SharePoint sources, durable embedded source tables, complete Visio
interchange semantics and pixel-identical UI remain outside this increment. Native JSON
preserves established links and accepted baselines. XLSX still uses stored/cached values.
