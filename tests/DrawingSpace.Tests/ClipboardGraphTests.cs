using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Tests;

public class ClipboardGraphTests
{
    [Fact] public void CopyingAnchorIncludesCoordinateChildrenAndInternalConnectors()
    {
        var session = Imported(); session.Select("anchor", subselect: true);
        var copy = DocumentCodec.Load(session.CopySelection());
        Assert.Equal(2, copy.Pages[0].Shapes.Count); Assert.Single(copy.Pages[0].Connectors);
        Assert.Equal("anchor", copy.Pages[0].Find("child")!.FormulaParentId);
        Assert.Equal("anchor", Assert.Single(copy.Pages[0].Groups).AnchorShapeId);
    }

    [Theory] [InlineData(0)] [InlineData(31)] [InlineData(-63)]
    public void PartialGroupCopyDetachesTheFrameWithoutMovingGeometry(double rotation)
    {
        var session = Imported(rotation); var before = session.Page.Find("child")!.WorldMatrix;
        session.Select("child", subselect: true);
        var copy = DocumentCodec.Load(session.CopySelection()); var shape = Assert.Single(copy.Pages[0].Shapes);
        Assert.Null(shape.FormulaParentId); Assert.Null(Assert.Single(copy.Pages[0].Groups).AnchorShapeId);
        Assert.Empty(ShapeSheetService.Recalculate(copy)); Equal(before, shape.WorldMatrix);
        Assert.DoesNotContain("ParentShape!", shape.Cells["User.ParentWidth"].Formula);
    }

    [Theory] [InlineData(0, 0, 794)] [InlineData(80, 40, 1200)] [InlineData(-50, -70, 500)]
    public void WholeImportedGroupPastePreservesGeometryAtAnOffsetAcrossPageSizes(double x, double y, double height)
    {
        var source = Imported(); source.Select("anchor", subselect: true);
        var geometry = source.Page.Shapes.ToDictionary(s => s.Name, s => s.WorldMatrix);
        var target = new EditorSession(new DiagramDocument { Pages = [new() { Height = height }] });
        target.Paste(source.CopySelection(), new(x, y));
        Assert.Equal(2, target.Page.Shapes.Count); Assert.Single(target.Page.Connectors);
        foreach (var shape in target.Page.Shapes) Equal(MatrixD.Translation(x, y) * geometry[shape.Name], shape.WorldMatrix);
        Assert.Empty(target.FormulaDiagnostics); DocumentCodec.Validate(target.Document);
        var pasted = DocumentCodec.Save(target.Document); target.Undo(); Assert.Empty(target.Page.Shapes);
        target.Redo(); Assert.Equal(pasted, DocumentCodec.Save(target.Document));
    }

    [Fact] public void NativePinFormulasCannotPullPastedShapesBackToTheirSourcePosition()
    {
        var source = new EditorSession(); var shape = new Shape { Id = "native", X = 100, Y = 90 };
        shape.Cells["PinX"] = new() { Formula = "1 in", Value = "1", Unit = "IN" };
        source.AddShape(shape); source.Select(shape.Id);
        var old = shape.Bounds.Center;
        source.Paste(source.CopySelection(), new(64, 32));
        var pasted = source.SelectedShapes.Single(); Assert.Equal(old.X + 64, pasted.Bounds.Center.X, 8); Assert.Equal(old.Y + 32, pasted.Bounds.Center.Y, 8);
    }

    [Fact] public void NumericVisioReferencesRebindToCopiedPeersNotExistingDestinationShapes()
    {
        var session = new EditorSession(); var a = new Shape { Id = "first", VisioId = 17, Width = 240 };
        var b = new Shape { Id = "second", VisioId = 18, X = 300 };
        b.Cells["Width"] = new() { Formula = "Sheet.17!Width/2", Value = "1.25", Unit = "IN" };
        b.Cells["User.Text"] = new() { Formula = "\"Sheet.17!Width\"", Value = "Sheet.17!Width" };
        session.Page.Shapes.AddRange([a, b]); session.SelectAll(); session.Duplicate();
        var copies = session.SelectedShapes; var first = copies.Single(s => s.Cells.Count == 0); var second = copies.Single(s => s.Cells.Count > 0);
        Assert.Contains("Sheet." + first.Id + "!Width", second.Cells["Width"].Formula);
        Assert.Equal("\"Sheet.17!Width\"", second.Cells["User.Text"].Formula);
        session.Execute("Edit original", () => a.Width = 400); Assert.Equal(120, second.Width, 8);
    }

    [Fact] public void ReferencesToUncopiedShapesBecomeDimensionalLiterals()
    {
        var session = new EditorSession(); var a = new Shape { Id = "reference", Width = 384 };
        var b = new Shape { Id = "selected", X = 500 };
        b.Cells["Width"] = new() { Formula = "Sheet.reference!Width/2", Value = "2", Unit = "IN" };
        session.Page.Shapes.AddRange([a, b]); session.Select("selected");
        var copy = DocumentCodec.Load(session.CopySelection()); Assert.Empty(ShapeSheetService.Recalculate(copy));
        Assert.Equal(192, copy.Pages[0].Shapes[0].Width, 8);
        Assert.DoesNotContain("Sheet.reference!", copy.Pages[0].Shapes[0].Cells["Width"].Formula);
    }

    [Fact] public void MasterBundlePasteRemapsTemplateGroupsConnectionsAndFrames()
    {
        var source = Imported(); var page = source.Page;
        var master = new DiagramMaster { Id = "master", Shape = page.Find("anchor")!.Clone(), Children = [page.Find("child")!.Clone()],
            Connectors = page.Connectors.Select(c => c.Clone()).ToList(), Groups = page.Groups.Select(g => g.Clone()).ToList() };
        // Template identities must differ from the page's global identities.
        master.Shape.Id = "templateAnchor"; master.Children[0].Id = "templateChild";
        master.Shape.GroupId = master.Children[0].GroupId = master.Groups[0].Id = "templateGroup";
        master.Groups[0].AnchorShapeId = master.Shape.Id; master.Children[0].FormulaParentId = master.Shape.Id;
        master.Connectors[0].Id = "templateEdge"; master.Connectors[0].SourceId = master.Shape.Id; master.Connectors[0].TargetId = master.Children[0].Id;
        master.Connectors[0].GroupId = "templateGroup";
        source.Document.Masters.Add(master);
        foreach (var shape in page.Shapes)
        { shape.MasterId = master.Id; shape.MasterShapeId = shape.Id == "anchor" ? master.Shape.Id : master.Children[0].Id; shape.MasterInstanceId = "anchor"; }
        source.SelectAll(); var target = new EditorSession(); target.Paste(source.CopySelection(), new(20, 30));
        DocumentCodec.Validate(target.Document); var imported = Assert.Single(target.Document.Masters);
        Assert.Equal(imported.Shape.Id, Assert.Single(imported.Groups).AnchorShapeId);
        Assert.Equal(imported.Shape.Id, imported.Children[0].FormulaParentId);
        Assert.Equal(imported.Shape.Id, Assert.Single(imported.Connectors).SourceId);
        Assert.NotEqual("templateGroup", imported.Groups[0].Id); Assert.All(target.Page.Shapes, s => Assert.Equal(imported.Id, s.MasterId));
        Assert.Single(target.Page.Shapes.Select(s => s.MasterInstanceId).Distinct());
    }

    [Fact] public void ExistingLayerLocksAreNeverBypassedWhenPasting()
    {
        var source = new EditorSession(); source.AddShape(new()); var target = new EditorSession();
        target.Page.Layers[0].Locked = true;
        target.Paste(source.CopySelection(), new()); Assert.False(target.Page.IsLocked(target.SelectedShapes.Single()));
        Assert.True(target.Page.Layers[0].Locked); Assert.Equal(2, target.Page.Layers.Count);
    }

    [Fact] public void PasteFailureRollsBackImportedMastersLayersAndSelection()
    {
        var source = Imported(); source.SelectAll(); var target = new EditorSession(); var before = DocumentCodec.Save(target.Document);
        Assert.Throws<InvalidDataException>(() => target.Paste(source.CopySelection(), new(2_000_000, 0)));
        Assert.Equal(before, DocumentCodec.Save(target.Document)); Assert.False(target.IsInteracting); Assert.False(target.CanUndo);
    }

    [Theory] [InlineData(0)] [InlineData(31)] [InlineData(-63)]
    public void UngroupRebasesImportedChildrenAndDoesNotResurrectTheirOldFrame(double angle)
    {
        var session = Imported(angle); var before = session.Page.Shapes.ToDictionary(s => s.Id, s => s.WorldMatrix);
        session.Select("anchor"); session.Ungroup();
        Assert.Empty(session.Page.Groups); Assert.Null(session.Page.Find("child")!.FormulaParentId);
        Assert.False(session.Page.Find("anchor")!.IsGroupAnchor); Assert.Empty(session.FormulaDiagnostics);
        foreach (var shape in session.Page.Shapes) Equal(before[shape.Id], shape.WorldMatrix);
        session.Recalculate(); foreach (var shape in session.Page.Shapes) Equal(before[shape.Id], shape.WorldMatrix);
        session.Undo(); session.Undo(); Assert.Single(session.Page.Groups);
    }

    [Fact] public void DeletingOnlyAnImportedAnchorPreservesUnselectedChildrenInWorldSpace()
    {
        var session = Imported(); var before = session.Page.Find("child")!.WorldMatrix;
        session.Select("anchor", subselect: true); session.DeleteSelection();
        var child = Assert.Single(session.Page.Shapes); Assert.Null(child.FormulaParentId); Equal(before, child.WorldMatrix);
        DocumentCodec.Validate(session.Document); session.Undo(); Assert.Equal("anchor", session.Page.Find("child")!.FormulaParentId);
    }

    [Fact] public void LockedGroupMembersPreventUngroupAtomically()
    {
        var session = Imported(); session.Page.Find("child")!.Locked = true; session.Select("anchor");
        var before = DocumentCodec.Save(session.Document);
        Assert.Throws<InvalidOperationException>(session.Ungroup); Assert.Equal(before, DocumentCodec.Save(session.Document));
    }

    internal static EditorSession Imported(double angle = 31)
    {
        var page = new DiagramPage();
        var anchor = FromLocal("anchor", new(3, 2, 3, 4, 1.5, 1, angle * Math.PI / 180, false, false), ShapeCoordinates.PageFrame(page));
        anchor.IsGroupAnchor = true; anchor.CoordinateWidth = 3; anchor.CoordinateHeight = 2; anchor.GroupId = "group";
        var child = FromLocal("child", new(1, .5, 1.2, .8, .5, .25, .2, false, false), ShapeCoordinates.ChildFrame(anchor));
        child.FormulaParentId = anchor.Id; child.GroupId = "group";
        child.Cells["User.ParentWidth"] = new() { Formula = "ParentShape!Width", Value = "3", Unit = "IN" };
        page.Shapes.AddRange([anchor, child]); page.Groups.Add(new() { Id = "group", AnchorShapeId = "anchor" });
        page.Connectors.Add(new() { Id = "edge", SourceId = "anchor", TargetId = "child", GroupId = "group" });
        return new(new DiagramDocument { Pages = [page] });
    }

    private static Shape FromLocal(string id, LocalShapeTransform value, MatrixD frame)
    {
        var shape = new Shape { Id = id, Name = id, UsesVisioCoordinates = true };
        shape.WorldMatrix.TryInvert(out var inverse); shape.ApplyWorldTransform(ShapeCoordinates.Compose(frame, value) * inverse);
        void Cell(string name, double number, string unit) => shape.Cells[name] = new() { Value = number.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Unit = unit };
        Cell("Width", value.Width, "IN"); Cell("Height", value.Height, "IN"); Cell("PinX", value.PinX, "IN"); Cell("PinY", value.PinY, "IN");
        Cell("LocPinX", value.LocPinX, "IN"); Cell("LocPinY", value.LocPinY, "IN"); Cell("Angle", value.Angle, "RAD");
        Cell("FlipX", value.FlipX ? 1 : 0, ""); Cell("FlipY", value.FlipY ? 1 : 0, "");
        return shape;
    }

    internal static void Equal(MatrixD a, MatrixD b)
    {
        Assert.Equal(a.A, b.A, 7); Assert.Equal(a.B, b.B, 7); Assert.Equal(a.C, b.C, 7);
        Assert.Equal(a.D, b.D, 7); Assert.Equal(a.Tx, b.Tx, 7); Assert.Equal(a.Ty, b.Ty, 7);
    }
}
