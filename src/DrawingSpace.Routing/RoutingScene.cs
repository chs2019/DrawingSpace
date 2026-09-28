using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Routing;

/// <summary>
/// One immutable routing snapshot shared by a batch of connectors. Copies only endpoint
/// geometry, custom ports, obstacle bounds and visibility; never images, text or formulas.
/// Capture again after geometry, connection-point or layer-visibility changes.
/// </summary>
public sealed class RoutingScene
{
    private readonly Dictionary<string, EndpointShape> _shapes;
    private readonly RectD[] _obstacles;
    private readonly SpatialBoundsIndex _index;
    public int ShapeCount => _shapes.Count;
    public int ObstacleCount => _obstacles.Length;

    private RoutingScene(DiagramPage page)
    {
        _shapes = new(page.Shapes.Count, StringComparer.Ordinal);
        var obstacles = new List<RectD>(page.Shapes.Count);
        var visible = page.Layers.Where(l => l.Visible).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var shape in page.Shapes)
        {
            var matrix = shape.WorldMatrix;
            _shapes.Add(shape.Id, new(matrix, shape.Bounds.Center,
                shape.ConnectionPoints.Select(p => new Port(p.Id, p.Position, p.Direction)).ToArray()));
            if (visible.Contains(shape.LayerId) && shape.Kind is not ShapeKind.Text and not ShapeKind.Container and not ShapeKind.Annotation)
                obstacles.Add(matrix.Map(new RectD(0, 0, 1, 1)));
        }
        _obstacles = obstacles.ToArray(); _index = new(_obstacles);
    }

    public static RoutingScene Capture(DiagramPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new(page);
    }

    internal bool Contains(string? id) => id is not null && _shapes.ContainsKey(id);
    internal PointD Center(string? id, PointD fallback) => id is not null && _shapes.TryGetValue(id, out var shape) ? shape.Center : fallback;
    internal ResolvedEndpoint Resolve(string? id, string? pointId, PortSide side, PointD free, PointD toward)
    {
        if (id is null || !_shapes.TryGetValue(id, out var shape)) return new(free, default, null, null, PortSide.Auto);
        Port? custom = null;
        if (pointId is not null)
            foreach (var port in shape.Ports) if (port.Id == pointId) { custom = port; break; }
        if (side == PortSide.Auto)
        {
            var local = shape.Matrix.TryInvert(out var inverse) ? inverse.Map(toward) : new PointD(.5, 1);
            side = OrthogonalRouter.Facing(new(.5, .5), local);
        }
        var position = custom?.Position ?? side switch
        {
            PortSide.North => new(.5, 0), PortSide.East => new(1, .5),
            PortSide.South => new(.5, 1), PortSide.West => new(0, .5), _ => new PointD(.5, .5)
        };
        var direction = custom?.Direction is { Length: > 1e-8 } vector ? vector : OrthogonalRouter.Direction(side);
        return new(shape.Matrix.Map(position), shape.Matrix.MapVector(direction).Normalized, id, custom?.Id, side);
    }

    internal RectD[] Obstacles(RectD corridor, double clearance)
    {
        var candidates = new List<int>();
        _index.Query(corridor.Inflate(clearance), candidates);
        // Preserve the page's order for equal-distance A* obstacle selection.
        candidates.Sort();
        var result = new RectD[candidates.Count];
        for (var i = 0; i < result.Length; i++) result[i] = _obstacles[candidates[i]].Inflate(clearance);
        return result;
    }

    private readonly record struct Port(string Id, PointD Position, PointD Direction);
    private readonly record struct EndpointShape(MatrixD Matrix, PointD Center, Port[] Ports);
}
