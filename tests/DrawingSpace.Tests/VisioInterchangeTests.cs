using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Visio;

namespace DrawingSpace.Tests;

public sealed class VisioInterchangeTests
{
    private const string Ns = "http://schemas.microsoft.com/office/visio/2012/main";
    private static DiagramDocument Sample()
    {
        var document = new DiagramDocument { Title = "Editable Ω process" }; var page = document.Pages[0];
        page.Shapes.Add(new() { Id = "start", Name = "Start", Text = "α start", Kind = ShapeKind.Ellipse, X = 100, Y = 150, Width = 140, Height = 60 });
        page.Shapes.Add(new() { Id = "decision", Name = "Decision", Text = "Approve?", Kind = ShapeKind.Decision, X = 370, Y = 180, Width = 150, Height = 90, Rotation = 17 });
        page.Shapes[1].Data["Owner"] = "Engineering"; page.Shapes[1].Cells["User.Budget"] = new() { Formula = "2+3", Value = "5" };
        page.Shapes[1].ConnectionPoints.Add(new() { Id = "custom", Position = new(.8, .3), Direction = new(1, 0) });
        page.Connectors.Add(new() { Id = "edge", SourceId = "start", TargetId = "decision", TargetPointId = "custom", Text = "Yes", Waypoints = [new(300, 180), new(300, 220)] });
        page.Shapes[0].TextSpans.Add(new() { Start = 2, Length = 5, Bold = true, Color = "#AA0033" });
        return document;
    }
    [Fact] public void VsdxContainsActualOpcPagesShapesAndConnections()
    {
        var document = Sample(); var output = VisioWriter.Write(document); var package = OpcPackage.Read(output.Bytes);
        Assert.NotNull(package.Xml("visio/document.xml")); Assert.NotNull(package.Xml("visio/pages/pages.xml"));
        var page = package.Xml("visio/pages/page1.xml")!;
        Assert.Equal(3, page.Descendants(XName.Get("Shape", Ns)).Count());
        Assert.Equal(2, page.Descendants(XName.Get("Connect", Ns)).Count());
        Assert.Contains("EllipticalArcTo", page.ToString()); Assert.DoesNotContain("DrawingSpaceModel", page.ToString());
    }
    [Fact] public void VsdxRoundTripsGeometryTextDataAndGluedCustomPoints()
    {
        var document = Sample(); var imported = VisioReader.Read(VisioWriter.Write(document).Bytes).Document;
        Assert.Equal(document.Title, imported.Title); var page = imported.Pages[0];
        Assert.Equal(2, page.Shapes.Count); Assert.Single(page.Connectors);
        foreach (var source in document.Pages[0].Shapes)
        {
            var target = page.Find(source.Id)!; Assert.Equal(source.Text, target.Text); Assert.Equal(source.Width, target.Width, 7); Assert.Equal(source.Height, target.Height, 7);
            for (var i = 0; i < 4; i++) Assert.True(source.WorldCorners[i].Distance(target.WorldCorners[i]) < 1e-6);
            Assert.NotEmpty(target.Geometry);
        }
        Assert.Equal("Engineering", page.Find("decision")!.Data["Owner"]);
        Assert.Equal("start", page.Connectors[0].SourceId); Assert.Equal("decision", page.Connectors[0].TargetId); Assert.NotNull(page.Connectors[0].TargetPointId);
        Assert.Contains(page.Find("start")!.TextSpans, s => s.Bold == true && s.Color == "#AA0033");
    }
    [Fact] public void NoEditRoundTripPreservesOriginalBytesExactly()
    {
        var bytes = VisioWriter.Write(Sample()).Bytes; var document = VisioReader.Read(bytes).Document;
        var result = VisioWriter.Write(document); Assert.True(result.OriginalBytesPreserved); Assert.Equal(bytes, result.Bytes);
    }
    [Fact] public void EditingPreservesUnknownOpcPartsAndUnknownShapeXml()
    {
        var original = OpcPackage.Read(VisioWriter.Write(Sample()).Bytes); original.Set("custom/data.bin", [1, 3, 3, 7]);
        var page = original.Xml("visio/pages/page1.xml")!; XNamespace extension = "urn:test:extension";
        page.Descendants(XName.Get("Shape", Ns)).First().Add(new XElement(extension + "PreserveMe", "unknown payload")); original.SetXml("visio/pages/page1.xml", page);
        var document = VisioReader.Read(original.Write()).Document; document.Pages[0].Find("start")!.Text = "Changed";
        var result = VisioWriter.Write(document); Assert.False(result.OriginalBytesPreserved);
        var exported = OpcPackage.Read(result.Bytes); Assert.Equal(new byte[] { 1, 3, 3, 7 }, exported.Get("custom/data.bin"));
        Assert.Contains("unknown payload", exported.Xml("visio/pages/page1.xml")!.ToString());
        Assert.Equal("Changed", VisioReader.Read(result.Bytes).Document.Pages[0].Find("start")!.Text);
    }
    [Fact] public void NativeNestedGroupsKeepTheirLeafWorldCoordinates()
    {
        var document = Sample(); var page = document.Pages[0];
        page.Shapes.Add(new() { Id = "third", Text = "Group child", X = 300, Y = 400, Rotation = -25 });
        var inner = GroupService.Create(page, ["start", "decision"]); GroupService.Create(page, ["start", "decision", "third"]);
        var imported = VisioReader.Read(VisioWriter.Write(document).Bytes).Document.Pages[0];
        Assert.Equal(2, imported.Groups.Count);
        foreach (var source in page.Shapes)
        {
            var target = imported.Find(source.Id)!;
            for (var i = 0; i < 4; i++) Assert.True(source.WorldCorners[i].Distance(target.WorldCorners[i]) < 1e-6);
        }
    }
    [Theory] [InlineData(VisioPackageKind.Stencil, "vssx")] [InlineData(VisioPackageKind.Template, "vstx")]
    public void ExportsStencilAndTemplateContentTypes(VisioPackageKind kind, string extension)
    {
        var document = Sample(); document.Masters.Add(MasterService.Create(document.Pages[0].Shapes[0], "Start master"));
        var output = VisioWriter.Write(document, new() { Kind = kind });
        Assert.Contains(kind == VisioPackageKind.Stencil ? "stencil.main" : "template.main", OpcPackage.Read(output.Bytes).Xml("[Content_Types].xml")!.ToString());
        var imported = VisioReader.Read(output.Bytes, "Drawing." + extension).Document; Assert.Single(imported.Masters);
        Assert.Equal(output.Bytes, VisioWriter.Write(imported, new() { Kind = kind }).Bytes);
    }
    [Fact] public void PageBackgroundReferencesSurviveInterchange()
    {
        var document = Sample(); var background = new DiagramPage { Name = "Background", IsBackground = true }; document.Pages.Add(background); document.Pages[0].BackgroundPageId = background.Id;
        var imported = VisioReader.Read(VisioWriter.Write(document).Bytes).Document;
        Assert.Equal(imported.Pages[1].Id, imported.Pages[0].BackgroundPageId); Assert.True(imported.Pages[1].IsBackground);
    }
    [Fact] public void ReadsLegacyXmlVdxWithoutExecutingAnything()
    {
        var xml = "<VisioDocument xmlns='http://schemas.microsoft.com/visio/2003/core'><Pages><Page ID='1' Name='Old page'><PageSheet><PageProps><PageWidth>11</PageWidth><PageHeight>8</PageHeight></PageProps></PageSheet><Shapes><Shape ID='1' NameU='Old shape'><XForm><PinX>2</PinX><PinY>3</PinY><Width>2</Width><Height>1</Height><LocPinX>1</LocPinX><LocPinY>0.5</LocPinY></XForm><Geom IX='0'><MoveTo IX='1'><X>0</X><Y>0</Y></MoveTo><LineTo IX='2'><X>2</X><Y>1</Y></LineTo></Geom><Text>Hello VDX</Text></Shape></Shapes></Page></Pages></VisioDocument>";
        var imported = VisioReader.Read(Encoding.UTF8.GetBytes(xml), "old.vdx").Document;
        Assert.Equal("Hello VDX", imported.Pages[0].Shapes[0].Text); Assert.Equal(192, imported.Pages[0].Shapes[0].Width, 8);
    }
    [Fact] public void DtdAndExternalEntitiesAreRejected()
    {
        var xml = "<!DOCTYPE VisioDocument [<!ENTITY leak SYSTEM 'file:///etc/passwd'>]><VisioDocument>&leak;</VisioDocument>";
        Assert.ThrowsAny<System.Xml.XmlException>(() => VisioReader.Read(Encoding.UTF8.GetBytes(xml), "bad.vdx"));
    }
    [Theory] [InlineData("../outside.xml")] [InlineData("/absolute.xml")] [InlineData("a\\b.xml")] [InlineData("a/%2e%2e/b.xml")] [InlineData("a//b.xml")]
    public void RejectsUnsafePartNames(string name) => Assert.Throws<InvalidDataException>(() => OpcPackage.NormalizePartName(name));
    [Fact] public void RelationshipTraversalCannotEscapeThePackage()
    {
        Assert.Equal("visio/masters/master1.xml", OpcPackage.ResolvePart("visio/pages/page1.xml", "../masters/master1.xml"));
        Assert.Throws<InvalidDataException>(() => OpcPackage.ResolvePart("visio/document.xml", "../../../outside.xml"));
        Assert.Throws<InvalidDataException>(() => OpcPackage.ResolvePart("visio/document.xml", "https://example.com/a"));
    }
    [Fact] public void ZipExpansionBudgetIsEnforced()
    {
        var package = new OpcPackage(); package.Set("[Content_Types].xml", Encoding.UTF8.GetBytes("<Types/>")); package.Set("large.bin", new byte[10000]);
        Assert.Throws<InvalidDataException>(() => OpcPackage.Read(package.Write(), new() { MaximumExpandedBytes = 1000 }));
    }
    [Fact] public void CaseAmbiguousZipEntriesAreRejected()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "[Content_Types].xml", "same.xml", "SAME.xml" }) { using var output = archive.CreateEntry(name).Open(); output.Write([1]); }
        Assert.Throws<InvalidDataException>(() => OpcPackage.Read(stream.ToArray()));
    }
    [Fact] public void ArcEndpointConversionKeepsEndpointAndMidpoint()
    {
        var segment = new GeometrySegment { Verb = GeometryVerb.Arc, End = new(.1, .5), Radius = new(.4, .2), Clockwise = true };
        Assert.True(ArcGeometry.TryFromEndpoint(new(.9, .5), segment, 200, 100, out var arc));
        Assert.True(arc.At(0).Distance(new(180, 50)) < 1e-7); Assert.True(arc.At(1).Distance(new(20, 50)) < 1e-7);
        Assert.True(ArcGeometry.TryThroughThreePoints(arc.At(0), arc.At(.5), arc.At(1), arc.Rotation, arc.RadiusX / arc.RadiusY, out var recovered));
        Assert.True(recovered.At(.5).Distance(arc.At(.5)) < 1e-7);
    }
    [Fact] public void WritesAnIndependentValidationFixture()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "visio-fixtures"); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "generated.vsdx"), VisioWriter.Write(Sample()).Bytes);
    }
}
