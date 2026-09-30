using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class EditorPaneStateTests
{
    [Fact]
    public void CaptureIsExplicitAndInvalidationForcesAnotherBuild()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        Assert.True(state.NeedsRebuild(session, "format"));
        Assert.True(state.NeedsRebuild(session, "format"));
        state.Capture(session, "format");
        Assert.False(state.NeedsRebuild(session, "format"));
        state.Invalidate();
        Assert.True(state.NeedsRebuild(session, "format"));
    }

    [Fact]
    public void ViewportAndToolChangesKeepTheCapturedPane()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        state.Capture(session, "format");
        session.Viewport.Zoom = 2; session.Notify(ChangeKind.Viewport);
        session.Tool = EditorTool.Connector;
        Assert.False(state.NeedsRebuild(session, "format"));
    }

    [Fact]
    public void SavingAnUnchangedSelectionDoesNotRebuildEditors()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        session.AddShape(new Shape()); state.Capture(session, "format");
        session.MarkSaved();
        Assert.False(state.NeedsRebuild(session, "format"));
    }

    [Fact]
    public void SameSizedDifferentSelectionInvalidatesButOrderDoesNot()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        session.Selection.UnionWith(["A", "B"]); state.Capture(session, "data");
        session.Selection.Clear(); session.Selection.UnionWith(["B", "A"]);
        Assert.False(state.NeedsRebuild(session, "data"));
        session.Selection.Remove("B"); session.Selection.Add("C");
        Assert.True(state.NeedsRebuild(session, "data"));
    }

    [Fact]
    public void DocumentEditsAndUndoInvalidateEvenWithTheSameSelection()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        session.AddShape(new Shape()); state.Capture(session, "format");
        session.Execute("Rename", () => session.Document.Title = "Changed");
        Assert.True(state.NeedsRebuild(session, "format"));
        state.Capture(session, "format"); session.Undo();
        Assert.True(state.NeedsRebuild(session, "format"));
    }

    [Fact]
    public void PageAndPaneChangesInvalidate()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        state.Capture(session, "format");
        Assert.True(state.NeedsRebuild(session, "data"));
        session.AddPage();
        Assert.True(state.NeedsRebuild(session, "format"));
    }

    [Fact]
    public void DifferentDocumentWithIdenticalIdentifiersCannotReuseEditors()
    {
        var first = new EditorSession();
        var second = new EditorSession(DocumentCodec.Load(DocumentCodec.Save(first.Document)));
        var state = new EditorPaneState(); state.Capture(first, "format");
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(first.ActivePageId, second.ActivePageId);
        Assert.True(state.NeedsRebuild(second, "format"));
    }

    [Fact]
    public void WarmChecksDoNotAllocateForAnUnchangedSelection()
    {
        var session = new EditorSession(); var state = new EditorPaneState();
        for (var i = 0; i < 256; i++) session.Selection.Add(i.ToString());
        state.Capture(session, "format");
        for (var i = 0; i < 1000; i++) _ = state.NeedsRebuild(session, "format");
        var before = GC.GetAllocatedBytesForCurrentThread(); var changed = false;
        for (var i = 0; i < 10000; i++) changed |= state.NeedsRebuild(session, "format");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(changed);
        Assert.InRange(allocated, 0, 256); // No array/dictionary snapshot per check.
    }
}
