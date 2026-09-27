using DrawingSpace.Skia;
using SkiaSharp;

namespace DrawingSpace.Editor;

public sealed partial class DiagramSurface
{
    private void Paint(SKCanvas canvas, Size area)
    {
        using var background = new SKPaint { Color = SKColor.Parse("#E9E9E9") };
        canvas.DrawRect(0, 0, (float)area.Width, (float)area.Height, background);
        if (Session is not { } session) return;
        var viewport = session.Viewport; var page = session.Page;
        canvas.Save();
        var gutter = session.RulersVisible ? 22 : 0;
        canvas.ClipRect(new(gutter, gutter, (float)area.Width, (float)area.Height));
        canvas.Translate((float)viewport.Pan.X, (float)viewport.Pan.Y); canvas.Scale((float)viewport.Zoom);
        background.Color = SKColor.Parse("#CACACA");
        canvas.DrawRect(4 / (float)viewport.Zoom, 4 / (float)viewport.Zoom, (float)page.Width, (float)page.Height, background);
        var visible = RectD.FromPoints(viewport.ToWorld(new(gutter, gutter)), viewport.ToWorld(new(area.Width, area.Height)));
        Renderer.DrawDocumentPage(canvas, session.Document, page, session.Revision, session.GridVisible && viewport.Zoom >= .35, visible);
        if (_ghost is not null) { _ghost.Style.Opacity = .6; Renderer.DrawShape(canvas, _ghost); }
        if (_gesture == Gesture.Connect)
        {
            var start = page.Find(_connectSource)?.Port(_connectPort) ?? _startWorld;
            using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#2B579A"), Style = SKPaintStyle.Stroke, StrokeWidth = (float)(1.5 / viewport.Zoom) };
            canvas.DrawLine((float)start.X, (float)start.Y, (float)_lastWorld.X, (float)_lastWorld.Y, paint);
            var target = Renderer.HitShape(page, _lastWorld, 8 / viewport.Zoom);
            if (target is not null)
                foreach (var side in new[] { PortSide.North, PortSide.East, PortSide.South, PortSide.West })
                { var port = target.Port(side); canvas.DrawCircle((float)port.X, (float)port.Y, (float)(4 / viewport.Zoom), paint); }
        }
        canvas.Restore();
        using var outline = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#5B9BD5"), Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f };
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        foreach (var shape in session.SelectedShapes.Where(s => page.IsVisible(s.LayerId)))
        {
            var corners = shape.WorldCorners.Select(viewport.ToScreen).ToArray();
            using var path = SceneRenderer.Polyline(corners.Append(corners[0]).ToArray()); canvas.DrawPath(path, outline);
            if (session.Page.IsLocked(shape)) continue;
            foreach (var handle in ShapeTransforms.Handles(shape))
            {
                var p = viewport.ToScreen(handle); var box = new SKRect((float)p.X - 3.5f, (float)p.Y - 3.5f, (float)p.X + 3.5f, (float)p.Y + 3.5f);
                canvas.DrawRect(box, white); canvas.DrawRect(box, outline);
            }
            if (session.SelectedShapes.Count == 1)
            {
                var rotation = RotationHandle(shape); var top = viewport.ToScreen(ShapeTransforms.Handles(shape)[1]);
                canvas.DrawLine((float)top.X, (float)top.Y, (float)rotation.X, (float)rotation.Y, outline);
                canvas.DrawCircle((float)rotation.X, (float)rotation.Y, 4, white); canvas.DrawCircle((float)rotation.X, (float)rotation.Y, 4, outline);
                if (session.AutoConnect && session.Tool == EditorTool.Pointer)
                {
                    using var accent = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#5B9BD5") };
                    foreach (var side in new[] { PortSide.North, PortSide.East, PortSide.South, PortSide.West })
                    {
                        var p = AutoConnectPosition(shape, side); var direction = DrawingSpace.Routing.OrthogonalRouter.Direction(side).Rotate(shape.Rotation, PointD.Zero); var normal = new PointD(-direction.Y, direction.X);
                        var tip = p + direction * 5; var a = p - direction * 4 + normal * 5; var b = p - direction * 4 - normal * 5;
                        using var triangle = SceneRenderer.Polyline([a, tip, b, a]); canvas.DrawPath(triangle, accent);
                    }
                }
            }
        }
        var routes = Renderer.Routes(page, session.Revision);
        foreach (var connector in session.SelectedConnectors)
        {
            if (!routes.TryGetValue(connector.Id, out var route)) continue;
            var points = route.Points.Select(viewport.ToScreen).ToArray();
            using var path = SceneRenderer.Polyline(points); outline.StrokeWidth = 3; outline.Color = SKColor.Parse("#AA5B9BD5"); canvas.DrawPath(path, outline); outline.StrokeWidth = 1.3f; outline.Color = SKColor.Parse("#5B9BD5");
            if (points.Length == 0) continue;
            foreach (var p in new[] { points[0], points[^1] }) { canvas.DrawCircle((float)p.X, (float)p.Y, 4, white); canvas.DrawCircle((float)p.X, (float)p.Y, 4, outline); }
        }
        DrawConnectorAdorners(canvas);
        if (_marquee is { } marquee)
        {
            var a = viewport.ToScreen(new(marquee.Left, marquee.Top)); var b = viewport.ToScreen(new(marquee.Right, marquee.Bottom));
            var rect = new SKRect((float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
            white.Color = SKColor.Parse("#205B9BD5"); canvas.DrawRect(rect, white); canvas.DrawRect(rect, outline);
        }
        if (_snap is not null)
        {
            outline.Color = SKColor.Parse("#D061A9"); outline.StrokeWidth = 1;
            if (_snap.GuideX is { } x) { var p = viewport.ToScreen(new(x, 0)); canvas.DrawLine((float)p.X, gutter, (float)p.X, (float)area.Height, outline); }
            if (_snap.GuideY is { } y) { var p = viewport.ToScreen(new(0, y)); canvas.DrawLine(gutter, (float)p.Y, (float)area.Width, (float)p.Y, outline); }
        }
        if (session.RulersVisible) DrawRulers(canvas, area);
    }
    private void DrawRulers(SKCanvas canvas, Size area)
    {
        if (Session is not { } session) return;
        using var paint = new SKPaint { Color = SKColor.Parse("#F8F8F8"), IsAntialias = true };
        canvas.DrawRect(0, 0, (float)area.Width, 22, paint); canvas.DrawRect(0, 0, 22, (float)area.Height, paint);
        paint.Color = SKColor.Parse("#B7B7B7"); paint.StrokeWidth = 1;
        canvas.DrawLine(22, 21.5f, (float)area.Width, 21.5f, paint); canvas.DrawLine(21.5f, 22, 21.5f, (float)area.Height, paint);
        using var font = new SKFont(SKTypeface.Default, 10) { Edging = SKFontEdging.Antialias };
        var zoom = session.Viewport.Zoom; var step = zoom < .35 ? 96 : zoom < .7 ? 48 : 24;
        var left = session.Viewport.ToWorld(new(22, 22)); var right = session.Viewport.ToWorld(new(area.Width, area.Height));
        paint.Color = SKColor.Parse("#777777");
        for (var x = Math.Ceiling(left.X / step) * step; x <= right.X; x += step)
        {
            var px = (float)session.Viewport.ToScreen(new(x, 0)).X; var major = Math.Abs(x % 96) < 1e-7;
            canvas.DrawLine(px, major ? 11 : 16, px, 21, paint);
            if (major) canvas.DrawText((x / 96).ToString("0", System.Globalization.CultureInfo.InvariantCulture), px + 3, 10, font, paint);
        }
        for (var y = Math.Ceiling(left.Y / step) * step; y <= right.Y; y += step)
        {
            var py = (float)session.Viewport.ToScreen(new(0, y)).Y; var major = Math.Abs(y % 96) < 1e-7;
            canvas.DrawLine(major ? 11 : 16, py, 21, py, paint);
            if (major) canvas.DrawText((y / 96).ToString("0", System.Globalization.CultureInfo.InvariantCulture), 2, py - 2, font, paint);
        }
        paint.Color = SKColor.Parse("#E5E5E5"); canvas.DrawRect(0, 0, 21, 21, paint);
    }
}
