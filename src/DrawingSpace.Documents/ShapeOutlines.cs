using DrawingSpace.Core;

namespace DrawingSpace.Documents;

/// <summary>Original normalized outlines shared by every renderer and document exporter.</summary>
public static class ShapeOutlines
{
    public static IReadOnlyList<GeometryFigure> Create(Shape shape)
    {
        if (shape.Geometry.Count > 0) return shape.Geometry;
        var builder = new GeometryBuilder();
        var path = builder.Figure(shape.Kind != ShapeKind.Annotation);
        void M(double x, double y) => path.Move(x, y);
        void L(double x, double y) => path.Line(x, y);
        void C(double ax, double ay, double bx, double by, double x, double y) => path.Cubic(ax, ay, bx, by, x, y);
        void Polygon(params (double X, double Y)[] points) => path.Polygon(points);
        switch (shape.Kind)
        {
            case ShapeKind.Text: break;
            case ShapeKind.RoundedRectangle:
                var radius = Math.Min(shape.Height / 2, 24); path.RoundRect(0, 0, 1, 1, radius / shape.Width, radius / shape.Height); break;
            case ShapeKind.Ellipse: path.Ellipse(.5, .5, .5, .5); break;
            case ShapeKind.Decision: Polygon((.5, 0), (1, .5), (.5, 1), (0, .5)); break;
            case ShapeKind.Triangle: Polygon((.5, 0), (1, 1), (0, 1)); break;
            case ShapeKind.Data: Polygon((.17, 0), (1, 0), (.83, 1), (0, 1)); break;
            case ShapeKind.Hexagon:
            case ShapeKind.Preparation: Polygon((.18, 0), (.82, 0), (1, .5), (.82, 1), (.18, 1), (0, .5)); break;
            case ShapeKind.Pentagon: Polygon((.5, 0), (1, .38), (.81, 1), (.19, 1), (0, .38)); break;
            case ShapeKind.Document: M(0, 0); L(1, 0); L(1, .82); C(.7, .63, .3, 1.05, 0, .84); path.Close(); break;
            case ShapeKind.Cylinder: M(0, .13); C(0, -.04, 1, -.04, 1, .13); L(1, .87); C(1, 1.04, 0, 1.04, 0, .87); path.Close(); break;
            case ShapeKind.Cloud:
                M(.2, .88); C(-.08, .9, -.06, .44, .15, .43); C(.09, .15, .32, .02, .47, .19);
                C(.61, -.13, .92, .03, .91, .3); C(1.09, .36, 1.06, .7, .88, .76); C(.91, 1.07, .56, 1.04, .45, .88); C(.4, 1.01, .25, 1.02, .2, .88); path.Close(); break;
            case ShapeKind.Person:
                path.Ellipse(.5, .115, .18, .115);
                Polygon((.25, .28), (.75, .28), (.94, .62), (.8, .68), (.66, .45), (.66, .65), (.77, 1), (.57, 1), (.5, .74), (.43, 1), (.23, 1), (.34, .65), (.34, .45), (.2, .68), (.06, .62)); break;
            case ShapeKind.Note: Polygon((0, 0), (.78, 0), (1, .22), (1, 1), (0, 1)); break;
            case ShapeKind.ManualInput: Polygon((0, .25), (1, 0), (1, 1), (0, 1)); break;
            case ShapeKind.ManualOperation: Polygon((0, 0), (1, 0), (.8, 1), (.2, 1)); break;
            case ShapeKind.Delay: M(0, 0); L(.5, 0); C(1.16, 0, 1.16, 1, .5, 1); L(0, 1); path.Close(); break;
            case ShapeKind.Display: M(.18, 0); L(.8, 0); C(1.07, .15, 1.07, .85, .8, 1); L(.18, 1); L(0, .5); path.Close(); break;
            case ShapeKind.OffPage: Polygon((0, 0), (1, 0), (1, .65), (.5, 1), (0, .65)); break;
            case ShapeKind.Cross: Polygon((.33, 0), (.67, 0), (.67, .33), (1, .33), (1, .67), (.67, .67), (.67, 1), (.33, 1), (.33, .67), (0, .67), (0, .33), (.33, .33)); break;
            case ShapeKind.Star:
                for (var index = 0; index < 10; index++)
                { var angle = -Math.PI / 2 + index * Math.PI / 5; var r = index % 2 == 0 ? .5 : .22; var x = .5 + r * Math.Cos(angle); var y = .5 + r * Math.Sin(angle); if (index == 0) M(x, y); else L(x, y); }
                path.Close(); break;
            case ShapeKind.Arrow: Polygon((0, .25), (.62, .25), (.62, 0), (1, .5), (.62, 1), (.62, .75), (0, .75)); break;
            case ShapeKind.Ring:
                path.Value.EvenOdd = true; path.Ellipse(.5, .5, .5, .5);
                var inset = Math.Min(shape.Width, shape.Height) * .25;
                path.Ellipse(.5, .5, .5 - inset / shape.Width, .5 - inset / shape.Height, false); break;
            case ShapeKind.Callout: Polygon((0, 0), (1, 0), (1, .75), (.4, .75), (.2, 1), (.2, .75), (0, .75)); break;
            case ShapeKind.Annotation: M(.22, 0); L(0, 0); L(0, 1); L(.22, 1); break;
            case ShapeKind.Container:
            case ShapeKind.Server: path.RoundRect(0, 0, 1, 1, Math.Min(.5, 4 / shape.Width), Math.Min(.5, 4 / shape.Height)); break;
            default: path.Rect(0, 0, 1, 1); break;
        }
        var details = builder.Figure(false);
        void Line(double ax, double ay, double bx, double by) { details.Move(ax, ay); details.Line(bx, by); }
        switch (shape.Kind)
        {
            case ShapeKind.PredefinedProcess: Line(.12, 0, .12, 1); Line(.88, 0, .88, 1); break;
            case ShapeKind.Cylinder: details.Ellipse(.5, .13, .5, .13); break;
            case ShapeKind.Note: Line(.78, 0, .78, .22); Line(.78, .22, 1, .22); break;
            case ShapeKind.Container: var header = Math.Min(.8, (shape.Container?.HeaderHeight ?? 34) / shape.Height); Line(0, header, 1, header); break;
            case ShapeKind.Server:
                for (var index = 0; index < 3; index++) details.Rect(.13, .14 + index * .2, .74, .12); break;
        }
        return builder.Figures.Where(f => f.Segments.Count > 0).ToArray();
    }
}

public sealed class GeometryBuilder
{
    public List<GeometryFigure> Figures { get; } = [];
    public FigureBuilder Figure(bool filled = true, bool stroked = true)
    {
        var figure = new GeometryFigure { Filled = filled, Stroked = stroked }; Figures.Add(figure); return new(figure);
    }
    public sealed class FigureBuilder(GeometryFigure figure)
    {
        public GeometryFigure Value => figure;
        public void Move(double x, double y) => figure.Segments.Add(new() { Verb = GeometryVerb.Move, End = new(x, y) });
        public void Line(double x, double y) => figure.Segments.Add(new() { Verb = GeometryVerb.Line, End = new(x, y) });
        public void Quadratic(double ax, double ay, double x, double y) => figure.Segments.Add(new() { Verb = GeometryVerb.Quadratic, Control1 = new(ax, ay), End = new(x, y) });
        public void Cubic(double ax, double ay, double bx, double by, double x, double y) => figure.Segments.Add(new() { Verb = GeometryVerb.Cubic, Control1 = new(ax, ay), Control2 = new(bx, by), End = new(x, y) });
        public void Arc(double rx, double ry, double rotation, bool large, bool clockwise, double x, double y)
            => figure.Segments.Add(new() { Verb = GeometryVerb.Arc, Radius = new(rx, ry), Rotation = rotation, LargeArc = large, Clockwise = clockwise, End = new(x, y) });
        public void Close() => figure.Segments.Add(new() { Verb = GeometryVerb.Close });
        public void Polygon(params (double X, double Y)[] points)
        { if (points.Length == 0) return; Move(points[0].X, points[0].Y); foreach (var p in points.Skip(1)) Line(p.X, p.Y); Close(); }
        public void Rect(double x, double y, double width, double height) => Polygon((x, y), (x + width, y), (x + width, y + height), (x, y + height));
        public void Ellipse(double cx, double cy, double rx, double ry, bool clockwise = true)
        { Move(cx + rx, cy); Arc(rx, ry, 0, false, clockwise, cx - rx, cy); Arc(rx, ry, 0, false, clockwise, cx + rx, cy); Close(); }
        public void RoundRect(double x, double y, double width, double height, double rx, double ry)
        {
            Move(x + rx, y); Line(x + width - rx, y); Arc(rx, ry, 0, false, true, x + width, y + ry);
            Line(x + width, y + height - ry); Arc(rx, ry, 0, false, true, x + width - rx, y + height);
            Line(x + rx, y + height); Arc(rx, ry, 0, false, true, x, y + height - ry);
            Line(x, y + ry); Arc(rx, ry, 0, false, true, x + rx, y); Close();
        }
    }
}
