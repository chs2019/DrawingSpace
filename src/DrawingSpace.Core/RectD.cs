namespace DrawingSpace.Core;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height);
    public bool Contains(PointD p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
    public bool Contains(RectD r) => r.Left >= Left && r.Right <= Right && r.Top >= Top && r.Bottom <= Bottom;
    public bool Intersects(RectD r) => Left <= r.Right && Right >= r.Left && Top <= r.Bottom && Bottom >= r.Top;
    public RectD Inflate(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);
    public RectD Translate(PointD d) => new(X + d.X, Y + d.Y, Width, Height);
    public PointD[] Corners => [new(Left, Top), new(Right, Top), new(Right, Bottom), new(Left, Bottom)];
    public static RectD FromPoints(PointD a, PointD b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    public static RectD Union(RectD a, RectD b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right) - Math.Min(a.Left, b.Left), Math.Max(a.Bottom, b.Bottom) - Math.Min(a.Top, b.Top));
    public static RectD Bounds(IEnumerable<PointD> points)
    {
        var list = points.ToArray();
        return list.Length == 0 ? default : new(list.Min(p => p.X), list.Min(p => p.Y), list.Max(p => p.X) - list.Min(p => p.X), list.Max(p => p.Y) - list.Min(p => p.Y));
    }
}
