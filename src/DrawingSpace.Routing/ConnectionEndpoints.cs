using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

public readonly record struct ResolvedEndpoint(PointD Position, PointD Direction, string? ShapeId, string? PointId, PortSide Side);

/// <summary>Shared by routing, endpoint gestures and connection-point adorners.</summary>
public static class ConnectionEndpoints
{
    private static readonly PortSide[] Sides = [PortSide.North, PortSide.East, PortSide.South, PortSide.West];
    public static ResolvedEndpoint Resolve(Shape? shape, string? pointId, PortSide side, PointD free, PointD toward)
    {
        if (shape is null) return new(free, PointD.Zero, null, null, PortSide.Auto);
        var custom = shape.ConnectionPoints.FirstOrDefault(p => p.Id == pointId);
        if (side == PortSide.Auto)
        {
            var local = shape.WorldMatrix.TryInvert(out var inverse) ? inverse.Map(toward) : new PointD(.5, 1);
            side = OrthogonalRouter.Facing(new(.5, .5), local);
        }
        var point = custom is null ? shape.Port(side) : shape.WorldMatrix.Map(custom.Position);
        var direction = shape.WorldMatrix.MapVector(custom?.Direction is { Length: > 1e-8 } vector ? vector : OrthogonalRouter.Direction(side)).Normalized;
        return new(point, direction, shape.Id, custom?.Id, side);
    }

    public static ResolvedEndpoint? Hit(DiagramPage page, PointD world, double tolerance, bool source)
    {
        if (!world.IsFinite || !double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        ResolvedEndpoint? result = null; var distance = tolerance;
        foreach (var shape in page.Shapes.AsEnumerable().Reverse().Where(s => page.IsVisible(s.LayerId) && !page.IsLocked(s)))
        {
            foreach (var point in shape.ConnectionPoints.Where(p => source ? p.Outgoing : p.Incoming))
            {
                var resolved = Resolve(shape, point.Id, PortSide.Auto, world, world);
                var d = resolved.Position.Distance(world);
                if (d <= distance) { distance = d; result = resolved; }
            }
            foreach (var side in Sides)
            {
                var resolved = Resolve(shape, null, side, world, world); var d = resolved.Position.Distance(world);
                if (d < distance) { distance = d; result = resolved; }
            }
        }
        return result;
    }

    public static void Attach(Connector edge, bool source, ResolvedEndpoint? target, PointD free)
    {
        if (!free.IsFinite) throw new ArgumentOutOfRangeException(nameof(free));
        if (source) { edge.SourceId = target?.ShapeId; edge.SourcePort = target?.Side ?? PortSide.Auto; edge.SourcePointId = target?.PointId; edge.Start = target?.Position ?? free; }
        else { edge.TargetId = target?.ShapeId; edge.TargetPort = target?.Side ?? PortSide.Auto; edge.TargetPointId = target?.PointId; edge.End = target?.Position ?? free; }
    }
}
