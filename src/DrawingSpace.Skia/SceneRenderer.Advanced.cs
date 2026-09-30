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
}
