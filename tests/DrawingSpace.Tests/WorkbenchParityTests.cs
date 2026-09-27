using System.Text;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using DrawingSpace.Routing;
using DrawingSpace.Skia;
using DrawingSpace.Text;
using DrawingSpace.Visio;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class WorkbenchParityTests
{
    private static DiagramMaster ConnectedMaster()
    {
        var master = new DiagramMaster { Id = "template", Name = "Connected pair", Shape = new() { Id = "root", VisioId = 7, X = 20, Y = 30, Width = 192, GroupId = "inner" } };
        master.Children.Add(new() { Id = "child", VisioId = 8, X = 300, Y = 30, Width = 96, GroupId = "inner" });
        master.Groups.Add(new() { Id = "inner", Name = "Nested pair" });
        master.Connectors.Add(new() { Id = "link", SourceId = "root", TargetId = "child", GroupId = "inner", Waypoints = [new(260, 90)] });
        master.Shape.ConnectionPoints.Add(new() { Id = "output", Position = new(1, .25), Direction = new(1, 0) });
        master.Connectors[0].SourcePointId = "output";
        return master;
    }

    [Fact] public void MasterBundleRetainsInternalConnectionsAndNestedGroups()
    {
        var session = new EditorSession(); session.Document.Masters.Add(ConnectedMaster());
        var shapes = session.InsertMaster("template", new(100, 160));
        Assert.Equal(2, shapes.Count); var edge = Assert.Single(session.Page.Connectors);
        Assert.Equal(shapes[0].Id, edge.SourceId); Assert.Equal(shapes[1].Id, edge.TargetId); Assert.Equal("output", edge.SourcePointId);
        Assert.Equal(new PointD(340, 220), Assert.Single(edge.Waypoints)); Assert.Equal(2, session.Page.Groups.Count);
        Assert.Equal(shapes[0].GroupId, edge.GroupId);
        Assert.Equal(100, shapes[0].X, 7); Assert.Equal(160, shapes[0].Y, 7);
        DocumentCodec.Validate(session.Document);
        session.Undo(); Assert.Empty(session.Page.Shapes); Assert.Empty(session.Page.Connectors); Assert.Empty(session.Page.Groups);
        session.Redo(); Assert.Equal(2, session.Page.Shapes.Count); Assert.Single(session.Page.Connectors); DocumentCodec.Validate(session.Document);
    }

    [Fact] public void MasterInstancesDoNotShareConnectionOrGroupIdentities()
    {
        var session = new EditorSession(); session.Document.Masters.Add(ConnectedMaster());
        var first = session.InsertMaster("template", new(10, 10)); var second = session.InsertMaster("template", new(500, 300));
        Assert.NotEqual(first[0].MasterInstanceId, second[0].MasterInstanceId);
        Assert.Equal(4, session.Page.Shapes.Select(s => s.Id).Distinct().Count());
        Assert.Equal(2, session.Page.Connectors.Select(s => s.Id).Distinct().Count());
        Assert.Equal(4, session.Page.Groups.Select(s => s.Id).Distinct().Count());
        foreach (var edge in session.Page.Connectors)
            Assert.Equal(session.Page.Find(edge.SourceId)!.MasterInstanceId, session.Page.Find(edge.TargetId)!.MasterInstanceId);
        DocumentCodec.Validate(session.Document);
    }

    [Fact] public void SheetReferencesResolveWithinTheCorrespondingMasterInstance()
    {
        var session = new EditorSession(); var master = ConnectedMaster();
        master.Children[0].Cells["Width"] = new() { Formula = "Sheet.7!Width/2", Unit = "IN" }; session.Document.Masters.Add(master);
        var first = session.InsertMaster(master.Id, new(10, 10)); var second = session.InsertMaster(master.Id, new(500, 300));
        session.SetFormula(first[0].Id, "Width", "4 in");
        Assert.Equal(192, session.Page.Find(first[1].Id)!.Width, 7);
        Assert.Equal(96, session.Page.Find(second[1].Id)!.Width, 7);
        Assert.Empty(session.FormulaDiagnostics);
    }

    [Fact] public void ImportingAStencilTwiceRemapsAllInternalIdentities()
    {
        var session = new EditorSession(); var master = ConnectedMaster(); var original = ModelJson.Serialize(master);
        var a = session.ImportMasters([master]).Single(); var b = session.ImportMasters([master]).Single();
        Assert.NotEqual(a.Id, b.Id); Assert.NotEqual(a.Shape.Id, b.Shape.Id); Assert.Equal(original, ModelJson.Serialize(master));
        Assert.Equal(a.Shape.Id, a.Connectors[0].SourceId); Assert.Equal(a.Children[0].Id, a.Connectors[0].TargetId);
        session.InsertMaster(a.Id, new(10, 10)); session.InsertMaster(b.Id, new(500, 10)); DocumentCodec.Validate(session.Document);
        session.Undo(); session.Undo(); session.Undo(); Assert.Single(session.Document.Masters);
    }

    [Fact] public void MasterLibraryRoundTripStillSupportsInsertion()
    {
        var document = new DiagramDocument(); document.Masters.Add(ConnectedMaster());
        var written = VisioWriter.Write(document, new() { Kind = VisioPackageKind.Stencil });
        var read = DrawingFileCodec.Read(new("sample.vssx", written.Bytes));
        var session = new EditorSession(); var imported = session.ImportMasters(read.Document.Masters).Single();
        session.InsertMaster(imported.Id, new(250, 200)); Assert.NotEmpty(session.Page.Shapes); Assert.Single(session.Page.Connectors);
        DocumentCodec.Validate(session.Document);
    }

    [Fact] public void FileDetectionHandlesUtf8BomAndIgnoresMisleadingExtensions()
    {
        var document = new DiagramDocument { Title = "Śląsk — drawing" };
        var json = Encoding.UTF8.GetBytes(" \n" + DocumentCodec.Save(document));
        var bytes = new byte[] { 239, 187, 191 }.Concat(json).ToArray();
        var read = DrawingFileCodec.Read(new("actually-json.vsdx", bytes)); Assert.Equal(document.Title, read.Document.Title);
    }

    [Fact] public void FileDetectionLoadsAnActualZipPackage()
    {
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(new() { Text = "Imported" });
        var read = DrawingFileCodec.Read(new("package.bin", VisioWriter.Write(document).Bytes));
        Assert.Equal("Imported", Assert.Single(read.Document.Pages[0].Shapes).Text);
    }

    [Fact] public void InvalidFilesAndCancellationCannotReplaceAnExistingDrawing()
    {
        Assert.Throws<InvalidDataException>(() => DrawingFileCodec.Read(new("empty", [])));
        Assert.Throws<InvalidDataException>(() => DrawingFileCodec.Read(new("corrupt.json", [123, 255])));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DrawingFileCodec.Read(new("a.json", [123, 125]), cancellation.Token));
    }

    [Theory]
    [InlineData(VisioPackageKind.Drawing, "application/vnd.ms-visio.drawing")]
    [InlineData(VisioPackageKind.Stencil, "application/vnd.ms-visio.stencil")]
    [InlineData(VisioPackageKind.Template, "application/vnd.ms-visio.template")]
    public void ExportMimeTypesMatchTheirPackageKind(VisioPackageKind kind, string expected) => Assert.Equal(expected, DrawingFileCodec.ContentType(kind));

    [Fact] public void CustomPortsResolveThroughTheFullAffineTransform()
    {
        var shape = new Shape { X = 100, Y = 130, Width = 160, Height = 80, Rotation = 35, ShearX = .3, FlipX = true };
        shape.ConnectionPoints.Add(new() { Id = "p", Position = new(.25, .7), Direction = new(1, 0) });
        var endpoint = ConnectionEndpoints.Resolve(shape, "p", PortSide.Auto, default, new(400, 100));
        Assert.True(endpoint.Position.Distance(shape.WorldMatrix.Map(new PointD(.25, .7))) < 1e-8);
        Assert.True(endpoint.Direction.Distance(shape.WorldMatrix.MapVector(new(1, 0)).Normalized) < 1e-8);
    }

    [Fact] public void PortHitTestingHonorsDirectionAndLocks()
    {
        var page = new DiagramPage(); var shape = new Shape { Width = 200, Height = 200 }; page.Shapes.Add(shape);
        shape.ConnectionPoints.Add(new() { Id = "p", Position = new(.25, .25), Incoming = true, Outgoing = false });
        Assert.Equal("p", ConnectionEndpoints.Hit(page, new(50, 50), 5, false)?.PointId);
        Assert.Null(ConnectionEndpoints.Hit(page, new(50, 50), 5, true));
        shape.Locked = true; Assert.Null(ConnectionEndpoints.Hit(page, new(50, 50), 5, false));
    }

    [Fact] public void ReattachingAndDetachingIsOneUndoableTransaction()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "a" }); session.AddShape(new() { Id = "b", X = 400 });
        var point = session.AddConnectionPoint("b", new(472, 32));
        session.Execute("Add connector", () => session.Page.Connectors.Add(new() { Id = "edge", SourceId = "a", End = new(300, 200) }));
        session.ReattachConnector("edge", false, "b", PortSide.Auto, point.Id, new(472, 32));
        Assert.Equal(point.Id, session.Page.Connectors[0].TargetPointId);
        session.Undo(); Assert.Null(session.Page.Connectors[0].TargetId);
        session.Redo(); Assert.Equal("b", session.Page.Connectors[0].TargetId);
        session.ReattachConnector("edge", false, null, PortSide.Auto, null, new(600, 350));
        Assert.Null(session.Page.Connectors[0].TargetPointId); Assert.Equal(new PointD(600, 350), session.Page.Connectors[0].End);
    }

    [Fact] public void LockedSemanticEditsAndWrongPortDirectionAreRejected()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "shape" }); session.SetFormula("shape", "Width", "2 in");
        session.Page.Find("shape")!.Locked = true;
        Assert.Throws<InvalidOperationException>(() => session.RemoveFormula("shape", "Width"));
        Assert.Throws<InvalidOperationException>(() => session.AddConnectionPoint("shape", new(10, 10)));
        session.Page.Find("shape")!.Locked = false;
        var point = session.AddConnectionPoint("shape", new(72, 32)); point.Incoming = false;
        session.Execute("Connector", () => session.Page.Connectors.Add(new() { Id = "e" }));
        Assert.Throws<InvalidOperationException>(() => session.ReattachConnector("e", false, "shape", PortSide.Auto, point.Id, default));
        session.Page.Layers[0].Locked = true;
        Assert.Throws<InvalidOperationException>(() => session.SetWaypoints("e", [new(20, 30)]));
    }

    [Theory] [InlineData(ConnectorKind.Straight)] [InlineData(ConnectorKind.Orthogonal)]
    public void WaypointRoutesPassThroughEveryControlPoint(ConnectorKind kind)
    {
        var edge = new Connector { Start = new(0, 0), End = new(300, 250), Kind = kind, Waypoints = [new(90, 70), new(240, 110)] };
        var route = new OrthogonalRouter().Route(new(), edge);
        foreach (var waypoint in edge.Waypoints)
            Assert.Contains(Enumerable.Range(1, route.Points.Count - 1), i => PointD.DistanceToSegment(waypoint, route.Points[i - 1], route.Points[i]) < 1e-6);
    }

    [Fact] public void JumpOwnershipAndBudgetAreDeterministic()
    {
        var a = new Connector { Id = "a", LineJumps = LineJumpStyle.Arc }; var b = new Connector { Id = "b", LineJumps = LineJumpStyle.Arc };
        var routes = new Dictionary<string, RouteResult> { ["a"] = new([new(0, 50), new(100, 50)], true), ["b"] = new([new(50, 0), new(50, 100)], true) };
        var result = LineJumpService.Analyze([a, b], routes);
        Assert.Empty(result.Jumps["a"]); Assert.Equal(new PointD(50, 50), Assert.Single(result.Jumps["b"]).Point);
        var c = new Connector { Id = "c", LineJumps = LineJumpStyle.Arc }; routes["c"] = new([new(30, 0), new(30, 100)], true);
        Assert.True(LineJumpService.Analyze([a, b, c], routes, 1).BudgetExceeded);
    }

    [Theory] [InlineData(LineJumpStyle.Arc)] [InlineData(LineJumpStyle.Gap)] [InlineData(LineJumpStyle.Square)]
    public void EachJumpStyleProducesActualVectorGeometry(LineJumpStyle style)
    {
        var edge = new Connector { LineJumps = style }; var route = new RouteResult([new(0, 50), new(100, 50)], true);
        using var path = SceneRenderer.ConnectorPath(edge, route, [new(1, new(50, 50), 6)]);
        var data = path.ToSvgPathData(); Assert.NotEqual("M0 50L100 50", data);
        if (style != LineJumpStyle.Gap) Assert.True(path.Bounds.Top < 50);
        else Assert.True(data.Count(c => c == 'M') >= 2);
    }

    [Fact] public void SharedEndpointsDoNotCreateJumps()
    {
        var a = new Connector { Id = "a", LineJumps = LineJumpStyle.Arc }; var b = new Connector { Id = "b", LineJumps = LineJumpStyle.Arc };
        var routes = new Dictionary<string, RouteResult> { ["a"] = new([new(0, 0), new(100, 0)], true), ["b"] = new([new(100, 0), new(100, 100)], true) };
        Assert.Empty(LineJumpService.Analyze([a, b], routes).Jumps["b"]);
    }

    [Fact] public void ConnectorLabelUsesArcLengthAndItsEditableOffset()
    {
        var edge = new Connector { LabelPosition = .75, LabelOffset = new(10, -20) };
        var route = new RouteResult([new(0, 0), new(100, 0), new(100, 100)], true);
        Assert.Equal(new PointD(110, 30), LineJumpService.LabelPoint(edge, route));
    }

    private static Shape Triangle(bool filled = true) => new()
    {
        X = 50, Y = 80, Width = 200, Height = 120, Text = "", ShearX = .4, Rotation = 23, FlipX = true,
        Geometry = [new() { Filled = filled, Segments = [new() { Verb = GeometryVerb.Move, End = new(0, 0) }, new() { Verb = GeometryVerb.Line, End = new(1, 0) }, new() { Verb = GeometryVerb.Line, End = new(.5, 1) }, new() { Verb = GeometryVerb.Close }] }]
    };

    [Fact] public void AffineCustomGeometryHitTestingRejectsUnfilledInteriors()
    {
        var shape = Triangle(); var center = shape.WorldMatrix.Map(new PointD(.5, .4));
        Assert.True(ShapeGeometry.Contains(shape, center));
        Assert.False(ShapeGeometry.Contains(shape, shape.WorldMatrix.Map(new PointD(.03, .95))));
        shape.Geometry[0].Filled = false; Assert.False(ShapeGeometry.Contains(shape, center));
        Assert.True(ShapeGeometry.Contains(shape, shape.WorldMatrix.Map(new PointD(.5, 0)), 2));
    }

    [Fact] public void RichTextShapesMixedDirectionAndProducesOutlinedGlyphs()
    {
        using var layoutEngine = new RichTextLayoutEngine();
        var shape = new Shape { Width = 400, Height = 200, Text = "office العربية 123\nBold and blue" };
        RichTextOperations.Format(shape, shape.Text.IndexOf("Bold", StringComparison.Ordinal), 4, s => { s.Bold = true; s.Color = "#0000FF"; });
        var layout = layoutEngine.Layout(shape);
        Assert.True(layout.GlyphCount > 5); Assert.True(layout.LineCount >= 2);
        Assert.Contains(layout.GetOutlines(), p => p.Color.Blue == 255 && p.Color.Red == 0);
        Assert.InRange(layout.HitTest(new(30, 40)), 0, shape.Text.Length);
    }

    [Fact] public void RichTextCacheIsIndependentOfShapeTranslationAndInvalidatesContent()
    {
        using var engine = new RichTextLayoutEngine(capacity: 2); var shape = new Shape { Text = "Cache me" };
        var first = engine.Layout(shape); shape.X += 300; shape.Y += 100;
        Assert.Same(first, engine.Layout(shape)); shape.Text = "Changed";
        Assert.NotSame(first, engine.Layout(shape));
        engine.Clear(); Assert.NotSame(first, engine.Layout(shape));
    }

    [Fact] public void SvgExportsAffineFiguresAndOutlinedRichText()
    {
        var shape = Triangle(); shape.Text = "Vector label"; shape.TextRotation = 15;
        var page = new DiagramPage(); page.Shapes.Add(shape);
        using var renderer = new SceneRenderer(); var xml = XDocument.Parse(renderer.ExportSvg(page)); XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.Contains(xml.Descendants(ns + "g"), g => ((string?)g.Attribute("transform"))?.StartsWith("matrix(") == true);
        Assert.Contains(xml.Descendants(ns + "g"), g => (string?)g.Attribute("aria-label") == "Vector label");
        Assert.NotEmpty(xml.Descendants(ns + "clipPath")); Assert.True(xml.Descendants(ns + "path").Count() > 1);
    }

    [Fact] public void BackgroundPagesAppearInPngAndSvgWithoutBecomingExtraPdfPages()
    {
        var document = new DiagramDocument(); var foreground = document.Pages[0]; foreground.Width = foreground.Height = 200;
        var background = new DiagramPage { IsBackground = true, Width = 200, Height = 200 };
        background.Shapes.Add(new() { Id = "watermark", X = 20, Y = 20, Width = 80, Height = 80, Text = "", Style = new() { Fill = "#FF0000", StrokeWidth = 0 } });
        document.Pages.Add(background); foreground.BackgroundPageId = background.Id;
        using var renderer = new SceneRenderer(); using var bitmap = SKBitmap.Decode(renderer.ExportPng(document, foreground, 1));
        Assert.Equal(SKColors.Red, bitmap.GetPixel(50, 50));
        Assert.Contains("shape-watermark", renderer.ExportSvg(document, foreground));
        var pdf = Encoding.ASCII.GetString(renderer.ExportPdf(document));
        Assert.Contains("/Count 1", pdf);
    }

    [Fact] public void EmbeddedPngIsIncludedInVectorExport()
    {
        using var image = SKSurface.Create(new SKImageInfo(2, 2)); image.Canvas.Clear(SKColors.Green); using var snapshot = image.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        var page = new DiagramPage(); page.Shapes.Add(new() { ImageData = data.ToArray(), ImageContentType = "image/png", Text = "" });
        using var renderer = new SceneRenderer(); Assert.Contains("data:image/png;base64,", renderer.ExportSvg(page));
    }

    [Fact] public void CommentRepliesAndResolutionAreUndoableAndRoundTrip()
    {
        var session = new EditorSession(); session.AddShape(new() { Id = "s" }); session.AddComment("s", "Author", "Review this");
        var thread = Assert.Single(session.Page.Find("s")!.Threads); session.AddComment("s", "Reviewer", "Looks good", thread.Id);
        session.ResolveComment("s", thread.Id, true); Assert.True(session.Page.Find("s")!.Threads[0].Resolved);
        session.Undo(); Assert.False(session.Page.Find("s")!.Threads[0].Resolved);
        var doc = DocumentCodec.Load(DocumentCodec.Save(session.Document)); Assert.Equal(2, doc.Pages[0].Find("s")!.Threads[0].Messages.Count);
        Assert.Throws<ArgumentException>(() => session.AddComment("s", "A", "B", "missing"));
    }
}
