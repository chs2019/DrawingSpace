using System.Text.Json.Serialization;

namespace DrawingSpace.Core;

public readonly record struct PointD(double X, double Y)
{
    public static PointD Zero => default;
    [JsonIgnore] public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    [JsonIgnore] public double Length => Math.Sqrt(X * X + Y * Y);
    [JsonIgnore] public PointD Normalized => Length < 1e-9 ? Zero : this / Length;
    public double Distance(PointD other) => (this - other).Length;
    public double Manhattan(PointD other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD a, double b) => new(a.X * b, a.Y * b);
    public static PointD operator /(PointD a, double b) => new(a.X / b, a.Y / b);
    public PointD Rotate(double degrees, PointD center)
    {
        var r = degrees * Math.PI / 180;
        var x = X - center.X;
        var y = Y - center.Y;
        return new(center.X + x * Math.Cos(r) - y * Math.Sin(r), center.Y + x * Math.Sin(r) + y * Math.Cos(r));
    }
    public static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var d = b - a;
        var length2 = d.X * d.X + d.Y * d.Y;
        var t = length2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / length2, 0, 1);
        return p.Distance(a + d * t);
    }
}
