# Manual source-row linking and navigation

## Workflow

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
shapes. It deliberately selects the linked identities, not every member of a group.

**Unlink Row** removes links from editable shapes on the active page while retaining
all shape-data values and data graphics. Locked shapes remain linked. Unlinking is one
undoable operation. An unlinked row is a no-op and creates no document revision/history.

## Retained and reusable source browsing

`TabularDataCursor` owns source-view state independently of Uno controls: filter, sort,
numeric mode, paging, and selected key. The workbench reuses it while the immutable
source snapshot is unchanged, including document edits, undo/redo and pane recreation.
Providing/re-parsing a new source creates a new cursor. A filtered-out selection stays
selected by key, not by the new row at its old displayed position.

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

Plans remain session/document/page/revision-bound. The complete original selection,
shape instance identities, locks, data, labels, and bindings are checked before writing.
ShapeSheet-owned fields are not overwritten. Resource limits, source-row validation,
three-way conflict resolution, transaction recalculation, and native persistence use
the existing data-refresh implementation rather than a second mutation path.

## Performance scope

An unfiltered, unsorted `TabularDataView` now represents the identity ordinal sequence
without allocating or populating an integer array proportional to source row count.
Filtered/sorted views continue sharing source row objects and retaining stable ordering.
The headless allocation regression uses 100 views of a 10,000-row source; it measures
current-thread managed allocations, not total application/native/GPU memory.

Connector crossing analysis skips spatial-tree construction when no eligible later
connector can own a bridge. Route validation and segment budgets still execute first.
`LineJumpResult.SpatialIndexBuilt` exposes that branch for deterministic regressions.
The optimization does not skip real later-owner intersections or change bridge ownership.
No whole-application frame-rate or hardware-GPU speedup is inferred from these tests.

## Validation and compatibility boundaries

Headless tests cover manual linking, separate replacement/overwrite permissions, stale
plans, undo, locks, no-ops, independent baseline cloning, cursor navigation, source-row
identity, allocation bounds, and crossing fast-path safeguards. The browser suite uses
real pointer/keyboard/file-picker input and is part of both Build and public Pages checks.

This implements the explicit linking and linked-row navigation workflows described in
[Microsoft's linking guidance](https://support.microsoft.com/en-us/visio/tips-and-tricks-for-linking-data),
not every Visio DataRecordset API. Dragging rows onto a canvas to create shapes, multiple
simultaneous providers per shape, live database/SharePoint sources, durable embedded
source tables, complete Visio interchange semantics and pixel-identical UI remain outside
this increment. Native JSON preserves the established links and accepted baselines.
