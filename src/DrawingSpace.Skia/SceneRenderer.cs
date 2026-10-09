using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using SkiaSharp;
using DrawingSpace.Text;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer : IDisposable
{
    private static readonly int GridRasterMajor = 100;
    private static readonly int GridRasterMinor = GridRasterMajor / 5;
    private readonly OrthogonalRouter _router = new();
    private RichTextLayoutEngine? _richText;
    private IReadOnlyDictionary<string, IReadOnlyList<LineJump>> _jumps = new Dictionary<string, IReadOnlyList<LineJump>>();
    public bool LineJumpBudgetExceeded { get; private set; }
    private RichTextLayoutEngine TextEngine => _richText ??= new(style => _fallbackTypeface is null ? null : _fallbackStyles.GetValueOrDefault((style.Bold, style.Italic), _fallbackTypeface));
    private readonly Dictionary<string, RouteResult> _routes = [];
    private readonly Dictionary<string, (SKTypeface Typeface, SKFont Font)> _fonts = [];
    private DiagramPage? _routePage;
    private bool _routePrinting;
    private long _revision = -1;
    private SKTypeface? _fallbackTypeface;
    private readonly Dictionary<(bool Bold, bool Italic), SKTypeface> _fallbackStyles = [];
    private void SetTypeface(SKTypeface typeface)
    {
        _richText?.Dispose(); _richText = null;
        foreach (var entry in _fonts.Values) { entry.Font.Dispose(); entry.Typeface.Dispose(); }
        _fonts.Clear(); _fallbackTypeface?.Dispose(); _fallbackTypeface = typeface;
    }
    public void ClearCache() { _routePage = null; _routes.Clear(); _shapeIndexes.Clear(); }
    public IReadOnlyDictionary<string, RouteResult> Routes(DiagramPage page, long revision, bool printing = false)
    {
        if (!ReferenceEquals(page, _routePage) || _revision != revision || _routePrinting != printing)
            RebuildRoutes(page, revision, printing);
        return _routes;
    }

    // Capturing LINQ predicates belong only on the cold path. Keeping them in Routes
    // allocated a compiler-generated closure even when its cache was already valid.
    private void RebuildRoutes(DiagramPage page, long revision, bool printing)
    {
        _routePage = null; _routes.Clear();
        var visibleEdges = page.Connectors.Where(c => page.IsVisible(c.LayerId) && (!printing || page.IsPrintable(c.LayerId))).ToArray();
        if (visibleEdges.Length != 0)
        {
            var scene = RoutingScene.Capture(page);
            foreach (var edge in visibleEdges) _routes[edge.Id] = _router.RouteSnapshot(scene, edge);
        }
        var analysis = LineJumpService.Analyze(visibleEdges, _routes);
        _jumps = analysis.Jumps; LineJumpBudgetExceeded = analysis.BudgetExceeded;
        _revision = revision; _routePrinting = printing; _routePage = page;
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
        var font = new SKFont(ResolveTypeface(style, typeface), (float)style.FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        _fonts[key] = (typeface, font);
        return font;
    }
    public void DrawPage(SKCanvas canvas, DiagramPage page, long revision, bool grid = false, RectD? visible = null, bool printing = false, bool drawBackground = true)
    {
        using var background = new SKPaint { Color = SKColor.Parse(page.Background) };
        if (drawBackground) canvas.DrawRect(ShapeGeometry.Rect(page.Bounds), background);
        if (grid) DrawGrid(canvas, page, visible ?? page.Bounds);
        var routes = Routes(page, revision, printing);
        bool Visible(string layer) => page.IsVisible(layer) && (!printing || page.IsPrintable(layer));
        var candidates = visible is { } area && page.Shapes.Count > 128 ? VisibleShapes(page, revision, area) : null;
        void DrawShapes(bool containers)
        {
            var count = candidates?.Count ?? page.Shapes.Count;
            for (var i = 0; i < count; i++)
            {
                var shape = page.Shapes[candidates is null ? i : candidates[i]];
                if ((shape.Kind == ShapeKind.Container) != containers || !Visible(shape.LayerId)) continue;
                if (candidates is not null || visible is null || DataGraphicProjection.WorldBounds(shape).Intersects(visible.Value)) DrawShape(canvas, shape);
            }
        }
        DrawShapes(true);
        foreach (var edge in page.Connectors.Where(c => Visible(c.LayerId)))
            if (routes.TryGetValue(edge.Id, out var route)) DrawConnector(canvas, edge, route);
        DrawShapes(false);
    }
    private static void DrawGrid(SKCanvas canvas, DiagramPage page, RectD visible)
    {
        var left = Math.Max(0, visible.Left); var top = Math.Max(0, visible.Top);
        var right = Math.Min(page.Width, visible.Right); var bottom = Math.Min(page.Height, visible.Bottom);
        using var paint = new SKPaint { Color = SKColor.Parse("#EDF0F4"), StrokeWidth = .45f };
        for (var x = Math.Ceiling(left / GridRasterMinor) * GridRasterMinor; x <= right; x += GridRasterMinor) canvas.DrawLine((float)x, (float)top, (float)x, (float)bottom, paint);
        for (var y = Math.Ceiling(top / GridRasterMinor) * GridRasterMinor; y <= bottom; y += GridRasterMinor) canvas.DrawLine((float)left, (float)y, (float)right, (float)y, paint);
        paint.Color = SKColor.Parse("#E1E6ED");
        for (var x = Math.Ceiling(left / GridRasterMajor) * GridRasterMajor; x <= right; x += GridRasterMajor) canvas.DrawLine((float)x, (float)top, (float)x, (float)bottom, paint);
        for (var y = Math.Ceiling(top / GridRasterMajor) * GridRasterMajor; y <= bottom; y += GridRasterMajor) canvas.DrawLine((float)left, (float)y, (float)right, (float)y, paint);
    }
    public void DrawShape(SKCanvas canvas, Shape shape)
    {
        canvas.Save();
        canvas.Concat(ShapeGeometry.Matrix(shape.DrawingMatrix));
        using var fill = new SKPaint { IsAntialias = true, Color = Color(DataGraphicProjection.Fill(shape), shape.Style.Opacity) };
        using var dash = shape.Style.Dashed ? SKPathEffect.CreateDash([6, 4], 0) : null;
        using var stroke = new SKPaint { IsAntialias = true, Color = Color(shape.Style.Stroke, shape.Style.Opacity), Style = SKPaintStyle.Stroke, StrokeWidth = (float)shape.Style.StrokeWidth, StrokeJoin = SKStrokeJoin.Round, PathEffect = dash };
        if (shape.Geometry.Count > 0)
        {
            foreach (var figure in shape.Geometry)
            {
                using var figurePath = ShapeGeometry.CreateFigure(shape, figure);
                if (figure.Filled) canvas.DrawPath(figurePath, fill);
                if (figure.Stroked && shape.Style.StrokeWidth > 0) canvas.DrawPath(figurePath, stroke);
            }
        }
        else
        {
            using var path = ShapeGeometry.Create(shape).Snapshot();
            using var details = ShapeGeometry.Details(shape);
            if (shape.Kind != ShapeKind.Annotation) canvas.DrawPath(path, fill);
            if (shape.Style.StrokeWidth > 0) { canvas.DrawPath(path, stroke); canvas.DrawPath(details, stroke); }
        }
        DrawImage(canvas, shape);
        DrawText(canvas, shape);
        DrawDataGraphics(canvas, shape);
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
        TextEngine.Layout(shape).Paint(canvas, new(shape.X, shape.Y));
    }

    private void DrawConnector(SKCanvas canvas, Connector connector, RouteResult route)
    {
        if (route.Points.Count < 2) return;
        using var dash = connector.Dashed ? SKPathEffect.CreateDash([7, 5], 0) : null;
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(connector.Color), Style = SKPaintStyle.Stroke, StrokeWidth = (float)connector.Width, StrokeJoin = SKStrokeJoin.Round, PathEffect = dash };
        using var path = ConnectorPath(connector, route, _jumps.GetValueOrDefault(connector.Id)); canvas.DrawPath(path, paint);
        DrawArrow(canvas, route.Points[1], route.Points[0], connector.StartArrow, connector.Color, connector.Width);
        DrawArrow(canvas, route.Points[^2], route.Points[^1], connector.EndArrow, connector.Color, connector.Width);
        if (!string.IsNullOrWhiteSpace(connector.Text))
        {
            var font = Font(new() { FontSize = 12 }); var label = connector.Text.Length > 120 ? connector.Text[..120] + "…" : connector.Text;
            var position = LineJumpService.LabelPoint(connector, route); var width = font.MeasureText(label);
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
    /// <summary>Supply the current geometry revision for spatially indexed hits; omit it for an uncached query.</summary>
    public Shape? HitShape(DiagramPage page, PointD point, double tolerance = 2, long? revision = null)
    {
        if (!point.IsFinite || !double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (revision is { } value && page.Shapes.Count > 128) return HitIndexedShape(page, point, value, tolerance);
        return page.Shapes.Where(s => s.Kind != ShapeKind.Container).Reverse().Concat(page.Shapes.Where(s => s.Kind == ShapeKind.Container).Reverse())
            .FirstOrDefault(s => page.IsVisible(s.LayerId) && ShapeGeometry.Contains(s, point, tolerance));
    }
    public Connector? HitConnector(DiagramPage page, PointD point, long revision, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!point.IsFinite) throw new ArgumentOutOfRangeException(nameof(point));
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var routes = Routes(page, revision);
        for (var i = page.Connectors.Count - 1; i >= 0; i--)
        {
            var connector = page.Connectors[i];
            if (!routes.TryGetValue(connector.Id, out var route)) continue;
            for (var segment = 1; segment < route.Points.Count; segment++)
                if (PointD.DistanceToSegment(point, route.Points[segment - 1], route.Points[segment]) <= tolerance)
                    return connector;
        }
        return null;
    }
    public static SKColor Color(string hex, double opacity = 1)
    {
        var color = SKColor.Parse(hex); return color.WithAlpha((byte)Math.Round(color.Alpha * Math.Clamp(opacity, 0, 1)));
    }
    public void Dispose()
    {
        foreach (var entry in _fonts.Values) { entry.Font.Dispose(); entry.Typeface.Dispose(); }
        _richText?.Dispose(); _richText = null;
        foreach (var image in _images.Values) image.Image.Dispose(); _images.Clear();
        _fonts.Clear(); _fallbackTypeface?.Dispose(); _fallbackTypeface = null;
        foreach (var typeface in _fallbackStyles.Values.Distinct()) typeface.Dispose();
        _fallbackStyles.Clear(); _routes.Clear(); _shapeIndexes.Clear(); ClearDataGraphics();
    }
}
