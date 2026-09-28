using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using SkiaSharp;

namespace DrawingSpace.Tests;

public sealed class SpatialRenderingTests
{
    private static DiagramPage Page()
    {
        var page = new DiagramPage();
        for (var i = 0; i < 140; i++) page.Shapes.Add(new() { Id = "s" + i, X = i % 14 * 48, Y = i / 14 * 64, Width = 50, Height = 32, Text = "" });
        page.Shapes[0].Kind = ShapeKind.Container; page.Shapes[1].Rotation = 32; return page;
    }

    [Fact] public void IndexedHitsPreservePainterOrderAndMatchUncachedExactGeometry()
    {
        var page = Page(); using var renderer = new SceneRenderer(); var random = new Random(873);
        for (var i = 0; i < 300; i++)
        {
            var point = new PointD(random.Next(700), random.Next(700));
            Assert.Same(renderer.HitShape(page, point, 3), renderer.HitShape(page, point, 3, 0));
        }
        page.Shapes[1].X = 450;
        Assert.Same(renderer.HitShape(page, new(460, 10)), renderer.HitShape(page, new(460, 10), revision: 1));
    }

    [Fact] public void IndexedCullingDoesNotChangeVisiblePixels()
    {
        var page = Page(); var visible = new RectD(20, 20, 360, 300);
        using var a = SKSurface.Create(new SKImageInfo(420, 360)); using var b = SKSurface.Create(new SKImageInfo(420, 360));
        using var renderer = new SceneRenderer();
        a.Canvas.ClipRect(ShapeGeometry.Rect(visible)); b.Canvas.ClipRect(ShapeGeometry.Rect(visible));
        renderer.DrawPage(a.Canvas, page, 0); renderer.DrawPage(b.Canvas, page, 0, visible: visible);
        using var ia = a.Snapshot(); using var ib = b.Snapshot();
        using var da = ia.Encode(); using var db = ib.Encode(); Assert.Equal(da.ToArray(), db.ToArray());
    }
}
