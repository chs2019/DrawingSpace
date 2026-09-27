using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed record SnapResult(PointD Delta, double? GuideX, double? GuideY);

public static class SnapService
{
    public static SnapResult Snap(DiagramPage page, IReadOnlyCollection<string> moving, RectD original, PointD delta, double zoom, bool grid, bool dynamic, double spacing = 8)
    {
        var translated = original.Translate(delta);
        var dx = grid && spacing > 0 ? Math.Round(translated.X / spacing) * spacing - translated.X : 0;
        var dy = grid && spacing > 0 ? Math.Round(translated.Y / spacing) * spacing - translated.Y : 0;
        double? gx = null, gy = null;
        if (dynamic)
        {
            var candidates = page.Shapes.Where(s => !moving.Contains(s.Id) && page.IsVisible(s.LayerId)).Select(s => s.WorldBounds).Append(page.Bounds).ToArray();
            var threshold = 6 / Math.Max(Viewport.MinimumZoom, zoom);
            var bestX = threshold; var bestY = threshold;
            foreach (var b in candidates)
            {
                foreach (var target in new[] { b.Left, b.Center.X, b.Right })
                    foreach (var source in new[] { translated.Left, translated.Center.X, translated.Right })
                        if (Math.Abs(target - source) < bestX) { bestX = Math.Abs(target - source); dx = target - source; gx = target; }
                foreach (var target in new[] { b.Top, b.Center.Y, b.Bottom })
                    foreach (var source in new[] { translated.Top, translated.Center.Y, translated.Bottom })
                        if (Math.Abs(target - source) < bestY) { bestY = Math.Abs(target - source); dy = target - source; gy = target; }
            }
        }
        return new(delta + new PointD(dx, dy), gx, gy);
    }
}
