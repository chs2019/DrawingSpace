using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly Dictionary<string, (byte[] Source, SKImage Image)> _images = [];
    private const long ImageCacheByteLimit = 64L * 1024 * 1024;
    private long _imageCacheBytes;
    private void DrawImage(SKCanvas canvas, Shape shape)
    {
        if (shape.ImageData is not { Length: > 0 } bytes) return;
        if (!_images.TryGetValue(shape.Id, out var entry) || !ReferenceEquals(entry.Source, bytes))
        {
            if (entry.Image is not null) { _imageCacheBytes -= (long)entry.Image.Width * entry.Image.Height * 4; entry.Image.Dispose(); _images.Remove(shape.Id); }
            using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
            if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1 || (long)codec.Info.Width * codec.Info.Height > 16_000_000) return;
            var estimate = (long)codec.Info.Width * codec.Info.Height * 4;
            while ((_imageCacheBytes + estimate > ImageCacheByteLimit || _images.Count >= 32) && _images.Count > 0)
            {
                var oldest = _images.First(); _imageCacheBytes -= (long)oldest.Value.Image.Width * oldest.Value.Image.Height * 4;
                oldest.Value.Image.Dispose(); _images.Remove(oldest.Key);
            }
            using var bitmap = SKBitmap.Decode(codec); if (bitmap is null) return;
            entry = (bytes, SKImage.FromBitmap(bitmap)); _images[shape.Id] = entry; _imageCacheBytes += estimate;
        }
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Round(255 * shape.Style.Opacity)) };
        canvas.DrawImage(entry.Image, ShapeGeometry.Rect(shape.Bounds), new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    public void DrawDocumentPage(SKCanvas canvas, DiagramDocument document, DiagramPage page, long revision, bool grid = false, RectD? visible = null, bool printing = false)
    {
        var stack = new List<DiagramPage>(); var current = page; var seen = new HashSet<string>();
        while (seen.Add(current.Id))
        {
            stack.Add(current);
            if (current.BackgroundPageId is not { } id || document.Pages.FirstOrDefault(p => p.Id == id) is not { } back) break;
            current = back;
        }
        using var background = new SKPaint { Color = SKColor.Parse(page.Background) };
        canvas.DrawRect(ShapeGeometry.Rect(page.Bounds), background);
        for (var i = stack.Count - 1; i >= 0; i--)
            DrawPage(canvas, stack[i], revision, i == 0 && grid, visible, printing, drawBackground: false);
    }

    public static SKPath ConnectorPath(Connector edge, RouteResult route, IReadOnlyList<LineJump>? jumps)
    {
        if (edge.LineJumps == LineJumpStyle.None || jumps is null || jumps.Count == 0) return Polyline(route.Points);
        var path = new SKPath(); if (route.Points.Count == 0) return path;
        void Move(PointD p) => path.MoveTo((float)p.X, (float)p.Y);
        void Line(PointD p) => path.LineTo((float)p.X, (float)p.Y);
        void Cubic(PointD a, PointD b, PointD c) => path.CubicTo((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, (float)c.X, (float)c.Y);
        Move(route.Points[0]);
        for (var index = 1; index < route.Points.Count; index++)
        {
            var a = route.Points[index - 1]; var b = route.Points[index]; var direction = (b - a).Normalized;
            var normal = new PointD(direction.Y, -direction.X);
            if (normal.Y > 0 || Math.Abs(normal.Y) < 1e-8 && normal.X < 0) normal *= -1;
            foreach (var jump in jumps.Where(j => j.Segment == index).OrderBy(j => a.Distance(j.Point)))
            {
                var before = jump.Point - direction * jump.Radius; var after = jump.Point + direction * jump.Radius;
                var top = jump.Point + normal * jump.Radius; var k = jump.Radius * .5522847498307936;
                Line(before);
                switch (edge.LineJumps)
                {
                    case LineJumpStyle.Gap: Move(after); break;
                    case LineJumpStyle.Square: Line(before + normal * jump.Radius); Line(after + normal * jump.Radius); Line(after); break;
                    default: Cubic(before + normal * k, top - direction * k, top); Cubic(top + direction * k, after + normal * k, after); break;
                }
            }
            Line(b);
        }
        return path;
    }
}
