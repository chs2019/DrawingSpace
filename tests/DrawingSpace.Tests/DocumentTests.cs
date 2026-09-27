using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Stencils;

namespace DrawingSpace.Tests;

public sealed class DocumentTests
{
    [Theory]
    [InlineData("flowchart")] [InlineData("organization")] [InlineData("network")] [InlineData("blank")]
    public void TemplateRoundTrips(string template)
    {
        var doc = SampleDiagrams.Create(template); var json = DocumentCodec.Save(doc);
        Assert.Equal(json, DocumentCodec.Save(DocumentCodec.Load(json)));
    }
    [Fact] public void EveryMasterCreatesValidGeometry()
    {
        foreach (var master in StencilCatalog.All.SelectMany(s => s.Masters))
        {
            var doc = new DiagramDocument(); doc.Pages[0].Shapes.Add(master.Create(new(200, 200))); DocumentCodec.Validate(doc);
        }
    }
    [Fact] public void DuplicateIdentityIsRejected()
    {
        var doc = new DiagramDocument(); var shape = new Shape(); doc.Pages[0].Shapes.Add(shape); doc.Pages[0].Shapes.Add(shape.Clone());
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(doc));
    }
    [Fact] public void BrokenConnectorIsRejected()
    {
        var doc = new DiagramDocument(); doc.Pages[0].Connectors.Add(new() { SourceId = "missing" });
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(doc));
    }
    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(100001)]
    public void InvalidWidthIsRejected(double width)
    {
        var doc = new DiagramDocument(); doc.Pages[0].Shapes.Add(new() { Width = width });
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(doc));
    }
    [Fact] public void NonFiniteGeometryIsRejected()
    {
        var doc = new DiagramDocument(); doc.Pages[0].Shapes.Add(new() { X = double.NaN });
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(doc));
    }
    [Fact] public void UnknownVersionIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(new() { FormatVersion = 99 }));
    }
    [Fact] public void InvalidColorIsRejected()
    {
        var doc = new DiagramDocument(); var shape = new Shape(); shape.Style.Fill = "javascript:bad"; doc.Pages[0].Shapes.Add(shape);
        Assert.Throws<InvalidDataException>(() => DocumentCodec.Validate(doc));
    }
    [Fact] public void FreeEndpointsRoundTripWithoutRecursiveComputedProperties()
    {
        var doc = new DiagramDocument(); doc.Pages[0].Connectors.Add(new() { Start = new(10, 20), End = new(90, 100) });
        var json = DocumentCodec.Save(doc); Assert.DoesNotContain("normalized", json);
        Assert.Equal(new PointD(10, 20), DocumentCodec.Load(json).Pages[0].Connectors[0].Start);
    }
    [Fact] public void CloneDoesNotShareMutableCollections()
    {
        var source = new Shape(); source.Data["Owner"] = "A"; source.Comments.Add("One");
        var clone = source.Clone(); clone.Data["Owner"] = "B"; clone.Comments.Add("Two"); clone.Style.Fill = "#000000";
        Assert.Equal("A", source.Data["Owner"]); Assert.Single(source.Comments); Assert.Equal("#FFFFFF", source.Style.Fill);
    }
}
