using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class ManualDataLinkTests
{
    private static CsvDataTable Source(string source = "Assets")
        => CsvDataTable.Parse("Id,Progress,Owner\n0001,10,Alice\n0002,90,Bob", source, "Id");
    private static EditorSession Session()
    {
        var document = new DiagramDocument();
        document.Pages[0].Shapes.Add(new() { Id = "a", Text = "Pump A" });
        document.Pages[0].Shapes.Add(new() { Id = "b", Text = "Pump B" });
        document.Pages[0].Shapes.Add(new() { Id = "c", Text = "Unselected" });
        var session = new EditorSession(document); session.Selection.UnionWith(["a", "b"]); return session;
    }
    private static void Link(EditorSession session, string key = "0001")
        => session.ApplyDataRefresh(session.PreviewLinkDataRow(Source(), key));

    [Fact] public void ManualRowLinksSeveralSelectedShapesWithoutMatchingLabelsAndUndoesAtomically()
    {
        var session = Session(); var before = DocumentCodec.Save(session.Document);
        var plan = session.PreviewLinkDataRow(Source(), "0001");
        Assert.Equal(2, plan.ChangedShapes); Assert.Empty(plan.Issues);
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        Assert.True(session.ApplyDataRefresh(plan));
        foreach (var id in new[] { "a", "b" })
        {
            var shape = session.Page.Find(id)!; Assert.Equal("0001", shape.DataBinding!.RowKey);
            Assert.Equal("10", shape.Data["Progress"]); Assert.StartsWith("Pump", shape.Text);
        }
        Assert.Null(session.Page.Find("c")!.DataBinding);
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.Redo(); Assert.Equal("0001", session.Page.Find("a")!.DataBinding!.RowKey);
    }

    [Fact] public void NoSelectionIsANoOpAndMissingKeysAreRejected()
    {
        var session = Session(); session.Selection.Clear(); var revision = session.Revision;
        Assert.False(session.ApplyDataRefresh(session.PreviewLinkDataRow(Source(), "0001")));
        Assert.Equal(revision, session.Revision);
        Assert.Throws<ArgumentException>(() => session.PreviewLinkDataRow(Source(), "1"));
        Assert.Throws<ArgumentNullException>(() => session.PreviewLinkDataRow(Source(), null!));
    }

    [Fact] public void RelinkingRequiresSeparatePermissionFromFieldOverwrite()
    {
        var session = Session(); Link(session);
        var plan = session.PreviewLinkDataRow(Source(), "0002", overwriteConflicts: true);
        Assert.Equal(2, plan.TotalIssues); Assert.Equal(0, plan.ChangedShapes);
        Assert.False(session.ApplyDataRefresh(plan));
        Assert.All(session.SelectedShapes, s => Assert.Equal("0001", s.DataBinding!.RowKey));
        plan = session.PreviewLinkDataRow(Source(), "0002", overwriteConflicts: true, replaceExistingLinks: true);
        Assert.True(session.ApplyDataRefresh(plan));
        Assert.All(session.SelectedShapes, s => { Assert.Equal("0002", s.DataBinding!.RowKey); Assert.Equal("90", s.Data["Progress"]); });
        session.Undo(); Assert.All(session.SelectedShapes, s => Assert.Equal("0001", s.DataBinding!.RowKey));
    }

    [Fact] public void AnotherRowsBaselineCannotAuthorizeLocalOverwrite()
    {
        var session = Session(); Link(session);
        var plan = session.PreviewLinkDataRow(Source(), "0002", replaceExistingLinks: true);
        Assert.Contains(plan.Issues, i => i.Field == "Progress");
        session.ApplyDataRefresh(plan);
        var shape = session.Page.Find("a")!;
        Assert.Equal("0002", shape.DataBinding!.RowKey);
        Assert.Equal("10", shape.Data["Progress"]);
        Assert.False(shape.DataBinding.Baseline.ContainsKey("Progress"));
        session.ApplyDataRefresh(session.PreviewDataRefresh(Source(), overwriteConflicts: true));
        Assert.Equal("90", session.Page.Find("a")!.Data["Progress"]);
    }

    [Fact] public void ForeignSourceWithSameKeyRequiresRelinkConsent()
    {
        var session = Session(); Link(session);
        var plan = session.PreviewLinkDataRow(Source("Other"), "0001");
        Assert.Equal(2, plan.TotalIssues); Assert.False(session.ApplyDataRefresh(plan));
        session.ApplyDataRefresh(session.PreviewLinkDataRow(Source("Other"), "0001", replaceExistingLinks: true));
        Assert.All(session.SelectedShapes, s => Assert.Equal("Other", s.DataBinding!.SourceId));
    }

    [Fact] public void LocalConflictsRemainUntilExplicitOverwrite()
    {
        var session = Session(); session.Page.Find("a")!.Data["Progress"] = "Local";
        var plan = session.PreviewLinkDataRow(Source(), "0001");
        Assert.Single(plan.Issues); session.ApplyDataRefresh(plan);
        Assert.Equal("Local", session.Page.Find("a")!.Data["Progress"]);
        session.ApplyDataRefresh(session.PreviewLinkDataRow(Source(), "0001", overwriteConflicts: true));
        Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]);
    }

    [Theory] [InlineData("selection")] [InlineData("data")] [InlineData("lock")] [InlineData("replace")]
    public void CompletePreflightRejectsAStaleManualPlanBeforeWritingAnyShape(string mutation)
    {
        var session = Session(); var plan = session.PreviewLinkDataRow(Source(), "0001");
        switch (mutation)
        {
            case "selection": session.Selection.Remove("b"); break;
            case "data": session.Page.Find("b")!.Data["Owner"] = "Changed"; break;
            case "lock": session.Page.Find("b")!.Locked = true; break;
            case "replace": session.Page.Shapes[1] = session.Page.Shapes[1].Clone(); break;
        }
        Assert.Throws<InvalidOperationException>(() => session.ApplyDataRefresh(plan));
        Assert.Null(session.Page.Find("a")!.DataBinding);
    }

    [Fact] public void LockedTargetsAreReportedAndOtherSelectedTargetsRemainEditable()
    {
        var session = Session(); session.Page.Find("b")!.Locked = true;
        var plan = session.PreviewLinkDataRow(Source(), "0001");
        Assert.Equal(1, plan.ChangedShapes); Assert.Single(plan.Issues);
        session.ApplyDataRefresh(plan);
        Assert.NotNull(session.Page.Find("a")!.DataBinding); Assert.Null(session.Page.Find("b")!.DataBinding);
    }

    [Fact] public void RelinkingTheSameUnchangedRowDoesNotAddHistory()
    {
        var session = Session(); Link(session); var revision = session.Revision; var undo = session.UndoName;
        Assert.False(session.ApplyDataRefresh(session.PreviewLinkDataRow(Source(), "0001")));
        Assert.Equal(revision, session.Revision); Assert.Equal(undo, session.UndoName);
    }

    [Fact] public void LinkedShapeNavigationDoesNotChangeRevisionOrHistory()
    {
        var session = Session(); Link(session); session.Select("c"); var revision = session.Revision;
        Assert.Equal(2, session.SelectShapesLinkedToDataRow(Source(), "0001"));
        Assert.True(session.Selection.SetEquals(["a", "b"])); Assert.Equal(revision, session.Revision);
        Assert.Equal(0, session.SelectShapesLinkedToDataRow(Source("Other"), "0001"));
    }

    [Fact] public void UnlinkRowPreservesGraphicsAndValuesSkipsLocksAndSupportsUndo()
    {
        var session = Session(); Link(session);
        session.SetSelectedDataGraphic(new() { Kind = DataGraphicKind.DataBar, Field = "Progress" });
        session.Execute("Lock", () => session.Page.Find("b")!.Locked = true);
        var before = DocumentCodec.Save(session.Document);
        Assert.Equal(1, session.UnlinkDataRow(Source(), "0001"));
        Assert.Null(session.Page.Find("a")!.DataBinding); Assert.NotNull(session.Page.Find("b")!.DataBinding);
        Assert.Equal("10", session.Page.Find("a")!.Data["Progress"]); Assert.Single(session.Page.Find("a")!.DataGraphics);
        var revision = session.Revision; Assert.Equal(0, session.UnlinkDataRow(Source(), "0001")); Assert.Equal(revision, session.Revision);
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
    }

    [Fact] public void LinkIndexIsAnImmutablePageLocalIdentitySnapshot()
    {
        var session = Session(); Link(session); var index = new DataRowLinkIndex(session.Page, Source());
        Assert.Equal(2, index.LinkedShapeCount); Assert.Equal(new[] { "a", "b" }, index.ShapesFor("0001"));
        Assert.Empty(index.ShapesFor("1")); Assert.Empty(new DataRowLinkIndex(session.Page, Source("Other")).ShapesFor("0001"));
        session.UnlinkDataRow(Source(), "0001"); Assert.Equal(2, index.ShapesFor("0001").Count);
        Assert.Empty(new DataRowLinkIndex(session.Page, Source()).ShapesFor("0001"));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)index.ShapesFor("0001")).Add("x"));
    }

    [Fact] public void NativeRoundTripKeepsManualLinksAndIndependentBaselines()
    {
        var session = Session(); Link(session);
        var loaded = DocumentCodec.Clone(session.Document);
        loaded.Pages[0].Find("a")!.DataBinding!.Baseline["Progress"] = "Changed";
        Assert.Equal("10", session.Page.Find("a")!.DataBinding!.Baseline["Progress"]);
        Assert.Equal("10", loaded.Pages[0].Find("b")!.DataBinding!.Baseline["Progress"]);
    }

    [Fact] public void GesturesRejectManualLinkAndNavigationWithoutMutatingSelection()
    {
        var session = Session(); session.Begin("Move");
        Assert.Throws<InvalidOperationException>(() => session.PreviewLinkDataRow(Source(), "0001"));
        Assert.Throws<InvalidOperationException>(() => session.UnlinkDataRow(Source(), "0001"));
        Assert.Throws<InvalidOperationException>(() => session.SelectShapesLinkedToDataRow(Source(), "0001"));
        Assert.True(session.Selection.SetEquals(["a", "b"])); session.Cancel();
    }
}
