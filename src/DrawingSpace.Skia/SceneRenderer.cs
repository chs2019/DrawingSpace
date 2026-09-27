using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer : IDisposable
{
    private readonly OrthogonalRouter _router = new();
    private readonly Dictionary<string, RouteResult> _routes = [];
    private readonly Dictionary<string, (SKTypeface Typeface, SKFont Font)> _fonts = [];
    private DiagramPage? _routePage;
    private long _revision = -1;
    private SKTypeface? _fallbackTypeface;
    public void SetTypeface(SKTypeface typeface)
    {
        foreach (var entry in _fonts.Values) { entry.Font.Dispose(); entry.Typeface.Dispose(); }
        _fonts.Clear(); _fallbackTypeface?.Dispose(); _fallbackTypeface = typeface;
    }
    public void ClearCache() { _routePage = null; _routes.Clear(); }
    public IReadOnlyDictionary<string, RouteResult> Routes(DiagramPage page, long revision)
    {
        if (!ReferenceEquals(page, _routePage) || _revision != revision)
        {
            _routePage = page; _revision = revision; _routes.Clear();
            foreach (var edge in page.Connectors.Where(c => page.IsVisible(c.LayerId))) _routes[edge.Id] = _router.Route(page, edge);
        }
        return _routes;
    }
    public SKFont Font(ShapeStyle style)
    {
        var key = $"{style.FontFamily}|{style.FontSize:R}|{style.Bold}|{style.Italic}";
        if (_fonts.TryGetValue(key, out var existing)) return existing.Font;
        if (_fonts.Count > 256)
        {
            foreach (var entry in _fonts.Values) { entry.Font.Dispose(); entry.Typeface.Dispose(); }
            _fonts.Clear();
        }
        var typeface = SKTypeface.FromFamilyName(style.FontFamily, style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var font = new SKFont(_fallbackTypeface ?? typeface, (float)style.FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        _fonts[key] = (typeface, font);
        return font;
    }
    public void DrawPage(SKCanvas canvas, DiagramPage page, long revision, bool grid = false, RectD? visible = null, bool printing = false)
    {
        using var background = new SKPaint { Color = SKColor.Parse(page.Background) };
        canvas.DrawRect(ShapeGeometry.Rect(page.Bounds), background);
        if (grid) DrawGrid(canvas, page, visible ?? page.Bounds);
        var routes = Routes(page, revision);
        bool Visible(string layer) => page.IsVisible(layer) && (!printing || page.IsPrintable(layer));
        foreach (var shape in page.Shapes.Where(s => s.Kind == ShapeKind.Container && Visible(s.LayerId)))
            if (visible is null || shape.WorldBounds.Intersects(visible.Value)) DrawShape(canvas, shape);
        foreach (var edge in page.Connectors.Where(c => Visible(c.LayerId)))
            if (routes.TryGetValue(edge.Id, out var route)) DrawConnector(canvas, edge, route);
        foreach (var shape in page.Shapes.Where(s => s.Kind != ShapeKind.Container && Visible(s.LayerId)))
            if (visible is null || shape.WorldBounds.Intersects(visible.Value)) DrawShape(canvas, shape);
    }
    private static void DrawGrid(SKCanvas canvas, DiagramPage page, RectD visible)
    {
        var left = Math.Max(0, visible.Left); var top = Math.Max(0, visible.Top);
        var right = Math.Min(page.Width, visible.Right); var bottom = Math.Min(page.Height, visible.Bottom);
        using var paint = new SKPaint { Color = SKColor.Parse("#EDF0F4"), StrokeWidth = .45f };
        for (var x = Math.Ceiling(left / 16) * 16; x <= right; x += 16) canvas.DrawLine((float)x, (float)top, (float)x, (float)bottom, paint);
        for (var y = Math.Ceiling(top / 16) * 16; y <= bottom; y += 16) canvas.DrawLine((float)left, (float)y, (float)right, (float)y, paint);
        paint.Color = SKColor.Parse("#E1E6ED");
        for (var x = Math.Ceiling(left / 96) * 96; x <= right; x += 96) canvas.DrawLine((float)x, (float)top, (float)x, (float)bottom, paint);
        for (var y = Math.Ceiling(top / 96) * 96; y <= bottom; y += 96) canvas.DrawLine((float)left, (float)y, (float)right, (float)y, paint);
    }
    public void DrawShape(SKCanvas canvas, Shape shape)
    {
        canvas.Save();
        var center = shape.Bounds.Center;
        canvas.RotateDegrees((float)shape.Rotation, (float)center.X, (float)center.Y);
        using var path = ShapeGeometry.Create(shape);
        using var details = ShapeGeometry.Details(shape);
        using var fill = new SKPaint { IsAntialias = true, Color = Color(shape.Style.Fill, shape.Style.Opacity) };
        using var dash = shape.Style.Dashed ? SKPathEffect.CreateDash([6, 4], 0) : null;
        using var stroke = new SKPaint { IsAntialias = true, Color = Color(shape.Style.Stroke, shape.Style.Opacity), Style = SKPaintStyle.Stroke, StrokeWidth = (float)shape.Style.StrokeWidth, StrokeJoin = SKStrokeJoin.Round, PathEffect = dash };
        if (shape.Kind != ShapeKind.Annotation) canvas.DrawPath(path, fill);
        if (shape.Style.StrokeWidth > 0) { canvas.DrawPath(path, stroke); canvas.DrawPath(details, stroke); }
        DrawText(canvas, shape);
        canvas.Restore();
    }
    internal (IReadOnlyList<string> Lines, float X, float Y, float Width, float LineHeight, bool Left) Layout(Shape shape)
    {
        var padding = shape.Kind == ShapeKind.Text ? 0f : 12f;
        var width = (float)shape.Width - padding * 2;
        var height = (float)shape.Height - padding * 2;
        var y = (float)shape.Y + padding;
        var left = shape.Kind is ShapeKind.Text or ShapeKind.Note or ShapeKind.Annotation or ShapeKind.Container;
        if (shape.Kind == ShapeKind.Decision) { width *= .68f; height *= .68f; }
        if (shape.Kind == ShapeKind.Container) { y = (float)shape.Y + 7; height = 22; }
        if (shape.Kind == ShapeKind.Cylinder) { y += (float)shape.Height * .16f; height -= (float)shape.Height * .16f; }
        if (shape.Kind == ShapeKind.Server) { y = (float)shape.Y + (float)shape.Height * .72f; height = (float)shape.Height * .25f; width = (float)shape.Width - 8; }
        var font = Font(shape.Style);
        var lineHeight = (float)shape.Style.FontSize * 1.24f;
        var maxLines = Math.Max(1, Math.Min(100, (int)(height / lineHeight)));
        var lines = TextLayout.Wrap(shape.Text, font, width, maxLines);
        if (shape.Kind is not ShapeKind.Note and not ShapeKind.Container and not ShapeKind.Server)
            y = (float)shape.Y + ((float)shape.Height - lines.Count * lineHeight) / 2;
        y -= font.Metrics.Ascent;
        var x = left ? (float)shape.X + padding : (float)shape.Bounds.Center.X;
        return (lines, x, y, width, lineHeight, left);
    }
    private void DrawText(SKCanvas canvas, Shape shape)
    {
        if (string.IsNullOrEmpty(shape.Text)) return;
        var layout = Layout(shape); var font = Font(shape.Style);
        using var paint = new SKPaint { IsAntialias = true, Color = Color(shape.Style.TextColor, shape.Style.Opacity) };
        canvas.Save(); canvas.ClipRect(ShapeGeometry.Rect(shape.Bounds.Inflate(-1)));
        var y = layout.Y;
        foreach (var line in layout.Lines)
        {
            var x = layout.Left ? layout.X : layout.X - font.MeasureText(line) / 2;
            canvas.DrawText(line, x, y, font, paint); y += layout.LineHeight;
        }
        canvas.Restore();
    }
    public void DrawConnector(SKCanvas canvas, Connector connector, RouteResult route)
    {
        if (route.Points.Count < 2) return;
        using var dash = connector.Dashed ? SKPathEffect.CreateDash([7, 5], 0) : null;
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(connector.Color), Style = SKPaintStyle.Stroke, StrokeWidth = (float)connector.Width, StrokeJoin = SKStrokeJoin.Round, PathEffect = dash };
        using var path = Polyline(route.Points); canvas.DrawPath(path, paint);
        DrawArrow(canvas, route.Points[1], route.Points[0], connector.StartArrow, connector.Color, connector.Width);
        DrawArrow(canvas, route.Points[^2], route.Points[^1], connector.EndArrow, connector.Color, connector.Width);
        if (!string.IsNullOrWhiteSpace(connector.Text))
        {
            var font = Font(new() { FontSize = 12 }); var label = connector.Text.Length > 120 ? connector.Text[..120] + "…" : connector.Text;
            var position = route.Midpoint; var width = font.MeasureText(label);
            using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
            canvas.DrawRoundRect(new((float)position.X - width / 2 - 5, (float)position.Y - 10, (float)position.X + width / 2 + 5, (float)position.Y + 10), 3, 3, fill);
            fill.Color = SKColor.Parse("#405574"); canvas.DrawText(label, (float)position.X - width / 2, (float)position.Y + 4, font, fill);
        }
    }
    public static SKPath Polyline(IReadOnlyList<PointD> points)
    {
        var path = new SKPath(); if (points.Count == 0) return path;
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        foreach (var p in points.Skip(1)) path.LineTo((float)p.X, (float)p.Y);
        return path;
    }
    public static SKPath ArrowPath(PointD from, PointD tip, ArrowHead head, double width)
    {
        var path = new SKPath(); if (head == ArrowHead.None) return path;
        var direction = (tip - from).Normalized; var normal = new PointD(-direction.Y, direction.X);
        var size = Math.Max(8, width * 4 + 3); var back = tip - direction * size;
        var a = back + normal * size * .42; var b = back - normal * size * .42;
        path.MoveTo((float)a.X, (float)a.Y); path.LineTo((float)tip.X, (float)tip.Y); path.LineTo((float)b.X, (float)b.Y);
        if (head == ArrowHead.Diamond) { var end = tip - direction * size * 2; path.LineTo((float)end.X, (float)end.Y); }
        if (head != ArrowHead.Open) path.Close();
        return path;
    }
    private static void DrawArrow(SKCanvas canvas, PointD from, PointD tip, ArrowHead head, string color, double width)
    {
        using var path = ArrowPath(from, tip, head, width);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(color), StrokeWidth = (float)width, Style = head == ArrowHead.Open ? SKPaintStyle.Stroke : SKPaintStyle.Fill };
        canvas.DrawPath(path, paint);
    }
    public Shape? HitShape(DiagramPage page, PointD point, double tolerance = 2)
    {
        return page.Shapes.Where(s => s.Kind != ShapeKind.Container).Reverse().Concat(page.Shapes.Where(s => s.Kind == ShapeKind.Container).Reverse())
            .FirstOrDefault(s => page.IsVisible(s.LayerId) && ShapeGeometry.Contains(s, point, tolerance));
    }
    public Connector? HitConnector(DiagramPage page, PointD point, long revision, double tolerance)
    {
        var routes = Routes(page, revision);
        return page.Connectors.AsEnumerable().Reverse().FirstOrDefault(c => routes.TryGetValue(c.Id, out var route) && route.Points.Zip(route.Points.Skip(1), (a, b) => PointD.DistanceToSegment(point, a, b)).Any(d => d <= tolerance));
    }
    public static SKColor Color(string hex, double opacity = 1)
    {
        var color = SKColor.Parse(hex); return color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));
    }
    public void Dispose()
    {
        foreach (var entry in _fonts.Values) { entry.Font.Dispose(); entry.Typeface.Dispose(); }
        _fonts.Clear(); _fallbackTypeface?.Dispose(); _fallbackTypeface = null; _routes.Clear();
    }
}
