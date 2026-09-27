using System.Text;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using DrawingSpace.Stencils;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class RenderingTests
{
    [Fact] public void EveryMasterHasRenderableGeometry()
    {
        using var renderer = new SceneRenderer(); using var surface = SKSurface.Create(new SKImageInfo(1024, 1024));
        foreach (var master in StencilCatalog.All.SelectMany(s => s.Masters))
        {
            var shape = master.Create(new(512, 512)); using var path = ShapeGeometry.Create(shape);
            if (shape.Kind != ShapeKind.Text) Assert.False(path.IsEmpty);
            renderer.DrawShape(surface.Canvas, shape);
        }
    }
    [Fact] public void DiamondHitTestRejectsEmptyCorners()
    {
        var shape = new Shape { Kind = ShapeKind.Decision, Width = 100, Height = 100 };
        Assert.True(ShapeGeometry.Contains(shape, new(50, 50)));
        Assert.False(ShapeGeometry.Contains(shape, new(2, 2)));
    }
    [Fact] public void RotatedHitTestUsesLocalGeometry()
    {
        var shape = new Shape { X = 100, Y = 100, Width = 140, Height = 50, Rotation = 45 };
        Assert.True(ShapeGeometry.Contains(shape, shape.Bounds.Center));
        Assert.False(ShapeGeometry.Contains(shape, new(100, 100)));
    }
    [Fact] public void SvgEscapesLabelsAndUsesRealVectorPaths()
    {
        var page = new DiagramPage(); page.Shapes.Add(new() { Text = "<script>&\"text\"", Kind = ShapeKind.Decision, Width = 300, Height = 150 });
        using var renderer = new SceneRenderer(); var svg = renderer.ExportSvg(page); var xml = XDocument.Parse(svg);
        XNamespace ns = "http://www.w3.org/2000/svg";
        Assert.NotEmpty(xml.Descendants(ns + "path")); Assert.Empty(xml.Descendants(ns + "script")); Assert.Contains("&lt;script&gt;", svg);
    }
    [Fact] public void ExportRespectsHiddenAndNonPrintableLayers()
    {
        var page = new DiagramPage(); page.Layers.Add(new() { Id = "hidden", Visible = false }); page.Layers.Add(new() { Id = "notes", Printable = false });
        page.Shapes.Add(new() { Id = "visible" }); page.Shapes.Add(new() { Id = "hidden-shape", LayerId = "hidden" }); page.Shapes.Add(new() { Id = "nonprint", LayerId = "notes" });
        using var renderer = new SceneRenderer(); var svg = renderer.ExportSvg(page);
        Assert.Contains("shape-visible", svg); Assert.DoesNotContain("shape-hidden-shape", svg); Assert.DoesNotContain("shape-nonprint", svg);
    }
    [Fact] public void PngExportHasRequestedSize()
    {
        using var renderer = new SceneRenderer(); var png = renderer.ExportPng(new() { Width = 200, Height = 100 }, 2);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());
        using var bitmap = SKBitmap.Decode(png); Assert.Equal(400, bitmap.Width); Assert.Equal(200, bitmap.Height);
    }
    [Fact] public void PngExportRejectsUnsafeAllocation()
    {
        using var renderer = new SceneRenderer(); Assert.Throws<InvalidOperationException>(() => renderer.ExportPng(new() { Width = 100000, Height = 100000 }));
    }
    [Fact] public void PdfExportWritesMultiplePages()
    {
        var doc = SampleDiagrams.Flowchart(); doc.Pages.Add(new() { Name = "Second page" });
        using var renderer = new SceneRenderer(); var pdf = renderer.ExportPdf(doc);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf.Take(8).ToArray())); Assert.True(pdf.Length > 5000);
    }
    [Fact] public void TextWrappingPreservesGraphemeClusters()
    {
        using var font = new SKFont(SKTypeface.Default, 14);
        var lines = TextLayout.Wrap("A\u0301B\u0301C\u0301D\u0301", font, 12, 20);
        Assert.Equal("A\u0301B\u0301C\u0301D\u0301", string.Concat(lines)); Assert.All(lines, line => Assert.False(line.StartsWith('\u0301')));
    }
    [Fact] public void RendererRouteCacheInvalidatesByRevision()
    {
        var page = new DiagramPage(); var a = new Shape { Id = "a" }; page.Shapes.Add(a); page.Shapes.Add(new() { Id = "b", X = 400 }); page.Connectors.Add(new() { Id = "edge", SourceId = "a", TargetId = "b" });
        using var renderer = new SceneRenderer(); var first = renderer.Routes(page, 1)["edge"].Points[0]; a.Y = 200;
        var second = renderer.Routes(page, 2)["edge"].Points[0]; Assert.NotEqual(first, second);
    }
}
