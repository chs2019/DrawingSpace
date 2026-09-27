using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class EditingTests
{
    private static EditorSession Pair()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a", X = 100, Y = 100 }); session.AddShape(new() { Id = "b", X = 400, Y = 100 });
        session.Connect("a", "b"); return session;
    }
    [Fact] public void GestureIsOneUndoOperation()
    {
        var session = Pair(); var shape = session.Page.Find("a")!; var before = shape.X;
        session.Begin("Drag"); for (var i = 0; i < 50; i++) { shape.X++; session.Preview(); } session.Commit();
        session.Undo(); Assert.Equal(before, session.Page.Find("a")!.X); session.Redo(); Assert.Equal(before + 50, session.Page.Find("a")!.X);
    }
    [Fact] public void CancelRestoresDocumentAndSelection()
    {
        var session = Pair(); session.Select("a"); var json = DocumentCodec.Save(session.Document);
        session.Begin("Drag"); session.Page.Find("a")!.X = 999; session.Preview(); session.Cancel();
        Assert.Equal(json, DocumentCodec.Save(session.Document)); Assert.Contains("a", session.Selection);
    }
    [Fact] public void FailedTransactionRollsBack()
    {
        var session = Pair();
        Assert.Throws<InvalidDataException>(() => session.Execute("Bad edit", () => session.Page.Find("a")!.Width = -50));
        Assert.Equal(144, session.Page.Find("a")!.Width); Assert.False(session.IsInteracting);
    }
    [Fact] public void DeleteCascadesToConnectedEdgesAndUndoRestoresThem()
    {
        var session = Pair(); session.Select("a"); session.DeleteSelection(); Assert.Empty(session.Page.Connectors);
        session.Undo(); Assert.Equal(2, session.Page.Shapes.Count); Assert.Single(session.Page.Connectors);
    }
    [Fact] public void CopyRemapsIdentitiesAndInternalConnections()
    {
        var session = Pair(); session.SelectAll(); var clipboard = session.CopySelection(); session.Paste(clipboard, new(20, 20));
        Assert.Equal(4, session.Page.Shapes.Count); Assert.Equal(2, session.Page.Connectors.Count);
        Assert.NotEqual("a", session.Page.Connectors[1].SourceId); DocumentCodec.Validate(session.Document);
    }
    [Fact] public void CopySingleConnectorDetachesMissingShapes()
    {
        var session = Pair(); session.Select(session.Page.Connectors[0].Id); var copy = DocumentCodec.Load(session.CopySelection());
        Assert.Null(copy.Pages[0].Connectors[0].SourceId); Assert.Null(copy.Pages[0].Connectors[0].TargetId);
    }
    [Fact] public void GroupSelectionSelectsWholeGroup()
    {
        var session = Pair(); session.SelectAll(); session.Group(); session.Select("a"); Assert.Equal(2, session.SelectedShapes.Count);
        session.Ungroup(); session.Select("a"); Assert.Single(session.SelectedShapes);
    }
    [Fact] public void LockedShapeCannotBeMovedOrDeleted()
    {
        var session = Pair(); session.Page.Find("a")!.Locked = true; session.Select("a"); session.MoveSelection(new(200, 100)); session.DeleteSelection();
        Assert.Equal(100, session.Page.Find("a")!.X);
    }
    [Fact] public void NewEditAfterUndoDiscardsRedo()
    {
        var session = Pair(); session.Undo(); session.AddShape(new()); Assert.False(session.CanRedo);
    }
    [Fact] public void PageDeleteNeverRemovesLastPage()
    {
        var session = new EditorSession(); session.DeletePage(); Assert.Single(session.Document.Pages);
        session.AddPage(); session.DeletePage(); Assert.Single(session.Document.Pages); session.Undo(); Assert.Equal(2, session.Document.Pages.Count);
    }
    [Fact] public void AlignAndDistribute()
    {
        var session = new EditorSession();
        foreach (var x in new[] { 0, 150, 500 }) session.AddShape(new() { X = x, Y = x, Width = 50, Height = 50 });
        session.SelectAll(); session.Align(Alignment.Top); Assert.All(session.Page.Shapes, s => Assert.Equal(0, s.Y));
        session.Distribute(true); Assert.Equal(250, session.Page.Shapes[1].X);
    }
    [Fact] public void GridSnapIsStable()
    {
        var session = new EditorSession(); var snap = SnapService.Snap(session.Page, [], new(10, 10, 100, 100), new(13, 15), 1, true, false);
        Assert.Equal(new PointD(14, 14), snap.Delta);
    }
    [Fact] public void DirtyStateTracksSaveAndUndo()
    {
        var session = new EditorSession(); Assert.False(session.IsDirty); session.AddShape(new()); Assert.True(session.IsDirty);
        session.MarkSaved(); Assert.False(session.IsDirty); session.Undo(); Assert.True(session.IsDirty); session.Redo(); Assert.False(session.IsDirty);
    }
    [Fact] public void AutoConnectCreatesOneUndoableTransaction()
    {
        var session = new EditorSession(); var first = new Shape(); session.AddShape(first); session.AddConnected(first, PortSide.South);
        Assert.Equal(2, session.Page.Shapes.Count); Assert.Single(session.Page.Connectors); session.Undo(); Assert.Single(session.Page.Shapes); Assert.Empty(session.Page.Connectors);
    }
    [Fact] public void CyclicLayoutTerminatesAndPreservesConnections()
    {
        var session = Pair(); session.Connect("b", "a"); session.AutoLayout(); DocumentCodec.Validate(session.Document); Assert.Equal(2, session.Page.Connectors.Count);
    }
}
