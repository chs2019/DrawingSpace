using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public class EditingPerformanceTests
{
    [Fact] public void DirtyReadsDoNotSerializeACommittedDrawingRepeatedly()
    {
        var session = new EditorSession(); session.AddShape(new());
        for (var i = 0; i < 100; i++) _ = session.IsDirty;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0; for (var i = 0; i < 1000; i++) if (session.IsDirty) count++;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000, count); Assert.True(allocated < 1024, $"Dirty getters allocated {allocated} bytes.");
        session.MarkSaved(); Assert.False(session.IsDirty);
        session.Document.Title = "Direct edit"; session.Notify(ChangeKind.Document); Assert.True(session.IsDirty);
        session.MarkSaved(); session.Undo(); Assert.True(session.IsDirty);
    }

    [Fact] public void PreviewDirtyCacheIsInvalidatedForEveryNotifiedRevision()
    {
        var session = new EditorSession(); session.AddShape(new()); session.MarkSaved();
        session.Begin("Move"); var shape = session.SelectedShapes.Single(); var x = shape.X;
        shape.X += 10; session.Preview(); Assert.True(session.IsDirty);
        shape.X = x; session.Preview(); Assert.False(session.IsDirty);
        session.Cancel(); Assert.False(session.IsDirty);
    }

    [Fact] public void TransformPreviewAllocationsDoNotGrowWithUnselectedPageObjects()
    {
        static long Measure(int count)
        {
            var page = new DiagramPage();
            for (var i = 0; i < count; i++) page.Shapes.Add(new() { X = i * 2 });
            var snapshot = SelectionTransformSnapshot.Capture(page, page.Shapes.Take(2));
            for (var i = 0; i < 10; i++) snapshot.Apply(MatrixD.Translation(i, 0));
            var start = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) snapshot.Apply(MatrixD.Translation(i, 0));
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        var small = Measure(10); var large = Measure(10_000);
        Assert.True(large <= small + 4096, $"Preview allocations grew from {small} to {large} bytes.");
        Assert.True(large < 64 * 1024, $"Preview allocations exceeded the selected-only budget: {large}.");
    }

    [Fact] public void ReorderedCollectionsReindexButReplacementTargetsAreRejectedAtomically()
    {
        var page = new DiagramPage(); var a = new Shape { Id = "a" }; var b = new Shape { Id = "b", X = 200 };
        page.Shapes.AddRange([a, b]); var snapshot = SelectionTransformSnapshot.Capture(page, page.Shapes);
        page.Shapes.Reverse(); snapshot.Apply(MatrixD.Translation(10, 20)); Assert.Equal(10, a.X); Assert.Equal(210, b.X);
        page.Shapes[0] = b.Clone(); var before = a.WorldMatrix;
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(MatrixD.Translation(50, 60))); Assert.Equal(before, a.WorldMatrix);
    }

    [Fact] public void WaypointListStorageIsReusedAndFailedProjectionsLeaveAllTargetsUnchanged()
    {
        var page = new DiagramPage(); var shape = new Shape(); page.Shapes.Add(shape);
        var edge = new Connector { Start = new(1, 2), End = new(80, 90), Waypoints = [new(50, 30)] }; page.Connectors.Add(edge);
        var snapshot = SelectionTransformSnapshot.Capture(page, [shape], [edge]); var list = edge.Waypoints;
        snapshot.Apply(MatrixD.Translation(4, 5)); Assert.Same(list, edge.Waypoints); Assert.Equal(new PointD(54, 35), list[0]);
        var before = shape.WorldMatrix; var point = list[0];
        Assert.Throws<InvalidOperationException>(() => snapshot.Apply(MatrixD.Translation(2_000_000, 0)));
        Assert.Equal(before, shape.WorldMatrix); Assert.Equal(point, list[0]);
        snapshot.Apply(MatrixD.Identity); Assert.Equal(new PointD(50, 30), list[0]);
    }

    [Fact] public void IndexedSheetReferencesPreserveMasterInstanceIsolation()
    {
        var template = new Shape { Id = "template", VisioId = 7 }; var master = new DiagramMaster { Id = "master", Shape = template };
        var a = new Shape { Id = "a", Width = 96, MasterId = master.Id, MasterShapeId = template.Id, MasterInstanceId = "first" };
        var b = new Shape { Id = "b", Width = 240, MasterId = master.Id, MasterShapeId = template.Id, MasterInstanceId = "second" };
        var page = new DiagramPage { Shapes = [a, b] }; var document = new DiagramDocument { Pages = [page], Masters = [master] };
        var scope = new ShapeSheetScope(document, page);
        Assert.Same(a, scope.FindSheet(a, "Sheet.7")); Assert.Same(b, scope.FindSheet(b, "Sheet.7"));
        Assert.Same(a, scope.FindSheet(b, "Sheet.a"));
    }
}
