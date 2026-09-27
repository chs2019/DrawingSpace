namespace DrawingSpace.Core;

/// <summary>Double-precision affine matrix. Products apply the right-hand transform first.</summary>
public readonly record struct MatrixD(double A, double B, double C, double D, double Tx, double Ty)
{
    public static MatrixD Identity => new(1, 0, 0, 1, 0, 0);
    public double Determinant => A * D - B * C;
    public bool IsFinite => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D) && double.IsFinite(Tx) && double.IsFinite(Ty);
    public PointD Map(PointD p) => new(A * p.X + C * p.Y + Tx, B * p.X + D * p.Y + Ty);
    public PointD MapVector(PointD p) => new(A * p.X + C * p.Y, B * p.X + D * p.Y);
    public static MatrixD Translation(double x, double y) => new(1, 0, 0, 1, x, y);
    public static MatrixD Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
    public static MatrixD Rotation(double degrees)
    {
        var radians = degrees * Math.PI / 180; var c = Math.Cos(radians); var s = Math.Sin(radians);
        return new(c, s, -s, c, 0, 0);
    }
    public static MatrixD Around(PointD center, MatrixD transform) => Translation(center.X, center.Y) * transform * Translation(-center.X, -center.Y);
    public static MatrixD operator *(MatrixD a, MatrixD b) => new(
        a.A * b.A + a.C * b.B, a.B * b.A + a.D * b.B,
        a.A * b.C + a.C * b.D, a.B * b.C + a.D * b.D,
        a.A * b.Tx + a.C * b.Ty + a.Tx, a.B * b.Tx + a.D * b.Ty + a.Ty);
    public bool TryInvert(out MatrixD result)
    {
        var det = Determinant;
        if (!IsFinite || Math.Abs(det) < 1e-14) { result = default; return false; }
        result = new(D / det, -B / det, -C / det, A / det, (C * Ty - D * Tx) / det, (B * Tx - A * Ty) / det);
        return result.IsFinite;
    }
}

public static class MatrixBoundsExtensions
{
    public static RectD Map(this MatrixD matrix, RectD bounds) => RectD.Bounds(bounds.Corners.Select(matrix.Map));
}
