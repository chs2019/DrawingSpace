using System.Globalization;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioReadContext
{
    private List<GeometryFigure> ReadGeometry(XElement effective, double width, double height, string part, uint shapeId)
    {
        var output = new List<GeometryFigure>(); var total = 0;
        foreach (var section in VisioXml.Children(effective, "Section").Where(s => VisioXml.Attribute(s, "N") == "Geometry" && VisioXml.Attribute(s, "Del") != "1"))
        {
            if (VisioXml.Boolean(section, "NoShow")) continue;
            // Visio compound contours use alternate filling. Native nonzero figures
            // carry an explicit optional extension so DrawingSpace round trips retain
            // their own rule rather than silently turning overlapping contours into holes.
            var figure = new GeometryFigure
            {
                Filled = !VisioXml.Boolean(section, "NoFill"), Stroked = !VisioXml.Boolean(section, "NoLine"),
                EvenOdd = (string?)section.Attribute(VisioNamespaces.DrawingSpace + "FillRule") != "NonZero"
            };
            var current = new PointD(); var first = new PointD(); var hasMove = false;
            PointD Normalize(PointD p) => new(p.X / width, 1 - p.Y / height);
            void CloseSubpath()
            {
                if (hasMove && current.Distance(first) < 1e-9 && figure.Segments.Count > 1
                    && figure.Segments[^1].Verb is not GeometryVerb.Close and not GeometryVerb.Move)
                    figure.Segments.Add(new() { Verb = GeometryVerb.Close });
            }
            void Move(PointD point) { CloseSubpath(); figure.Segments.Add(new() { Verb = GeometryVerb.Move, End = Normalize(point) }); current = first = point; hasMove = true; }
            void Line(PointD point) { if (!hasMove) Move(current); figure.Segments.Add(new() { Verb = GeometryVerb.Line, End = Normalize(point) }); current = point; }
            void Arc(PointD through, PointD end, double rotation, double ratio)
            {
                if (!hasMove) Move(current);
                if (ArcGeometry.TryThroughThreePoints(current, through, end, rotation, ratio, out var arc))
                    figure.Segments.Add(new()
                    {
                        Verb = GeometryVerb.Arc, End = Normalize(end), Radius = new(arc.RadiusX / width, arc.RadiusY / height),
                        Rotation = -arc.Rotation * 180 / Math.PI, LargeArc = Math.Abs(arc.SweepAngle) > Math.PI + 1e-8, Clockwise = arc.SweepAngle < 0
                    });
                else { Line(end); Warn("DegenerateArc", part, "A degenerate elliptical arc was represented as a line.", shapeId); }
                current = end;
            }
            foreach (var row in VisioXml.Children(section, "Row").Where(r => VisioXml.Attribute(r, "Del") != "1").OrderBy(r => VisioXml.Id(r, "IX")))
            {
                if (++total > 32768) throw new InvalidDataException("A shape exceeds the geometry segment budget.");
                var type = VisioXml.Attribute(row, "T"); var relative = type.StartsWith("Rel", StringComparison.Ordinal);
                var x = VisioXml.Number(row, "X") * (relative ? width : 1); var y = VisioXml.Number(row, "Y") * (relative ? height : 1); var end = new PointD(x, y);
                switch (type)
                {
                    case "MoveTo": case "RelMoveTo": Move(end); break;
                    case "LineTo": case "RelLineTo": Line(end); break;
                    case "RelQuadBezTo":
                        if (!hasMove) Move(current);
                        figure.Segments.Add(new() { Verb = GeometryVerb.Quadratic, End = Normalize(end), Control1 = Normalize(new(VisioXml.Number(row, "A") * width, VisioXml.Number(row, "B") * height)) }); current = end; break;
                    case "RelCubBezTo":
                        if (!hasMove) Move(current);
                        figure.Segments.Add(new() { Verb = GeometryVerb.Cubic, End = Normalize(end), Control1 = Normalize(new(VisioXml.Number(row, "A") * width, VisioXml.Number(row, "B") * height)), Control2 = Normalize(new(VisioXml.Number(row, "C") * width, VisioXml.Number(row, "D") * height)) }); current = end; break;
                    case "ArcTo":
                        var chord = end - current; var length = chord.Length; var bow = VisioXml.Number(row, "A");
                        if (length < 1e-12 || Math.Abs(bow) < 1e-12) Line(end);
                        else Arc((current + end) / 2 + new PointD(-chord.Y / length, chord.X / length) * bow, end, 0, 1);
                        break;
                    case "EllipticalArcTo": case "RelEllipticalArcTo":
                        Arc(new(VisioXml.Number(row, "A") * (relative ? width : 1), VisioXml.Number(row, "B") * (relative ? height : 1)), end, VisioXml.Number(row, "C"), VisioXml.Number(row, "D", 1)); break;
                    case "Ellipse":
                        var center = end; var major = new PointD(VisioXml.Number(row, "A"), VisioXml.Number(row, "B")) - center;
                        var minor = new PointD(VisioXml.Number(row, "C"), VisioXml.Number(row, "D")) - center;
                        var rx = major.Length; var ry = minor.Length;
                        if (rx < 1e-12 || ry < 1e-12) { Warn("DegenerateEllipse", part, "A zero-radius ellipse was ignored.", shapeId); break; }
                        Move(center + major);
                        var ellipse = new GeometrySegment { Verb = GeometryVerb.Arc, End = Normalize(center - major), Radius = new(rx / width, ry / height), Rotation = -Math.Atan2(major.Y, major.X) * 180 / Math.PI, Clockwise = true };
                        figure.Segments.Add(ellipse); var second = ellipse.Clone(); second.End = Normalize(center + major); figure.Segments.Add(second); figure.Segments.Add(new() { Verb = GeometryVerb.Close }); current = first; break;
                    case "PolylineTo":
                        var formula = VisioXml.Attribute(VisioXml.Cell(row, "A"), "F", VisioXml.Value(row, "A"));
                        if (TryNumericFunction(formula, "POLYLINE", out var values) && values.Length >= 4 && values.Length % 2 == 0)
                        {
                            for (var i = 2; i + 1 < values.Length; i += 2) Line(new(values[i] * (values[0] == 0 ? width : 1), values[i + 1] * (values[1] == 0 ? height : 1)));
                            if (current.Distance(end) > 1e-10) Line(end);
                        }
                        else { Line(end); Warn("PolylineFormula", part, "A polyline formula could not be evaluated; its endpoint and original XML were retained.", shapeId); }
                        break;
                    case "InfiniteLine":
                        Move(end); Line(new(VisioXml.Number(row, "A"), VisioXml.Number(row, "B"))); Warn("InfiniteLine", part, "An infinite line is displayed between its defining points.", shapeId); break;
                    default:
                        if (type.Length > 0) { Line(end); Warn("GeometryRow", part, "Unsupported geometry row retained in the original package: " + type, shapeId); }
                        break;
                }
            }
            CloseSubpath();
            if (figure.Segments.Count > 0) output.Add(figure);
        }
        return output;
    }
    private static bool TryNumericFunction(string formula, string name, out double[] values)
    {
        values = []; formula = formula.Trim();
        if (!formula.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase) || !formula.EndsWith(')') || formula.Length > 32768) return false;
        var tokens = formula[(name.Length + 1)..^1].Split([',', ';']); if (tokens.Length > 8192) return false;
        var output = new double[tokens.Length];
        for (var i = 0; i < tokens.Length; i++) if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out output[i]) || !double.IsFinite(output[i])) return false;
        values = output; return true;
    }
}
