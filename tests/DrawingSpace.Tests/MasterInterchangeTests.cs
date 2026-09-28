using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Visio;
using System.IO.Compression;
using System.Xml.Linq;

namespace DrawingSpace.Tests;

public sealed class MasterInterchangeTests
{
    private static EditorSession Create()
    {
        var editor = new EditorSession();
        editor.AddShape(new() { Id = "receive", Name = "Receive", Text = "Receive", X = 100, Y = 100, Width = 120, Height = 70 });
        editor.AddShape(new() { Id = "approve", Name = "Approve", Text = "Approve", X = 340, Y = 140, Width = 130, Height = 80 });
        editor.Connect("receive", "approve"); editor.SelectAll();
        return editor;
    }

    [Fact]
    public void AuthoredBundleHasOneOuterAnchorAndExportsUniqueNumericShapeIds()
    {
        var editor = Create(); var master = editor.CreateMasterFromSelection("Workflow");
        editor.InsertMaster(master.Id, new(150, 400));
        Assert.Single(editor.Page.Groups);
        Assert.Single(editor.Page.Groups.Select(g => g.AnchorShapeId).Distinct());
        var bytes = VisioWriter.Write(editor.Document).Bytes;
        using var zip = new ZipArchive(new MemoryStream(bytes));
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") && (e.FullName.StartsWith("visio/pages/page") || e.FullName.StartsWith("visio/masters/master"))))
        {
            using var stream = entry.Open(); var root = XDocument.Load(stream);
            var ids = root.Descendants().Where(e => e.Name == XName.Get("Shape", "http://schemas.microsoft.com/office/visio/2012/main")).Select(e => (string?)e.Attribute("ID")).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
        var loaded = VisioReader.Read(bytes, "workflow.vsdx").Document;
        Assert.Equal(editor.Page.Shapes.Count, loaded.Pages[0].Shapes.Count);
        Assert.Equal(editor.Page.Connectors.Count, loaded.Pages[0].Connectors.Count);
        Assert.All(editor.Page.Shapes, expected =>
        {
            var actual = loaded.Pages[0].Shapes.Single(s => s.Id == expected.Id);
            foreach (var point in new[] { new PointD(0, 0), new PointD(1, 1), new PointD(1, 0), new PointD(0, 1) })
                Assert.True(expected.WorldMatrix.Map(point).Distance(actual.WorldMatrix.Map(point)) < 1e-6);
        });
    }

    [Fact]
    public void CapturedStencilCanBeImportedAndInsertedAsAnIndependentConnectedMaster()
    {
        var source = Create(); var master = source.CreateMasterFromSelection("Workflow");
        var bytes = VisioWriter.Write(source.Document, new() { Kind = VisioPackageKind.Stencil }).Bytes;
        var library = VisioReader.Read(bytes, "library.vssx").Document;
        Assert.Single(library.Masters);
        var target = new EditorSession(); target.ImportMasters(library.Masters);
        var imported = Assert.Single(target.Document.Masters);
        Assert.NotEqual(master.Id, imported.Id);
        var instance = target.InsertMaster(imported.Id, new(200, 200));
        Assert.Equal(3, target.Page.Shapes.Count); Assert.Single(target.Page.Connectors);
        Assert.All(target.Page.Shapes, s => Assert.Equal(instance[0].Id, s.MasterInstanceId));
        var edge = target.Page.Connectors[0];
        Assert.Contains(target.Page.Shapes, s => s.Id == edge.SourceId && s.Text == "Receive");
        Assert.Contains(target.Page.Shapes, s => s.Id == edge.TargetId && s.Text == "Approve");
        DocumentCodec.Validate(target.Document);
    }
}
