using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class PrintLayerRoutingTests
{
    [Fact]
    public void HiddenPrintLayerDoesNotCreatePhantomJumpInRasterExport()
    {
        var page = new DiagramPage { Width = 240, Height = 200 };
        page.Layers.Add(new() { Id = "reference", Name = "Reference", Printable = false });
        page.Connectors.Add(new() { Id = "reference-line", LayerId = "reference", Kind = ConnectorKind.Straight, Start = new(120, 20), End = new(120, 180), EndArrow = ArrowHead.None });
        page.Connectors.Add(new() { Id = "visible-line", Kind = ConnectorKind.Straight, Start = new(20, 100), End = new(220, 100), EndArrow = ArrowHead.None, LineJumps = LineJumpStyle.Arc, JumpSize = 10 });
        using var renderer = new SceneRenderer();
        var withHidden = renderer.ExportPng(page, 1);
        page.Connectors.RemoveAt(0);
        var withoutHidden = renderer.ExportPng(page, 1);
        Assert.Equal(withoutHidden, withHidden);
    }
}
