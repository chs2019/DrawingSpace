using DrawingSpace.Core;

namespace DrawingSpace.Routing;

/// <summary>
/// A fixed-route orthogonal segment gesture. Interior segments translate perpendicular
/// to their direction; endpoint segments insert doglegs without moving the glued ends.
/// The result is a waypoint sequence for the normal obstacle-aware router.
/// </summary>
public sealed class ConnectorSegmentEditor
{
    private const double Epsilon = 1e-7;
    private readonly PointD[] _route;
    public int SegmentIndex { get; }
    public bool IsHorizontal { get; }
    public PointD Midpoint => (_route[SegmentIndex] + _route[SegmentIndex + 1]) / 2;

    public ConnectorSegmentEditor(IReadOnlyList<PointD> route, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Count is < 2 or > 4094) throw new ArgumentException("The route exceeds the editable segment budget.", nameof(route));
        if (segmentIndex < 0 || segmentIndex >= route.Count - 1) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        if (route.Any(p => !IsValid(p))) throw new ArgumentException("Route coordinates must be finite and within document limits.", nameof(route));
        if (!CanDrag(route[segmentIndex], route[segmentIndex + 1]))
            throw new ArgumentException("Only non-degenerate orthogonal segments can be dragged.", nameof(segmentIndex));
        _route = route.ToArray(); SegmentIndex = segmentIndex;
        IsHorizontal = Math.Abs(_route[segmentIndex].Y - _route[segmentIndex + 1].Y) <= Epsilon;
    }

    public static bool CanDrag(PointD a, PointD b) => IsValid(a) && IsValid(b) && a.Distance(b) > Epsilon
        && (Math.Abs(a.X - b.X) <= Epsilon || Math.Abs(a.Y - b.Y) <= Epsilon);

    /// <summary>Projects pointer motion onto the segment normal, optionally snapping its absolute coordinate.</summary>
    public double Offset(PointD pointerStart, PointD pointerCurrent, double grid = 0)
    {
        if (!IsValid(pointerStart) || !IsValid(pointerCurrent) || !double.IsFinite(grid) || grid < 0)
            throw new ArgumentOutOfRangeException(nameof(pointerCurrent));
        var coordinate = IsHorizontal ? Midpoint.Y : Midpoint.X;
        var displacement = IsHorizontal ? pointerCurrent.Y - pointerStart.Y : pointerCurrent.X - pointerStart.X;
        if (grid > 0) displacement = Math.Round((coordinate + displacement) / grid, MidpointRounding.AwayFromZero) * grid - coordinate;
        return displacement;
    }

    public IReadOnlyList<PointD> CreateRoute(double perpendicularOffset)
    {
        if (!double.IsFinite(perpendicularOffset)) throw new ArgumentOutOfRangeException(nameof(perpendicularOffset));
        if (Math.Abs(perpendicularOffset) < Epsilon) return _route.ToArray();
        var a = _route[SegmentIndex]; var b = _route[SegmentIndex + 1];
        var delta = IsHorizontal ? new PointD(0, perpendicularOffset) : new PointD(perpendicularOffset, 0);
        var direction = (b - a).Normalized;
        var stubLength = Math.Min(20, a.Distance(b) / 4);
        var first = SegmentIndex == 0; var last = SegmentIndex == _route.Length - 2;
        var route = new List<PointD>(_route.Length + 4);
        route.AddRange(_route.Take(SegmentIndex));
        var movedA = a + delta; var movedB = b + delta;
        if (first)
        {
            var stub = a + direction * stubLength;
            route.Add(a); route.Add(stub); movedA = stub + delta;
        }
        route.Add(movedA);
        if (last) movedB = b - direction * stubLength + delta;
        route.Add(movedB);
        if (last) { route.Add(b - direction * stubLength); route.Add(b); }
        route.AddRange(_route.Skip(SegmentIndex + 2));
        if (route.Any(p => !IsValid(p))) throw new InvalidOperationException("The dragged segment exceeds the document coordinate limits.");
        var result = OrthogonalRouter.Simplify(route);
        if (result.Count > 4098) throw new InvalidOperationException("The dragged route exceeds the waypoint budget.");
        return result;
    }

    public IReadOnlyList<PointD> CreateWaypoints(double perpendicularOffset)
    {
        var route = CreateRoute(perpendicularOffset);
        return route.Skip(1).Take(Math.Max(0, route.Count - 2)).ToArray();
    }

    private static bool IsValid(PointD p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= 1000000 && Math.Abs(p.Y) <= 1000000;
}
