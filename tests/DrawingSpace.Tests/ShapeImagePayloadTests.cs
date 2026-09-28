using System.IO.Compression;
using System.Xml.Linq;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Skia;
using DrawingSpace.Visio;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class ShapeImagePayloadTests
{
    [Fact]
    public void EmptyRasterPayloadIsAbsentAndCloningRetainsRealPayloadOwnership()
    {
        var shape = new Shape { ImageData = Array.Empty<byte>() };
        Assert.Null(shape.ImageData);
        Assert.Null(shape.Clone().ImageData);
        var bytes = new byte[] { 1, 2, 3 };
        shape.ImageData = bytes;
        Assert.Same(bytes, shape.ImageData);
        var clone = shape.Clone();
        Assert.Equal(bytes, clone.ImageData);
        Assert.NotSame(bytes, clone.ImageData);
        Assert.Throws<InvalidOperationException>(() => ShapeBooleanGeometry.CreatePathCopy(shape));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public void MissingAndEmptyJsonPayloadsRemainVectorShapes(string jsonPayload)
    {
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(new Shape());
        var json = DocumentCodec.Save(document).Replace("\"imageData\": null", "\"imageData\": " + jsonPayload, StringComparison.Ordinal);
        var loaded = DocumentCodec.Load(json);
        var shape = Assert.Single(loaded.Pages[0].Shapes);
        Assert.Null(shape.ImageData);
        Assert.NotNull(ShapeBooleanGeometry.CreatePathCopy(shape));
        using var stream = new MemoryStream(VisioWriter.Write(loaded).Bytes);
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        var page = package.GetEntry("visio/pages/page1.xml");
        Assert.NotNull(page);
        using var input = page.Open();
        var xml = XDocument.Load(input);
        var node = Assert.Single(xml.Descendants().Where(e => e.Name.LocalName == "Shape"));
        Assert.Equal("Shape", (string?)node.Attribute("Type"));
        Assert.DoesNotContain(node.Elements(), e => e.Name.LocalName == "ForeignData");
    }

    [Fact]
    public void AllBooleanCommandsCanFollowUndoAndRedoUsingRestoredModelInstances()
    {
        var session = new EditorSession();
        var a = new Shape { X = 0, Y = 0, Width = 100, Height = 100, ImageData = [] };
        var b = new Shape { X = 50, Y = 0, Width = 100, Height = 100 };
        session.AddShape(a); session.AddShape(b); session.SelectAll();
        var before = DocumentCodec.Save(session.Document);
        foreach (var operation in Enum.GetValues<ShapeBooleanOperation>())
        {
            var ids = new[] { a.Id, b.Id };
            var restored = session.GetGeometryOperands(ids);
            Assert.All(restored, shape => Assert.Null(shape.ImageData));
            var result = ShapeBooleanGeometry.Compute(restored, operation);
            Assert.NotNull(result);
            session.ReplaceShapesWithGeometry(ids, result, operation + " shapes");
            var after = DocumentCodec.Save(session.Document);
            session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
            session.Redo(); Assert.Equal(after, DocumentCodec.Save(session.Document));
            session.Undo(); Assert.Equal(before, DocumentCodec.Save(session.Document));
        }
    }
}
