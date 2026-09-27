using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public readonly record struct EllipticArc(PointD Center, double RadiusX, double RadiusY, double Rotation, double StartAngle, double SweepAngle)
{
    public PointD At(double fraction)
    {
        var angle = StartAngle + SweepAngle * fraction; var x = RadiusX * Math.Cos(angle); var y = RadiusY * Math.Sin(angle);
        var c = Math.Cos(Rotation); var s = Math.Sin(Rotation);
        return Center + new PointD(c * x - s * y, s * x + c * y);
    }
}

public static class ArcGeometry
{
    /// <summary>SVG endpoint-to-center conversion. Inputs use normalized coordinates; returned geometry uses local drawing units.</summary>
    public static bool TryFromEndpoint(PointD start, GeometrySegment segment, double width, double height, out EllipticArc arc)
    {
        arc = default;
        var a = new PointD(start.X * width, start.Y * height); var b = new PointD(segment.End.X * width, segment.End.Y * height);
        var rx = Math.Abs(segment.Radius.X * width); var ry = Math.Abs(segment.Radius.Y * height);
        if (!a.IsFinite || !b.IsFinite || !double.IsFinite(rx) || !double.IsFinite(ry) || rx < 1e-12 || ry < 1e-12 || a.Distance(b) < 1e-12) return false;
        var rotation = segment.Rotation * Math.PI / 180; var c = Math.Cos(rotation); var s = Math.Sin(rotation);
        var delta = (a - b) / 2; var xp = c * delta.X + s * delta.Y; var yp = -s * delta.X + c * delta.Y;
        var lambda = xp * xp / (rx * rx) + yp * yp / (ry * ry);
        if (lambda > 1) { var scale = Math.Sqrt(lambda); rx *= scale; ry *= scale; }
        var denominator = rx * rx * yp * yp + ry * ry * xp * xp;
        if (denominator < 1e-28) return false;
        var sign = segment.LargeArc == segment.Clockwise ? -1 : 1;
        var factor = sign * Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - denominator) / denominator));
        var cx = factor * rx * yp / ry; var cy = -factor * ry * xp / rx;
        var center = (a + b) / 2 + new PointD(c * cx - s * cy, s * cx + c * cy);
        var first = Math.Atan2((yp - cy) / ry, (xp - cx) / rx);
        var last = Math.Atan2((-yp - cy) / ry, (-xp - cx) / rx);
        var sweep = last - first;
        if (segment.Clockwise && sweep < 0) sweep += Math.Tau;
        if (!segment.Clockwise && sweep > 0) sweep -= Math.Tau;
        arc = new(center, rx, ry, rotation, first, sweep);
        return center.IsFinite && double.IsFinite(sweep);
    }

    /// <summary>Reconstruct an oriented ellipse from start/control/end and a known axis ratio, as used by Visio EllipticalArcTo.</summary>
    public static bool TryThroughThreePoints(PointD start, PointD through, PointD end, double rotation, double ratio, out EllipticArc arc)
    {
        arc = default;
        if (!start.IsFinite || !through.IsFinite || !end.IsFinite || !double.IsFinite(ratio) || ratio <= 0 || ratio > 1000) return false;
        var c = Math.Cos(rotation); var s = Math.Sin(rotation);
        PointD Circle(PointD p) => new(c * p.X + s * p.Y, (-s * p.X + c * p.Y) * ratio);
        var a = Circle(start); var b = Circle(through); var z = Circle(end);
        // Subtract the first point before solving to reduce cancellation for large coordinates.
        var u = b - a; var v = z - a; var determinant = 2 * (u.X * v.Y - u.Y * v.X);
        if (Math.Abs(determinant) < 1e-12 * Math.Max(1, u.Length * v.Length)) return false;
        var u2 = u.X * u.X + u.Y * u.Y; var v2 = v.X * v.X + v.Y * v.Y;
        var centerCircle = a + new PointD((v.Y * u2 - u.Y * v2) / determinant, (u.X * v2 - v.X * u2) / determinant);
        var rx = centerCircle.Distance(a); var ry = rx / ratio;
        var centerRotated = new PointD(centerCircle.X, centerCircle.Y / ratio);
        var center = new PointD(c * centerRotated.X - s * centerRotated.Y, s * centerRotated.X + c * centerRotated.Y);
        var first = Math.Atan2(a.Y - centerCircle.Y, a.X - centerCircle.X);
        var middle = Positive(Math.Atan2(b.Y - centerCircle.Y, b.X - centerCircle.X) - first);
        var last = Positive(Math.Atan2(z.Y - centerCircle.Y, z.X - centerCircle.X) - first);
        var sweep = middle <= last + 1e-10 ? last : last - Math.Tau;
        arc = new(center, rx, ry, rotation, first, sweep);
        return center.IsFinite && double.IsFinite(rx) && rx > 0 && double.IsFinite(sweep);
    }

    public static IEnumerable<GeometrySegment> ToCubics(EllipticArc arc, double width, double height)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(arc.SweepAngle) / (Math.PI / 2)), 1, 64);
        var step = arc.SweepAngle / count; var c = Math.Cos(arc.Rotation); var s = Math.Sin(arc.Rotation);
        PointD Map(double angle) => new(arc.Center.X + c * arc.RadiusX * Math.Cos(angle) - s * arc.RadiusY * Math.Sin(angle), arc.Center.Y + s * arc.RadiusX * Math.Cos(angle) + c * arc.RadiusY * Math.Sin(angle));
        PointD Tangent(double angle) => new(-c * arc.RadiusX * Math.Sin(angle) - s * arc.RadiusY * Math.Cos(angle), -s * arc.RadiusX * Math.Sin(angle) + c * arc.RadiusY * Math.Cos(angle));
        PointD Normalize(PointD p) => new(p.X / width, p.Y / height);
        for (var index = 0; index < count; index++)
        {
            var first = arc.StartAngle + index * step; var last = first + step; var factor = 4d / 3 * Math.Tan(step / 4);
            yield return new() { Verb = GeometryVerb.Cubic, Control1 = Normalize(Map(first) + Tangent(first) * factor), Control2 = Normalize(Map(last) - Tangent(last) * factor), End = Normalize(Map(last)) };
        }
    }
    private static double Positive(double angle) => (angle % Math.Tau + Math.Tau) % Math.Tau;
}
