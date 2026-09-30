# Workbench presentation and recovery

The workbench uses document transactions for editing, but a camera change is not
a document edit. Keeping those lifetimes distinct avoids rebuilding forms while
users zoom, switch tools, change ribbon tabs or save an unchanged selection.

## Property-pane invalidation

`DrawingSpace.Editing.EditorPaneState` records the exact document instance,
revision, active page, pane identifier and selected identities. A pane captures
this state only after a successful build. Equal selection counts are insufficient:
selecting a different shape must rebuild the form. Selection enumeration order is
irrelevant. Reopening a closed pane or explicitly changing source settings forces
a rebuild. Undo restores a document instance and therefore invalidates old editors.

Viewport notifications update only zoom presentation. Tool/ribbon changes refresh
command state without recreating the task pane. Document changes still rebuild
an open pane after a transaction; this is not a fully retained form binding engine.
All direct model mutations must still use a transaction or the editor's existing
`Notify` contract. `EditorPaneState` is dispatcher-confined, like its owning session.

`PageStrip.Update` compares ordered page identities and names before constructing
controls. Unchanged tabs retain their controls and scroll/focus state; active-page
selection updates only the selected flags. Add, delete, rename and reorder still
rebuild the strip, including after native document replacement or undo.

## Command-state ownership

Quick-access, active-ribbon and active-pane registrations have separate lists.
Ribbon factories are materialized while their registration scope is active; the
scope is restored in `finally`. Replacing a ribbon or pane clears its old list.
Closing a pane also retires its data editors and clears its controls and bindings.
No detached-control sweep or garbage-collection timing is required to remove
retired callbacks from command-state refreshes.

Shape eligibility uses an early-exit scan rather than creating a selected-shape
array. Refresh avoids reassigning an unchanged `IsEnabled` value. These changes
remove unnecessary work; they do not establish a hardware-independent frame-time
or application-wide memory guarantee.

## Recovery status and snapshot ordering

The status bar has a separate recovery indicator: pending, saving, saved or
failed. A successful automatic/manual recovery copy never replaces the foreground
command result, such as a dimension-validation error or a no-op data refresh.
An explicit drawing-file save still reports its foreground completion/failure.

Explicit drawing-file saves and debounced recovery share one asynchronous write
gate. A queued snapshot is checked against document instance and revision before
writing. An already executing write cannot be cancelled by that gate; a subsequent
current snapshot is serialized after it. Its completion reports pending rather
than current when the document changed meanwhile. Recovery is not marked current
while an edit gesture is active.

The debounce writer defers while a gesture is uncommitted. Cancellation restores
the transaction baseline before it can be saved. The workbench stops scheduling
on disposal, and in-flight completions do not update disposed UI. The semaphore
is intentionally not disposed while an asynchronous writer could still release it.
Recovery failures are visible in the recovery indicator and logged; they do not
silently replace an unrelated command result. This is local snapshot recovery,
not a durable operation journal or a cross-window storage arbitration protocol.

## Validation

Run the headless regression suite:

```sh
dotnet test tests/DrawingSpace.Tests -c Release
```

`EditorPaneStateTests` covers initial/explicit invalidation, viewport/tool changes,
save notifications, same-sized different selections, selection-order changes,
document edits/undo, page/pane changes, distinct document instances with identical
identifiers, and bounded allocation over 10,000 warmed unchanged checks.

The browser runner includes `scripts/workbench-state-test.mjs`. It uses ordinary
pointer/keyboard/file-picker input and opt-in read-only diagnostics (`?test=1`).
No test API performs edits. Observations include a publication sequence, document
revision, undo/redo names, pane/tab rebuild counts, command registrations and
completed recovery writes. Command targeting requires three distinct stable
observations; Quick Access Save uses its explicit automation identity rather
than choosing arbitrarily among two visible Save buttons.

Seven workbench scenarios cover correct editors after selection changes,
zero pane/tab reconstruction on zoom/tool changes, saving with the File ribbon
open, stable registrations across 30 ribbon switches, foreground-message
preservation during a completed recovery write, page insertion/undo, and a live
drag held across the recovery interval followed by cancellation and reload.

The Excel no-op scenario also checks unchanged revision, shape snapshots and
undo/redo state, rather than treating a transient status string as proof of data
integrity. The existing SVG test requires three consecutive correct exports;
there is no assertion retry that converts a failed export into a pass.

Build and Pages run all seven suites. Counter output is retained in
`workbench-binding-results.json`; failures retain the snapshot, console and
screenshot. Counter assertions demonstrate eliminated rebuilds and bounded
registrations in those scenarios, not pixel-perfect Visio parity or GPU timing.
