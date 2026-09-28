# Master authoring and lifecycle

## Capture a connected selection

Select diagram objects and choose **Developer → Masters → Create master from selection**. The resulting definition includes the selected shapes, group hierarchy, transitive container/formula-coordinate descendants and internal connectors. A multi-shape selection gets an unpainted group anchor enclosing its geometry. Source objects remain ordinary, unchanged diagram objects; inserting the master creates a separate linked instance.

Capture assigns independent shape, connector, group and template identities. ShapeSheet references within the captured graph continue to reference the corresponding instance's components. References to context outside the captured graph are materialized before severing the source context. Unresolved external references fail capture rather than silently introducing invalid formulas. String literals are never rewritten as references. Comments are not copied into reusable definitions.

**Create master from shape** remains available for the single-shape workflow. A selected container or formula-frame anchor still carries its semantic descendants; a reusable definition is not a flattened screenshot.

## Edit existing components

Choose a document master in the Masters pane. Multi-shape masters expose a component explorer, with the root and component shape names. Select a component to change its text, dimensions, position, rotation or fill. Existing linked instances inherit these edits except where they own a local property/cell override. Dependent formulas recalculate in their instance scope. Every command is transactional and undoable.

Unchanged rich text, path and connection-point lists are not repeatedly cloned on master refresh. Definitions and instances still own independent mutable resources; changed resources are copied rather than unsafely shared. Direct host changes to rich-text spans and paragraph formatting are detected as a local text override.

## Manage the library

**Rename master** changes the library name. **Duplicate master** creates an independent definition with remapped internal references. **Delete unused master** rejects in-use definitions. **Detach selected master instance** materializes the complete selected instance's inherited formulas and removes its template links, preserving geometry, groups and glued connectors. It does not ungroup the instance.

**Detach instances and delete master** requires explicit confirmation and operates across all document pages. It preserves same-instance formula dependencies as local references. A locked member rejects the operation; transaction rollback prevents partially detached pages. Undo restores the deleted definition and all instance links.

VSSX exports document master libraries. Importing a VSSX adds independent definitions to the open drawing. VSDX exports instantiated diagrams. Round-trip tests verify connected authored bundles, unique numeric shape identities, world geometry and live internal glue. These tests are not independent certification by Microsoft Visio.

## Headless API

```csharp
var master = session.CreateMasterFromSelection("Approval chain");
var instance = session.InsertMaster(master.Id, new PointD(300, 200));
var component = master.Children.First(s => s.Name == "Review");

session.UpdateMasterComponent(master.Id, component.Id, shape =>
{
    shape.Width = 180;
    RichTextOperations.ReplaceAll(shape, "Review purchase");
});

var independentCopy = session.DuplicateMaster(master.Id, "Approval chain copy");
session.DetachMasterInstance(instance[0].Id);
session.DeleteMaster(master.Id); // Throws if other live instances remain.
session.Undo();
```

`MasterAuthoring.Capture(document, page, selection, name)` returns a self-contained definition without changing its source. `MasterAuthoring.Detach` materializes the supplied instance members; reusable service callers own the surrounding transaction. Session commands are the recommended application boundary. Resolve entities by identity after undo/redo because history replaces model objects.

## Remaining boundaries

The component explorer edits existing component properties; it is not a separate free-form master drawing window. Live structural add/remove propagation, exhaustive nested-master inheritance, all Visio protection cells and every instance-update rule remain unfinished. Connector-specific master property overrides do not yet have the same inheritance machinery as component shapes. Capture can preserve a supported existing graph but is not a promise that every unsupported imported construct becomes editable.
