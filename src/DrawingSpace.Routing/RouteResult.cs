using DrawingSpace.Core;

namespace DrawingSpace.Routing;

public sealed record RouteResult(IReadOnlyList<PointD> Points, bool IsObstacleFree)
{
    public double Length => Points.Zip(Points.Skip(1), (a, b) => a.Distance(b)).Sum();
    public PointD Midpoint
    {
        get
        {
            var remaining = Length / 2;
            for (var i = 1; i < Points.Count; i++)
            {
                var distance = Points[i - 1].Distance(Points[i]);
                if (remaining <= distance) return Points[i - 1] + (Points[i] - Points[i - 1]) * (distance < 1e-9 ? 0 : remaining / distance);
                remaining -= distance;
            }
            return Points.Count > 0 ? Points[^1] : default;
        }
    }
}
