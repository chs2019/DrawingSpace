using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Editing;

/// <summary>
/// An immutable geometry snapshot for one interactive transform. Apply always uses the
/// captured state, never the preceding preview. The caller owns the document transaction.
/// Images, paths, rich text, master identities and formula cells are not copied or replaced.
/// </summary>
public sealed class SelectionTransformSnapshot
{
    private readonly DiagramPage _page;
    private readonly (Shape Target, TransformState Original)[] _shapes;
    private readonly (Connector Target, ConnectorState Original)[] _connectors;
    private readonly HashSet<string> _shapeIds;

    public RectD Bounds { get; }
    public int ShapeCount => _shapes.Length;
    public int ConnectorCount => _connectors.Length;
    public MatrixD LastTransform { get; private set; } = MatrixD.Identity;

    private SelectionTransformSnapshot(DiagramPage page, Shape[] shapes, Connector[] connectors)
    {
        _page = page;
        _shapeIds = shapes.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        _shapes = shapes.Select(s => (s, TransformState.Capture(s))).ToArray();
        _connectors = connectors.Select(c => (c, ConnectorState.Capture(c))).ToArray();
        var bounds = shapes.Select(s => s.WorldBounds).ToList();
        var router = new OrthogonalRouter();
        foreach (var edge in connectors)
        {
            var route = router.Route(page, edge);
            if (route.Points.Count > 0) bounds.Add(RectD.Bounds(route.Points));
        }
        Bounds = bounds.Count == 0 ? new RectD(0, 0, 1, 1) : bounds.Aggregate(RectD.Union);
    }

    /// <summary>
    /// Captures selected objects plus transitive container and formula-frame descendants.
    /// Internal connectors are included once. Locked descendants reject the whole operation.
    /// Group selection expansion belongs to EditorSession.Select, allowing explicit subselection.
    /// </summary>
    public static SelectionTransformSnapshot Capture(DiagramPage page, IEnumerable<Shape> shapes, IEnumerable<Connector>? connectors = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(shapes);
        var pageShapes = page.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var selected = new Dictionary<string, Shape>(StringComparer.Ordinal);
        var pending = new Queue<Shape>();
        foreach (var shape in shapes)
        {
            if (!pageShapes.TryGetValue(shape.Id, out var actual) || !ReferenceEquals(shape, actual))
                throw new ArgumentException("A selected shape does not belong to this page.", nameof(shapes));
            if (selected.TryAdd(shape.Id, shape)) pending.Enqueue(shape);
        }
        var children = new Dictionary<string, List<Shape>>(StringComparer.Ordinal);
        foreach (var shape in page.Shapes)
        {
            foreach (var parent in new[] { shape.ContainerId, shape.FormulaParentId }.OfType<string>().Distinct())
            {
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(shape);
            }
        }
        while (pending.TryDequeue(out var parent))
        {
            if (!children.TryGetValue(parent.Id, out var descendants)) continue;
            foreach (var child in descendants)
                if (selected.TryAdd(child.Id, child)) pending.Enqueue(child);
        }
        if (selected.Values.Any(page.IsLocked))
            throw new InvalidOperationException("Unlock every selected shape and descendant before transforming this selection.");
        var pageEdges = page.Connectors.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in connectors ?? [])
        {
            if (!pageEdges.TryGetValue(edge.Id, out var actual) || !ReferenceEquals(actual, edge))
                throw new ArgumentException("A selected connector does not belong to this page.", nameof(connectors));
            explicitIds.Add(edge.Id);
        }
        var edges = page.Connectors.Where(c => explicitIds.Contains(c.Id)
            || c.SourceId is not null && c.TargetId is not null && selected.ContainsKey(c.SourceId) && selected.ContainsKey(c.TargetId)).ToArray();
        if (edges.Any(c => page.Layers.FirstOrDefault(l => l.Id == c.LayerId)?.Locked == true))
            throw new InvalidOperationException("A locked connector prevents this selection transform.");
        if (selected.Count == 0 && edges.Length == 0)
            throw new InvalidOperationException("Select at least one object to transform.");
        return new(page, selected.Values.ToArray(), edges);
    }

    /// <summary>
    /// Applies a world-space affine transform atomically to the captured geometry. Call
    /// session.Preview afterwards to update ShapeSheet dependencies and repaint the host.
    /// </summary>
    public void Apply(MatrixD transform)
    {
        if (!transform.IsFinite || Math.Abs(transform.Determinant) < 1e-14)
            throw new ArgumentException("The selection transform must be finite and invertible.", nameof(transform));
        var shapes = _page.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var edges = _page.Connectors.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var projectedShapes = new TransformState[_shapes.Length];
        var projectedEdges = new ConnectorState[_connectors.Length];
        // Preflight everything before writing even one property. A stale snapshot or an
        // invalid small child must not leave the first objects partially transformed.
        for (var i = 0; i < _shapes.Length; i++)
        {
            var (target, original) = _shapes[i];
            if (!shapes.TryGetValue(target.Id, out var current) || !ReferenceEquals(current, target))
                throw new InvalidOperationException("The selection snapshot is no longer attached to this page.");
            if (_page.IsLocked(target)) throw new InvalidOperationException("A selected object is locked.");
            projectedShapes[i] = original.Project(transform);
        }
        for (var i = 0; i < _connectors.Length; i++)
        {
            var (target, original) = _connectors[i];
            if (!edges.TryGetValue(target.Id, out var current) || !ReferenceEquals(current, target))
                throw new InvalidOperationException("The connector snapshot is no longer attached to this page.");
            if (_page.Layers.FirstOrDefault(l => l.Id == target.LayerId)?.Locked == true)
                throw new InvalidOperationException("A selected connector is locked.");
            projectedEdges[i] = original.Project(transform, target.SourceId is null || _shapeIds.Contains(target.SourceId), target.TargetId is null || _shapeIds.Contains(target.TargetId));
        }
        for (var i = 0; i < _shapes.Length; i++) projectedShapes[i].Write(_shapes[i].Target);
        for (var i = 0; i < _connectors.Length; i++) projectedEdges[i].Write(_connectors[i].Target);
        LastTransform = transform;
    }

    private readonly record struct TransformState(double X, double Y, double Width, double Height, double Rotation, double ShearX, bool FlipX, bool FlipY, MatrixD Matrix)
    {
        public static TransformState Capture(Shape shape) => new(shape.X, shape.Y, shape.Width, shape.Height, shape.Rotation, shape.ShearX, shape.FlipX, shape.FlipY, shape.WorldMatrix);

        public TransformState Project(MatrixD transform)
        {
            var matrix = transform * Matrix;
            TransformState result;
            if (transform.A == 1 && transform.D == 1 && transform.B == 0 && transform.C == 0)
                result = this with { X = X + transform.Tx, Y = Y + transform.Ty, Matrix = matrix };
            else
            {
                var width = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
                var height = Math.Abs(matrix.Determinant) / width;
                var center = matrix.Map(new PointD(.5, .5));
                result = new(center.X - width / 2, center.Y - height / 2, width, height,
                    Math.Atan2(matrix.B, matrix.A) * 180 / Math.PI,
                    (matrix.A * matrix.C + matrix.B * matrix.D) / (width * height), false, matrix.Determinant < 0, matrix);
            }
            if (!matrix.IsFinite || !double.IsFinite(result.ShearX)
                || result.Width < 1 - 1e-8 || result.Width > 100000 || result.Height < 1 - 1e-8 || result.Height > 100000
                || Math.Abs(result.X) > 1000000 || Math.Abs(result.Y) > 1000000)
                throw new InvalidOperationException("This transform would exceed the document's geometry limits.");
            // Absorb numerical roundoff at the minimum, not meaningful undersizing.
            return result with { Width = Math.Max(1, result.Width), Height = Math.Max(1, result.Height) };
        }

        public void Write(Shape shape)
        {
            shape.X = X; shape.Y = Y; shape.Width = Width; shape.Height = Height;
            shape.Rotation = Rotation; shape.ShearX = ShearX; shape.FlipX = FlipX; shape.FlipY = FlipY;
        }
    }

    private sealed record ConnectorState(PointD Start, PointD End, PointD LabelOffset, PointD[] Waypoints)
    {
        public static ConnectorState Capture(Connector edge) => new(edge.Start, edge.End, edge.LabelOffset, [.. edge.Waypoints]);
        public ConnectorState Project(MatrixD transform, bool moveStart, bool moveEnd)
        {
            var result = new ConnectorState(moveStart ? transform.Map(Start) : Start, moveEnd ? transform.Map(End) : End,
                transform.MapVector(LabelOffset), Waypoints.Select(transform.Map).ToArray());
            foreach (var point in result.Waypoints.Append(result.Start).Append(result.End).Append(result.LabelOffset))
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 1000000 || Math.Abs(point.Y) > 1000000)
                    throw new InvalidOperationException("This transform would exceed the connector coordinate limits.");
            return result;
        }
        public void Write(Connector edge)
        {
            edge.Start = Start; edge.End = End; edge.LabelOffset = LabelOffset; edge.Waypoints = [.. Waypoints];
        }
    }
}
