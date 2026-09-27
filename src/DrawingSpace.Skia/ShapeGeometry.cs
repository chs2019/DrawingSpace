using DrawingSpace.Core;
using DrawingSpace.Documents;
using SkiaSharp;

namespace DrawingSpace.Skia;

/// <summary>Original normalized vector geometry shared by rendering, hit testing, stencil previews and SVG export.</summary>
public static partial class ShapeGeometry
{
    public static SKRect Rect(RectD r) => new((float)r.Left, (float)r.Top, (float)r.Right, (float)r.Bottom);
    public static SKPath Create(Shape shape)
    {
        if (shape.Geometry.Count > 0)
        {
            var combined = new SKPath();
            foreach (var figure in shape.Geometry) { using var part = CreateFigure(shape, figure); combined.AddPath(part); }
            return combined;
        }
        if (shape.IsGroupAnchor) return new SKPath();
        var path = new SKPath();
        var x = (float)shape.X; var y = (float)shape.Y; var w = (float)shape.Width; var h = (float)shape.Height;
        void M(float a, float b) => path.MoveTo(x + a * w, y + b * h);
        void L(float a, float b) => path.LineTo(x + a * w, y + b * h);
        void C(float a, float b, float c, float d, float e, float f) => path.CubicTo(x + a * w, y + b * h, x + c * w, y + d * h, x + e * w, y + f * h);
        void Polygon(params (float X, float Y)[] points)
        {
            M(points[0].X, points[0].Y);
            foreach (var point in points.Skip(1)) L(point.X, point.Y);
            path.Close();
        }
        switch (shape.Kind)
        {
            case ShapeKind.Text: break;
            case ShapeKind.RoundedRectangle: path.AddRoundRect(Rect(shape.Bounds), Math.Min(h / 2, 24), Math.Min(h / 2, 24)); break;
            case ShapeKind.Ellipse: path.AddOval(Rect(shape.Bounds)); break;
            case ShapeKind.Decision: Polygon((.5f, 0), (1, .5f), (.5f, 1), (0, .5f)); break;
            case ShapeKind.Triangle: Polygon((.5f, 0), (1, 1), (0, 1)); break;
            case ShapeKind.Data: Polygon((.17f, 0), (1, 0), (.83f, 1), (0, 1)); break;
            case ShapeKind.Hexagon:
            case ShapeKind.Preparation: Polygon((.18f, 0), (.82f, 0), (1, .5f), (.82f, 1), (.18f, 1), (0, .5f)); break;
            case ShapeKind.Pentagon: Polygon((.5f, 0), (1, .38f), (.81f, 1), (.19f, 1), (0, .38f)); break;
            case ShapeKind.Document:
                M(0, 0); L(1, 0); L(1, .82f); C(.7f, .63f, .3f, 1.05f, 0, .84f); path.Close(); break;
            case ShapeKind.Cylinder:
                M(0, .13f); C(0, -.04f, 1, -.04f, 1, .13f); L(1, .87f); C(1, 1.04f, 0, 1.04f, 0, .87f); path.Close(); break;
            case ShapeKind.Cloud:
                M(.2f, .88f); C(-.08f, .9f, -.06f, .44f, .15f, .43f); C(.09f, .15f, .32f, .02f, .47f, .19f);
                C(.61f, -.13f, .92f, .03f, .91f, .3f); C(1.09f, .36f, 1.06f, .7f, .88f, .76f); C(.91f, 1.07f, .56f, 1.04f, .45f, .88f); C(.4f, 1.01f, .25f, 1.02f, .2f, .88f); path.Close(); break;
            case ShapeKind.Person:
                path.AddOval(new(x + w * .32f, y, x + w * .68f, y + h * .23f));
                Polygon((.25f, .28f), (.75f, .28f), (.94f, .62f), (.8f, .68f), (.66f, .45f), (.66f, .65f), (.77f, 1), (.57f, 1), (.5f, .74f), (.43f, 1), (.23f, 1), (.34f, .65f), (.34f, .45f), (.2f, .68f), (.06f, .62f)); break;
            case ShapeKind.Note: Polygon((0, 0), (.78f, 0), (1, .22f), (1, 1), (0, 1)); break;
            case ShapeKind.ManualInput: Polygon((0, .25f), (1, 0), (1, 1), (0, 1)); break;
            case ShapeKind.ManualOperation: Polygon((0, 0), (1, 0), (.8f, 1), (.2f, 1)); break;
            case ShapeKind.Delay:
                M(0, 0); L(.5f, 0); C(1.16f, 0, 1.16f, 1, .5f, 1); L(0, 1); path.Close(); break;
            case ShapeKind.Display:
                M(.18f, 0); L(.8f, 0); C(1.07f, .15f, 1.07f, .85f, .8f, 1); L(.18f, 1); L(0, .5f); path.Close(); break;
            case ShapeKind.OffPage: Polygon((0, 0), (1, 0), (1, .65f), (.5f, 1), (0, .65f)); break;
            case ShapeKind.Cross: Polygon((.33f, 0), (.67f, 0), (.67f, .33f), (1, .33f), (1, .67f), (.67f, .67f), (.67f, 1), (.33f, 1), (.33f, .67f), (0, .67f), (0, .33f), (.33f, .33f)); break;
            case ShapeKind.Star:
                for (var i = 0; i < 10; i++)
                {
                    var angle = -Math.PI / 2 + i * Math.PI / 5; var radius = i % 2 == 0 ? .5 : .22;
                    var px = (float)(.5 + radius * Math.Cos(angle)); var py = (float)(.5 + radius * Math.Sin(angle));
                    if (i == 0) M(px, py); else L(px, py);
                }
                path.Close(); break;
            case ShapeKind.Arrow: Polygon((0, .25f), (.62f, .25f), (.62f, 0), (1, .5f), (.62f, 1), (.62f, .75f), (0, .75f)); break;
            case ShapeKind.Ring:
                path.FillType = SKPathFillType.EvenOdd; path.AddOval(Rect(shape.Bounds)); path.AddOval(Rect(shape.Bounds.Inflate(-Math.Min(w, h) * .25))); break;
            case ShapeKind.Callout: Polygon((0, 0), (1, 0), (1, .75f), (.4f, .75f), (.2f, 1), (.2f, .75f), (0, .75f)); break;
            case ShapeKind.Annotation: M(.22f, 0); L(0, 0); L(0, 1); L(.22f, 1); break;
            case ShapeKind.Container: path.AddRoundRect(Rect(shape.Bounds), 4, 4); break;
            case ShapeKind.Server: path.AddRoundRect(Rect(shape.Bounds), 4, 4); break;
            default: path.AddRect(Rect(shape.Bounds)); break;
        }
        return path;
    }
    public static SKPath Details(Shape shape)
    {
        var p = new SKPath();
        if (shape.Geometry.Count > 0 || shape.IsGroupAnchor) return p;
        var b = shape.Bounds;
        var x = (float)b.X; var y = (float)b.Y; var w = (float)b.Width; var h = (float)b.Height;
        void Line(float ax, float ay, float bx, float by) { p.MoveTo(x + ax * w, y + ay * h); p.LineTo(x + bx * w, y + by * h); }
        switch (shape.Kind)
        {
            case ShapeKind.PredefinedProcess: Line(.12f, 0, .12f, 1); Line(.88f, 0, .88f, 1); break;
            case ShapeKind.Cylinder: p.AddOval(new(x, y, x + w, y + h * .26f)); break;
            case ShapeKind.Note: Line(.78f, 0, .78f, .22f); Line(.78f, .22f, 1, .22f); break;
            case ShapeKind.Container: Line(0, Math.Min(.3f, 34 / h), 1, Math.Min(.3f, 34 / h)); break;
            case ShapeKind.Server:
                for (var i = 0; i < 3; i++) { var yy = .14f + i * .2f; p.AddRect(new(x + .13f * w, y + yy * h, x + .87f * w, y + (yy + .12f) * h)); }
                break;
        }
        return p;
    }
    public static bool Contains(Shape shape, PointD world, double tolerance = 0)
    {
        if (shape.IsGroupAnchor && shape.Geometry.Count == 0) return false;
        if (!shape.DrawingMatrix.TryInvert(out var inverse)) return false;
        var local = inverse.Map(world);
        if (!shape.Bounds.Inflate(tolerance).Contains(local)) return false;
        if (shape.Geometry.Count > 0)
        {
            foreach (var figure in shape.Geometry)
            {
                using var figurePath = CreateFigure(shape, figure);
                if (figure.Filled && figurePath.Contains((float)local.X, (float)local.Y)) return true;
                if (!figure.Stroked) continue;
                using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(shape.Style.StrokeWidth, tolerance * 2) };
                using var outlinePath = new SKPath();
                if (paint.GetFillPath(figurePath, outlinePath) && outlinePath.Contains((float)local.X, (float)local.Y)) return true;
            }
            return false;
        }
        if (shape.Kind is ShapeKind.Text or ShapeKind.Annotation or ShapeKind.Container) return true;
        using var path = Create(shape);
        if (path.Contains((float)local.X, (float)local.Y)) return true;
        if (tolerance <= 0) return false;
        using var stroke = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)tolerance * 2 };
        using var outline = new SKPath();
        return stroke.GetFillPath(path, outline) && outline.Contains((float)local.X, (float)local.Y);
    }
}
