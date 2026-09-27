using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Skia;
using DrawingSpace.Stencils;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace DrawingSpace.Controls;

public sealed class StencilPreview : SKCanvasElement
{
    public StencilMaster? Master { get; set; }
    public StencilPreview() { Width = 68; Height = 42; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (Master is null) return;
        var shape = Master.Create(new(Master.Width / 2, Master.Height / 2));
        var scale = (float)Math.Min((area.Width - 12) / shape.Width, (area.Height - 8) / shape.Height);
        canvas.Save(); canvas.Translate((float)(area.Width - shape.Width * scale) / 2, (float)(area.Height - shape.Height * scale) / 2); canvas.Scale(scale);
        using var path = ShapeGeometry.Create(shape); using var details = ShapeGeometry.Details(shape);
        using var fill = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#F8FAFD") };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColor.Parse("#426385"), StrokeWidth = 1.3f / scale };
        canvas.DrawPath(path, fill); canvas.DrawPath(path, stroke); canvas.DrawPath(details, stroke);
        if (shape.Kind == ShapeKind.Text) { using var font = new SKFont(SKTypeface.Default, 38); fill.Color = stroke.Color; canvas.DrawText("Text", 6, 35, font, fill); }
        canvas.Restore();
    }
}
