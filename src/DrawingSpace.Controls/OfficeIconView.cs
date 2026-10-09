using SkiaSharp;
using Uno.WinUI.Graphics2DSK;

namespace DrawingSpace.Controls;

/// <summary>Resolution-independent, original line icons. No bitmap or proprietary Office assets.</summary>
public sealed class OfficeIconView : SKCanvasElement
{
    private OfficeIcon _icon;
    private string _color = "#3A3A3A";
    public OfficeIcon Icon { get => _icon; set { _icon = value; Invalidate(); } }
    public string Color { get => _color; set { _color = value; Invalidate(); } }
    public OfficeIconView() { Width = 20; Height = 20; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)area.Width / 24, (float)area.Height / 24);
        using var p = new SKPaint { IsAntialias = true, Color = SKColor.Parse(Color), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        void Line(float a, float b, float c, float d) => canvas.DrawLine(a, b, c, d, p);
        void Box(float x, float y, float w, float h) => canvas.DrawRect(x, y, w, h, p);
        void Path(params float[] points)
        {
            using var path = new SKPath(); path.MoveTo(points[0], points[1]);
            for (var i = 2; i < points.Length; i += 2) path.LineTo(points[i], points[i + 1]); canvas.DrawPath(path, p);
        }
        switch (Icon)
        {
            case OfficeIcon.App: Box(2, 2, 8, 6); Box(14, 16, 8, 6); Box(2, 16, 8, 6); Path(6, 8, 6, 12, 18, 12, 18, 16); Line(6, 12, 6, 16); break;
            case OfficeIcon.Save: Path(4, 3, 17, 3, 21, 7, 21, 21, 3, 21, 3, 3, 4, 3); Box(7, 3, 9, 6); Box(7, 14, 10, 7); break;
            case OfficeIcon.Open: Path(3, 19, 3, 5, 9, 5, 12, 8, 21, 8, 21, 10); Path(3, 19, 7, 11, 22, 11, 18, 19, 3, 19); break;
            case OfficeIcon.New: Path(5, 21, 5, 3, 15, 3, 20, 8, 20, 21, 5, 21); Path(15, 3, 15, 8, 20, 8); Line(9, 14, 16, 14); Line(12.5f, 10.5f, 12.5f, 17.5f); break;
            case OfficeIcon.Undo: Path(8, 4, 3, 9, 8, 14); using (var path = new SKPath()) { path.MoveTo(3, 9); path.CubicTo(22, 1, 25, 22, 9, 20); canvas.DrawPath(path, p); } break;
            case OfficeIcon.Redo: Path(16, 4, 21, 9, 16, 14); using (var path = new SKPath()) { path.MoveTo(21, 9); path.CubicTo(2, 1, -1, 22, 15, 20); canvas.DrawPath(path, p); } break;
            case OfficeIcon.Cut: canvas.DrawCircle(6, 17, 3, p); canvas.DrawCircle(18, 17, 3, p); Line(8, 15, 19, 3); Line(16, 15, 5, 3); break;
            case OfficeIcon.Copy: Box(3, 3, 12, 15); Box(8, 7, 13, 15); break;
            case OfficeIcon.Paste: Box(5, 5, 15, 17); Box(9, 2, 7, 5); Line(9, 12, 16, 12); Line(9, 16, 16, 16); break;
            case OfficeIcon.Pointer: Path(5, 2, 6, 21, 11, 15, 16, 21, 19, 18, 14, 12, 21, 11, 5, 2); break;
            case OfficeIcon.Connector: Box(2, 3, 6, 5); Box(16, 16, 6, 5); Path(8, 5.5f, 13, 5.5f, 13, 18.5f, 16, 18.5f); Path(13, 16, 16, 18.5f, 13, 21); break;
            case OfficeIcon.Text: Line(4, 4, 20, 4); Line(12, 4, 12, 21); Line(8, 21, 16, 21); Line(4, 4, 4, 8); Line(20, 4, 20, 8); break;
            case OfficeIcon.Rectangle: Box(3, 5, 18, 14); break;
            case OfficeIcon.Ellipse: canvas.DrawOval(new(3, 4, 21, 20), p); break;
            case OfficeIcon.Fill: Path(5, 10, 12, 3, 21, 12, 14, 19, 5, 10); Line(4, 10, 20, 10); Line(8, 2, 13, 7); p.Color = SKColor.Parse("#F2C95A"); p.StrokeWidth = 3; Line(3, 22, 21, 22); break;
            case OfficeIcon.Line: Line(4, 18, 19, 3); Path(16, 3, 21, 3, 21, 8); p.Color = SKColor.Parse("#4672C4"); p.StrokeWidth = 3; Line(3, 22, 21, 22); break;
            case OfficeIcon.Align: Line(4, 2, 4, 22); Box(7, 4, 14, 5); Box(7, 14, 9, 5); break;
            case OfficeIcon.Distribute: Line(3, 2, 3, 22); Line(21, 2, 21, 22); Box(6, 5, 4, 14); Box(14, 5, 4, 14); break;
            case OfficeIcon.Group: Box(3, 3, 18, 18); Box(6, 6, 7, 7); Box(11, 11, 7, 7); break;
            case OfficeIcon.Ungroup: Box(3, 3, 9, 9); Box(12, 12, 9, 9); break;
            case OfficeIcon.Front: Box(10, 10, 11, 11); p.Style = SKPaintStyle.Fill; p.Color = SKColor.Parse("#7E9FD0"); Box(3, 3, 12, 12); break;
            case OfficeIcon.Back: Box(3, 3, 11, 11); p.Style = SKPaintStyle.Fill; p.Color = SKColor.Parse("#7E9FD0"); Box(10, 10, 11, 11); break;
            case OfficeIcon.Rotate: canvas.DrawArc(new(4, 4, 20, 20), 35, 290, false, p); Path(19, 3, 20, 10, 13, 9); break;
            case OfficeIcon.Delete: Path(6, 7, 7, 21, 17, 21, 18, 7); Line(4, 6, 20, 6); Path(9, 6, 9, 3, 15, 3, 15, 6); Line(10, 10, 10, 17); Line(14, 10, 14, 17); break;
            case OfficeIcon.Search: canvas.DrawCircle(10, 10, 6, p); Line(14, 14, 21, 21); break;
            case OfficeIcon.ChevronDown: Path(6, 9, 12, 15, 18, 9); break;
            case OfficeIcon.ChevronRight: Path(9, 6, 15, 12, 9, 18); break;
            case OfficeIcon.Add: Line(4, 12, 20, 12); Line(12, 4, 12, 20); break;
            case OfficeIcon.Minus: Line(4, 12, 20, 12); break;
            case OfficeIcon.Close: Line(5, 5, 19, 19); Line(19, 5, 5, 19); break;
            case OfficeIcon.Fit: Path(9, 3, 3, 3, 3, 9); Path(15, 3, 21, 3, 21, 9); Path(3, 15, 3, 21, 9, 21); Path(15, 21, 21, 21, 21, 15); Box(7, 7, 10, 10); break;
            case OfficeIcon.Grid: Box(3, 3, 18, 18); for (var i = 9; i < 21; i += 6) { Line(i, 3, i, 21); Line(3, i, 21, i); } break;
            case OfficeIcon.Ruler: Box(3, 6, 18, 12); for (var i = 6; i < 21; i += 3) Line(i, 6, i, i % 2 == 0 ? 12 : 10); break;
            case OfficeIcon.Layers: Path(2, 8, 12, 3, 22, 8, 12, 13, 2, 8); Path(2, 12, 12, 17, 22, 12); Path(2, 16, 12, 21, 22, 16); break;
            case OfficeIcon.Data: Box(3, 4, 18, 16); Line(3, 9, 21, 9); Line(3, 14, 21, 14); Line(9, 4, 9, 20); break;
            case OfficeIcon.Comment: Path(3, 3, 21, 3, 21, 17, 10, 17, 5, 22, 5, 17, 3, 17, 3, 3); Line(7, 8, 17, 8); Line(7, 12, 14, 12); break;
            case OfficeIcon.Check: Path(3, 12, 9, 18, 21, 5); break;
            case OfficeIcon.Export: Path(3, 12, 3, 21, 21, 21, 21, 12); Path(7, 8, 12, 3, 17, 8); Line(12, 3, 12, 16); break;
            case OfficeIcon.Print: Box(6, 2, 12, 6); Box(2, 8, 20, 10); Box(6, 14, 12, 8); Line(17, 11, 19, 11); break;
            case OfficeIcon.Lock: Box(5, 10, 14, 11); canvas.DrawArc(new(7, 2, 17, 15), 180, 180, false, p); Line(12, 14, 12, 17); break;
            case OfficeIcon.Eye: Path(2, 12, 7, 6, 17, 6, 22, 12, 17, 18, 7, 18, 2, 12); canvas.DrawCircle(12, 12, 3, p); break;
            case OfficeIcon.Page: Box(5, 2, 14, 20); Line(8, 7, 16, 7); Line(8, 11, 16, 11); Line(8, 15, 14, 15); break;
            case OfficeIcon.Landscape: Box(2, 5, 20, 14); break;
            case OfficeIcon.Portrait: Box(5, 2, 14, 20); break;
            case OfficeIcon.Left:
            case OfficeIcon.Center:
            case OfficeIcon.Right:
                for (var i = 0; i < 4; i++) { var width = i % 2 == 0 ? 18 : 12; var left = Icon == OfficeIcon.Left ? 3 : Icon == OfficeIcon.Center ? (24 - width) / 2 : 21 - width; Line(left, 5 + i * 5, left + width, 5 + i * 5); } break;
            case OfficeIcon.Layout: Box(8, 2, 8, 5); Box(2, 17, 8, 5); Box(14, 17, 8, 5); Path(12, 7, 12, 12, 6, 12, 6, 17); Path(12, 12, 18, 12, 18, 17); break;
            case OfficeIcon.Bold:
            case OfficeIcon.Italic:
            case OfficeIcon.Help:
            case OfficeIcon.Settings:
                using (var font = new SKFont(SKTypeface.Default, 21)) { p.Style = SKPaintStyle.Fill; canvas.DrawText(Icon == OfficeIcon.Bold ? "B" : Icon == OfficeIcon.Italic ? "I" : Icon == OfficeIcon.Help ? "?" : "⋮", 6, 20, font, p); } break;
            case OfficeIcon.Preferences: Path(5, 2, 14, 20); break;
            case OfficeIcon.User: canvas.DrawCircle(12,4,4, p); break;//.Path( 4, 0, 0, 16,4, 0, 0,1, 12,12,4, 0,0,1, 8,0,4,4, 0, 0,1, 12,4, 42,14, 20, 15,79, 20,18,15,79, 7,58,14, 12, 14") ; break;
               // <path d="M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,6A2,2 0 0,0 10,8A2,2 0 0,0 12,10A2,2 0 0,0 14,8A2,2 0 0,0 12,6M12,13C14.67,13 20,14.33 20,17V20H4V17C4,14.33 9.33,13 12,13M12,14.9C9.03,14.9 5.9,16.36 5.9,17V18.1H18.1V17C18.1,16.36 14.97,14.9 12,14.9Z" /></svg>
        }
        canvas.Restore();
    }
}
