using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Routing;
using DrawingSpace.Visio;

namespace DrawingSpace.Tests;

public sealed class MasterAuthoringTests
{
    private static EditorSession Diagram()
    {
        var session = new EditorSession();
        session.AddShape(new() { Id = "a", Name = "Receive", X = 100, Y = 100, Text = "Receive", Width = 120, Height = 70 });
        session.AddShape(new() { Id = "b", Name = "Approve", X = 340, Y = 140, Text = "Approve", Width = 130, Height = 80 });
        session.Connect("a", "b"); session.SelectAll(); return session;
    }

    [Fact] public void CaptureSelectionIsIndependentAndIncludesInternalGlue()
    {
        var session = Diagram(); var before = DocumentCodec.Save(session.Document);
        var master = MasterAuthoring.Capture(session.Document, session.Page, session.Selection, "Purchase");
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        Assert.True(master.Shape.IsGroupAnchor); Assert.Equal(2, master.Children.Count); Assert.Single(master.Connectors);
        Assert.All(master.Children, c => Assert.Equal(master.Shape.Id, c.FormulaParentId));
        Assert.Contains(master.Children, s => s.Id == master.Connectors[0].SourceId);
        Assert.Contains(master.Children, s => s.Id == master.Connectors[0].TargetId);
        Assert.DoesNotContain(master.Children, s => s.Id is "a" or "b");
        master.Children[0].Style.Fill = "#FF0000";
        Assert.Equal("#FFFFFF", session.Page.Find("a")!.Style.Fill);
    }

    [Fact] public void TwoInstancesPreserveLayoutAndTheirOwnInternalReferences()
    {
        var session = Diagram(); session.SetFormula("b", "Height", "Sheet.a!Height + 10 px");
        var master = session.CreateMasterFromSelection("Purchase");
        var first = session.InsertMaster(master.Id, new(150, 350));
        var second = session.InsertMaster(master.Id, new(600, 450));
        Assert.Equal(3, first.Count); Assert.Equal(3, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(450, second[i].X - first[i].X, 6);
            Assert.Equal(100, second[i].Y - first[i].Y, 6);
        }
        var a = first.Single(s => s.Name == "Receive"); var b = first.Single(s => s.Name == "Approve");
        session.Execute("Resize first member", () => a.Height = 110);
        Assert.Equal(120, b.Height, 6);
        Assert.Equal(80, second.Single(s => s.Name == "Approve").Height, 6);
        DocumentCodec.Validate(session.Document);
    }

    [Fact] public void CaptureResolvesContextAndDoesNotRewriteFormulaStringLiterals()
    {
        var session = Diagram(); session.SetFormula("a", "User.Page", "ThePage!PageWidth");
        session.SetFormula("a", "User.Label", "\"Sheet.a!Width\"");
        var master = session.CreateMasterFromSelection("Context");
        var component = master.Children.Single(s => s.Name == "Receive");
        Assert.DoesNotContain("ThePage!", component.Cells["User.Page"].Formula);
        Assert.Equal("\"Sheet.a!Width\"", component.Cells["User.Label"].Formula);
        var copy = session.DuplicateMaster(master.Id, "Copy");
        Assert.Equal("\"Sheet.a!Width\"", copy.Children.Single(s => s.Name == "Receive").Cells["User.Label"].Formula);
    }

    [Fact] public void ComponentEditsPropagateThroughCellsButRespectInstanceOverrides()
    {
        var session = Diagram(); var master = session.CreateMasterFromSelection("Purchase");
        var first = session.InsertMaster(master.Id, new(100, 350));
        var second = session.InsertMaster(master.Id, new(500, 350));
        var component = master.Children.Single(s => s.Name == "Receive");
        var local = first.Single(s => s.MasterShapeId == component.Id);
        session.SetText(local.Id, "Local label");
        session.UpdateMasterComponent(master.Id, component.Id, s => { s.Text = "Updated"; s.Width = 180; s.Style.Fill = "#FF0000"; });
        Assert.Equal("Local label", local.Text);
        Assert.Equal("Updated", second.Single(s => s.MasterShapeId == component.Id).Text);
        Assert.Equal(180, second.Single(s => s.MasterShapeId == component.Id).Width, 6);
        Assert.Equal("#FF0000", second.Single(s => s.MasterShapeId == component.Id).Style.Fill);
        session.Undo(); Assert.Equal("Receive", session.Document.Masters.Single().Children.Single(s => s.Id == component.Id).Text);
    }

    [Fact] public void DetachmentPreservesGeometryFormulasAndInternalGlue()
    {
        var session = Diagram(); session.SetFormula("b", "Height", "Sheet.a!Height + 10 px");
        var master = session.CreateMasterFromSelection("Purchase");
        var members = session.InsertMaster(master.Id, new(140, 400));
        var root = members[0]; var a = members.Single(s => s.Name == "Receive"); var b = members.Single(s => s.Name == "Approve");
        var positions = members.ToDictionary(s => s.Id, s => s.WorldMatrix);
        session.DetachMasterInstance(b.Id);
        Assert.All(members, s => { Assert.Null(s.MasterId); Assert.Equal(positions[s.Id], s.WorldMatrix); });
        session.Execute("Edit detached member", () => a.Height = 95);
        Assert.Equal(105, b.Height, 6);
        Assert.True(root.IsGroupAnchor); DocumentCodec.Validate(session.Document);
        session.Undo(); session.Undo(); Assert.Equal(master.Id, session.Page.Find(root.Id)!.MasterId);
    }

    [Fact] public void DeletingUsedMasterRequiresExplicitDetachmentAndIsUndoable()
    {
        var session = Diagram(); var master = session.CreateMasterFromSelection("Purchase");
        var members = session.InsertMaster(master.Id, new(100, 350));
        var before = DocumentCodec.Save(session.Document);
        Assert.Throws<InvalidOperationException>(() => session.DeleteMaster(master.Id));
        Assert.Equal(before, DocumentCodec.Save(session.Document));
        session.DeleteMaster(master.Id, true); Assert.Empty(session.Document.Masters);
        Assert.All(session.Page.Shapes, s => Assert.Null(s.MasterId));
        session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
    }

    [Fact] public void LockedMemberRejectsWholeDetachment()
    {
        var session = Diagram(); var master = session.CreateMasterFromSelection("Purchase");
        var members = session.InsertMaster(master.Id, new(100, 350)); members[1].Locked = true;
        var before = DocumentCodec.Save(session.Document);
        Assert.Throws<InvalidOperationException>(() => session.DetachMasterInstance(members[0].Id));
        Assert.Equal(before, DocumentCodec.Save(session.Document));
    }

    [Fact] public void UnchangedMasterRefreshRetainsIndependentResourceLists()
    {
        var session = Diagram(); var source = session.Page.Find("a")!;
        source.Geometry.Add(new() { Segments = [new() { Verb = GeometryVerb.Move }, new() { Verb = GeometryVerb.Line, End = new(1, 1) }] });
        source.ConnectionPoints.Add(new() { Id = "p", Position = new(.25, 1) });
        source.TextSpans.Add(new() { Start = 0, Length = 3, Bold = true });
        var master = session.CreateMaster("a", "Resources"); var instance = session.InsertMaster(master.Id, new(400, 400))[0];
        var geometry = instance.Geometry; var ports = instance.ConnectionPoints; var spans = instance.TextSpans;
        MasterService.Refresh(session.Document); MasterService.Refresh(session.Document);
        Assert.Same(geometry, instance.Geometry); Assert.Same(ports, instance.ConnectionPoints); Assert.Same(spans, instance.TextSpans);
        Assert.NotSame(master.Shape.Geometry, instance.Geometry);
        session.UpdateMaster(master.Id, m => m.Shape.Geometry[0].Segments[1].End = new(.8, .9));
        Assert.NotSame(geometry, instance.Geometry); Assert.Equal(new PointD(.8, .9), instance.Geometry[0].Segments[1].End);
    }

    [Fact] public void DirectHostTextFormattingBecomesALocalOverride()
    {
        var session = Diagram(); var master = session.CreateMaster("a", "Text");
        var instance = session.InsertMaster(master.Id, new(100, 350))[0];
        session.Execute("External host formatting", () => instance.TextSpans.Add(new() { Start = 0, Length = 3, Bold = true }));
        Assert.Single(instance.TextSpans); Assert.Contains("Text", instance.LocalOverrides);
        session.UpdateMaster(master.Id, m => m.Shape.Text = "Template change");
        Assert.Equal("Receive", instance.Text); Assert.Single(instance.TextSpans);
    }

    [Fact] public void EmptySelectionAndInvalidNameDoNotCreateMasters()
    {
        var session = Diagram(); session.Select(null);
        Assert.Throws<InvalidOperationException>(() => session.CreateMasterFromSelection("Empty"));
        session.SelectAll(); Assert.Throws<ArgumentException>(() => session.CreateMasterFromSelection(new string('n', 1025)));
        Assert.Empty(session.Document.Masters);
    }
    [Fact] public void ComponentPositionAndAngleRemainInstanceRelative()
    {
        var session = Diagram(); var master = session.CreateMasterFromSelection("Purchase");
        var first = session.InsertMaster(master.Id, new(150, 350));
        var second = session.InsertMaster(master.Id, new(550, 650));
        var template = master.Children.First(s => s.Name == "Approve");
        var a = first.First(s => s.MasterShapeId == template.Id); var b = second.First(s => s.MasterShapeId == template.Id);
        var oldA = a.WorldMatrix; var oldB = b.WorldMatrix;
        session.UpdateMasterComponent(master.Id, template.Id, s => { s.X += 25; s.Rotation = 30; });
        a = session.Page.Find(a.Id)!; b = session.Page.Find(b.Id)!;
        Assert.Equal(30, a.Rotation, 6); Assert.Equal(30, b.Rotation, 6);
        Assert.Equal(oldA.Map(new PointD(.5, .5)).X + 25, a.Bounds.Center.X, 6);
        Assert.Equal(oldB.Map(new PointD(.5, .5)).X + 25, b.Bounds.Center.X, 6);
        Assert.Equal(oldB.Map(new PointD(.5, .5)).Y - oldA.Map(new PointD(.5, .5)).Y, b.Bounds.Center.Y - a.Bounds.Center.Y, 6);
        Assert.Empty(session.FormulaDiagnostics);
    }

    [Fact] public void CaptureNestedGroupAndSemanticContainerRetainsItsClosure()
    {
        var session = Diagram(); session.Group();
        var container = new Shape { Id = "container", Name = "Stage", Kind = ShapeKind.Container, Container = new(), X = 70, Y = 50, Width = 450, Height = 220 };
        session.AddShape(container);
        session.Execute("Assign members", () => { session.Page.Find("a")!.ContainerId = container.Id; session.Page.Find("b")!.ContainerId = container.Id; });
        session.Select(container.Id);
        var master = session.CreateMasterFromSelection("Stage library");
        Assert.Equal(3, master.Children.Count); Assert.Single(master.Connectors); Assert.Equal(2, master.Groups.Count);
        var instance = session.InsertMaster(master.Id, new(200, 450));
        Assert.Equal(4, instance.Count);
        var stage = instance.Single(s => s.Name == "Stage");
        Assert.Equal(2, instance.Count(s => s.ContainerId == stage.Id));
        DocumentCodec.Validate(session.Document);
    }

}
