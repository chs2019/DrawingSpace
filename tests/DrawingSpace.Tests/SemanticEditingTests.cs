using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;

namespace DrawingSpace.Tests;

public sealed class SemanticEditingTests
{
    [Fact] public void DependentFormulaRecalculatesAndUndoesWithItsSource()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a", Width = 192 }); session.AddShape(new() { Id = "b" });
        session.SetFormula("b", "Width", "Sheet.a!Width*2"); Assert.Equal(384, session.Page.Find("b")!.Width);
        session.Execute("Resize source", () => session.Page.Find("a")!.Width = 240);
        Assert.Equal(480, session.Page.Find("b")!.Width);
        session.Undo(); Assert.Equal(192, session.Page.Find("a")!.Width); Assert.Equal(384, session.Page.Find("b")!.Width);
    }
    [Fact] public void GuardedFormulaWinsOverDirectManipulation()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a" }); session.SetFormula("a", "Width", "GUARD(2 in)");
        session.Execute("Attempt resize", () => session.Page.Find("a")!.Width = 320);
        Assert.Equal(192, session.Page.Find("a")!.Width);
        Assert.Throws<InvalidOperationException>(() => session.SetFormula("a", "Width", "3 in"));
        session.SetFormula("a", "Width", "3 in", true); Assert.Equal(288, session.Page.Find("a")!.Width);
    }
    [Fact] public void SetAtRefRedirectsAWidthEditWithoutDestroyingTheFormula()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a" });
        session.SetFormula("a", "User.Size", "2 in"); session.SetFormula("a", "Width", "SETATREF(User.Size)");
        session.Execute("Resize", () => session.Page.Find("a")!.Width = 336);
        Assert.Equal(336, session.Page.Find("a")!.Width);
        Assert.Equal("SETATREF(User.Size)", session.Page.Find("a")!.Cells["Width"].Formula);
        Assert.Equal("3.5", session.Page.Find("a")!.Cells["User.Size"].Value);
    }
    [Fact] public void CyclesAreReportedWithoutCorruptingGeometry()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a" });
        session.SetFormula("a", "User.A", "User.B"); session.SetFormula("a", "User.B", "User.A");
        Assert.Contains(session.FormulaDiagnostics, d => d.Message.StartsWith("#CYCLE!"));
        DocumentCodec.Validate(session.Document);
    }
    [Fact] public void PinYUsesPageCoordinatesIncludingReferences()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a", X = 100, Y = 100 }); session.AddShape(new() { Id = "b", Y = 400 });
        session.SetFormula("b", "PinY", "Sheet.a!PinY");
        Assert.Equal(session.Page.Find("a")!.Bounds.Center.Y, session.Page.Find("b")!.Bounds.Center.Y);
    }
    [Fact] public void MasterUpdatePropagatesExceptForLocalOverrides()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "source" });
        var master = session.CreateMaster("source", "Reusable process");
        var first = session.InsertMaster(master.Id, new(100, 100))[0];
        var second = session.InsertMaster(master.Id, new(400, 100))[0];
        session.Select(first.Id); session.Format(s => s.Fill = "#00AA00");
        session.UpdateMaster(master.Id, m => m.Shape.Style.Fill = "#FF0000");
        Assert.Equal("#00AA00", session.Page.Find(first.Id)!.Style.Fill);
        Assert.Equal("#FF0000", session.Page.Find(second.Id)!.Style.Fill);
        session.Undo(); Assert.Equal("#FFFFFF", session.Page.Find(second.Id)!.Style.Fill);
        session.Redo(); Assert.Equal("#FF0000", session.Page.Find(second.Id)!.Style.Fill);
    }
    [Fact] public void MasterFormulaResolvesInEachInstanceScope()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "source" });
        var master = session.CreateMaster("source", "Ratio");
        session.UpdateMaster(master.Id, m => m.Shape.Cells["Height"] = new() { Formula = "Width/2", Unit = "IN" });
        var instance = session.InsertMaster(master.Id, new(300, 200))[0];
        session.Execute("Width override", () => instance.Width = 300);
        Assert.Equal(150, instance.Height);
    }
    [Fact] public void AffineDecompositionPreservesAllFourCorners()
    {
        var shape = new Shape { X = 90, Y = 130, Width = 170, Height = 63, Rotation = 37, ShearX = .3, FlipY = true };
        var transform = MatrixD.Translation(20, -30) * MatrixD.Scale(1.7, .8) * MatrixD.Rotation(13);
        var expected = shape.WorldCorners.Select(transform.Map).ToArray();
        shape.ApplyWorldTransform(transform);
        for (var i = 0; i < 4; i++) Assert.True(expected[i].Distance(shape.WorldCorners[i]) < 1e-8);
    }
    [Fact] public void NestedGroupTransformsAndUngroupKeepHierarchy()
    {
        var page = new DiagramPage(); page.Shapes.AddRange([new() { Id = "a" }, new() { Id = "b", X = 200 }, new() { Id = "c", X = 400 }]);
        var inner = GroupService.Create(page, ["a", "b"]); var outer = GroupService.Create(page, ["a", "b", "c"]);
        Assert.Equal(outer.Id, inner.ParentId); Assert.Equal(3, page.GroupShapes(outer.Id).Count);
        GroupService.Transform(page, page.GroupShapes(outer.Id), MatrixD.Translation(20, 30));
        Assert.Equal(220, page.Find("b")!.X, 8);
        GroupService.Ungroup(page, outer.Id); Assert.Null(inner.ParentId); Assert.Equal(inner.Id, page.Find("a")!.GroupId); Assert.Null(page.Find("c")!.GroupId);
    }
    [Fact] public void ContainerMembershipIsAcyclicAndMovesChildrenOnce()
    {
        var page = new DiagramPage(); var parent = new Shape { Id = "p", Kind = ShapeKind.Container, Container = new() }; var child = new Shape { Id = "c", X = 20, Y = 30 };
        page.Shapes.AddRange([parent, child]); ContainerService.Assign(page, child, parent.Id);
        GroupService.Transform(page, [parent, child], MatrixD.Translation(40, 50));
        Assert.Equal(60, child.X, 8); Assert.Equal(80, child.Y, 8);
        child.Container = new(); Assert.Throws<InvalidOperationException>(() => ContainerService.Assign(page, parent, child.Id));
    }
    [Fact] public void RichTextReplacementPreservesOutsideFormattingAndRejectsSplitSurrogates()
    {
        var shape = new Shape { Text = "A😀BCD" }; shape.TextSpans.Add(new() { Start = 3, Length = 3, Bold = true });
        Assert.Throws<ArgumentOutOfRangeException>(() => RichTextOperations.Replace(shape, 2, 0, "x"));
        RichTextOperations.Replace(shape, 1, 2, "hello");
        Assert.Equal("AhelloBCD", shape.Text); Assert.True(RichTextOperations.StyleAt(shape, 6).Bold);
        RichTextOperations.Format(shape, 6, 1, s => s.Color = "#FF0000");
        Assert.True(RichTextOperations.StyleAt(shape, 6).Bold); Assert.Equal("#FF0000", RichTextOperations.StyleAt(shape, 6).Color);
    }
}
