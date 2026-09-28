using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Editing;

/// <summary>Fixed geometry snapshot for one transaction. Preview storage is reused; large shape resources are never copied.</summary>
public sealed class SelectionTransformSnapshot
{
    private readonly DiagramPage _page;
    private readonly (Shape Target, TransformState Original)[] _shapes;
    private readonly (Connector Target, ConnectorState Original)[] _connectors;
    private readonly HashSet<string> _shapeIds;
    private readonly int[] _shapeSlots, _connectorSlots;
    private readonly TransformState[] _projectedShapes;
    private readonly ConnectorState[] _projectedConnectors;
    private readonly PointD[][] _waypointBuffers;

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
        var shapeSlots = page.Shapes.Select((s, i) => (s, i)).ToDictionary(p => p.s.Id, p => p.i, StringComparer.Ordinal);
        var edgeSlots = page.Connectors.Select((c, i) => (c, i)).ToDictionary(p => p.c.Id, p => p.i, StringComparer.Ordinal);
        _shapeSlots = shapes.Select(s => shapeSlots[s.Id]).ToArray();
        _connectorSlots = connectors.Select(c => edgeSlots[c.Id]).ToArray();
        _projectedShapes = new TransformState[shapes.Length];
        _projectedConnectors = new ConnectorState[connectors.Length];
        _waypointBuffers = connectors.Select(c => new PointD[c.Waypoints.Count]).ToArray();
        var bounds = shapes.Select(s => s.WorldBounds).ToList();
        var router = new OrthogonalRouter();
        foreach (var edge in connectors)
        {
            var route = router.Route(page, edge);
            if (route.Points.Count > 0) bounds.Add(RectD.Bounds(route.Points));
        }
        Bounds = bounds.Count == 0 ? new RectD(0, 0, 1, 1) : bounds.Aggregate(RectD.Union);
    }

    public static SelectionTransformSnapshot Capture(DiagramPage page, IEnumerable<Shape> shapes, IEnumerable<Connector>? connectors = null)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(shapes);
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
            foreach (var parent in new[] { shape.ContainerId, shape.FormulaParentId }.OfType<string>().Distinct())
            {
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(shape);
            }
        while (pending.TryDequeue(out var parent))
            if (children.TryGetValue(parent.Id, out var descendants))
                foreach (var child in descendants) if (selected.TryAdd(child.Id, child)) pending.Enqueue(child);
        if (selected.Values.Any(page.IsLocked)) throw new InvalidOperationException("Unlock every selected shape and descendant before transforming this selection.");
        var pageEdges = page.Connectors.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var explicitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in connectors ?? [])
        {
            if (!pageEdges.TryGetValue(edge.Id, out var actual) || !ReferenceEquals(actual, edge))
                throw new ArgumentException("A selected connector does not belong to this page.", nameof(connectors));
            explicitIds.Add(edge.Id);
        }
        var edges = page.Connectors.Where(c => explicitIds.Contains(c.Id) || c.SourceId is not null && c.TargetId is not null
            && selected.ContainsKey(c.SourceId) && selected.ContainsKey(c.TargetId)).ToArray();
        if (edges.Any(c => page.Layers.FirstOrDefault(l => l.Id == c.LayerId)?.Locked == true))
            throw new InvalidOperationException("A locked connector prevents this selection transform.");
        if (selected.Count == 0 && edges.Length == 0) throw new InvalidOperationException("Select at least one object to transform.");
        return new(page, selected.Values.ToArray(), edges);
    }

    /// <summary>Atomically projects captured geometry; the caller invokes Preview and commits or cancels the transaction.</summary>
    public void Apply(MatrixD transform)
    {
        if (!transform.IsFinite || Math.Abs(transform.Determinant) < 1e-14)
            throw new ArgumentException("The selection transform must be finite and invertible.", nameof(transform));
        // The normal pointer path checks only selected slots, not every object on the page.
        // A structural edit/reorder falls back to one index rebuild, still rejecting replaced objects.
        ValidateSlots();
        for (var i = 0; i < _shapes.Length; i++)
        {
            var (target, original) = _shapes[i];
            if (_page.IsLocked(target)) throw new InvalidOperationException("A selected object is locked.");
            _projectedShapes[i] = original.Project(transform);
        }
        for (var i = 0; i < _connectors.Length; i++)
        {
            var (target, original) = _connectors[i];
            if (_page.Layers.FirstOrDefault(l => l.Id == target.LayerId)?.Locked == true)
                throw new InvalidOperationException("A selected connector is locked.");
            if (target.SourceId != original.SourceId || target.TargetId != original.TargetId)
                throw new InvalidOperationException("The captured connector attachment changed during the transform.");
            _projectedConnectors[i] = original.Project(transform, target.SourceId is null || _shapeIds.Contains(target.SourceId),
                target.TargetId is null || _shapeIds.Contains(target.TargetId), _waypointBuffers[i]);
        }
        for (var i = 0; i < _connectors.Length; i++) _connectors[i].Target.Waypoints.EnsureCapacity(_waypointBuffers[i].Length);
        for (var i = 0; i < _shapes.Length; i++) _projectedShapes[i].Write(_shapes[i].Target);
        for (var i = 0; i < _connectors.Length; i++) _projectedConnectors[i].Write(_connectors[i].Target);
        LastTransform = transform;
    }

    private void ValidateSlots()
    {
        Dictionary<string, int>? shapes = null, edges = null;
        for (var i = 0; i < _shapes.Length; i++)
        {
            var target = _shapes[i].Target; var slot = _shapeSlots[i];
            if ((uint)slot < (uint)_page.Shapes.Count && ReferenceEquals(_page.Shapes[slot], target)) continue;
            shapes ??= _page.Shapes.Select((s, index) => (s, index)).ToDictionary(p => p.s.Id, p => p.index, StringComparer.Ordinal);
            if (!shapes.TryGetValue(target.Id, out slot) || !ReferenceEquals(_page.Shapes[slot], target))
                throw new InvalidOperationException("The selection snapshot is no longer attached to this page.");
            _shapeSlots[i] = slot;
        }
        for (var i = 0; i < _connectors.Length; i++)
        {
            var target = _connectors[i].Target; var slot = _connectorSlots[i];
            if ((uint)slot < (uint)_page.Connectors.Count && ReferenceEquals(_page.Connectors[slot], target)) continue;
            edges ??= _page.Connectors.Select((c, index) => (c, index)).ToDictionary(p => p.c.Id, p => p.index, StringComparer.Ordinal);
            if (!edges.TryGetValue(target.Id, out slot) || !ReferenceEquals(_page.Connectors[slot], target))
                throw new InvalidOperationException("The connector snapshot is no longer attached to this page.");
            _connectorSlots[i] = slot;
        }
    }

    private readonly record struct TransformState(double X, double Y, double Width, double Height, double Rotation, double ShearX, bool FlipX, bool FlipY, MatrixD Matrix)
    {
        public static TransformState Capture(Shape s) => new(s.X, s.Y, s.Width, s.Height, s.Rotation, s.ShearX, s.FlipX, s.FlipY, s.WorldMatrix);
        public TransformState Project(MatrixD transform)
        {
            var matrix = transform * Matrix;
            TransformState result;
            if (transform.A == 1 && transform.D == 1 && transform.B == 0 && transform.C == 0)
                result = this with { X = X + transform.Tx, Y = Y + transform.Ty, Matrix = matrix };
            else
            {
                var width = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B);
                var height = Math.Abs(matrix.Determinant) / width; var center = matrix.Map(new PointD(.5, .5));
                result = new(center.X - width / 2, center.Y - height / 2, width, height, Math.Atan2(matrix.B, matrix.A) * 180 / Math.PI,
                    (matrix.A * matrix.C + matrix.B * matrix.D) / (width * height), false, matrix.Determinant < 0, matrix);
            }
            if (!matrix.IsFinite || !double.IsFinite(result.ShearX) || Math.Abs(result.ShearX) > 1000
                || result.Width < 1 - 1e-8 || result.Width > 100000 || result.Height < 1 - 1e-8 || result.Height > 100000
                || Math.Abs(result.X) > 1000000 || Math.Abs(result.Y) > 1000000)
                throw new InvalidOperationException("This transform would exceed the document's geometry limits.");
            return result with { Width = Math.Max(1, result.Width), Height = Math.Max(1, result.Height) };
        }
        public void Write(Shape s)
        { s.X = X; s.Y = Y; s.Width = Width; s.Height = Height; s.Rotation = Rotation; s.ShearX = ShearX; s.FlipX = FlipX; s.FlipY = FlipY; }
    }

    private readonly record struct ConnectorState(PointD Start, PointD End, PointD LabelOffset, PointD[] Waypoints, string? SourceId, string? TargetId)
    {
        public static ConnectorState Capture(Connector e) => new(e.Start, e.End, e.LabelOffset, [.. e.Waypoints], e.SourceId, e.TargetId);
        public ConnectorState Project(MatrixD transform, bool moveStart, bool moveEnd, PointD[] buffer)
        {
            var start = moveStart ? transform.Map(Start) : Start; var end = moveEnd ? transform.Map(End) : End;
            var offset = transform.MapVector(LabelOffset); Validate(start); Validate(end); Validate(offset);
            for (var i = 0; i < Waypoints.Length; i++) { buffer[i] = transform.Map(Waypoints[i]); Validate(buffer[i]); }
            return this with { Start = start, End = end, LabelOffset = offset, Waypoints = buffer };
        }
        private static void Validate(PointD p)
        {
            if (!p.IsFinite || Math.Abs(p.X) > 1000000 || Math.Abs(p.Y) > 1000000)
                throw new InvalidOperationException("This transform would exceed the connector coordinate limits.");
        }
        public void Write(Connector e)
        { e.Start = Start; e.End = End; e.LabelOffset = LabelOffset; e.Waypoints.Clear(); e.Waypoints.AddRange(Waypoints); }
    }
}
