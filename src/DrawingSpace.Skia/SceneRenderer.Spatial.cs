using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly Dictionary<DiagramPage, ShapeIndex> _shapeIndexes = [];
    private readonly List<int> _visibleShapes = [];
    private readonly List<int> _hitShapes = [];

    private SpatialBoundsIndex ShapeIndexFor(DiagramPage page, long revision)
    {
        if (_shapeIndexes.TryGetValue(page, out var cached) && cached.Revision == revision && cached.Count == page.Shapes.Count)
            return cached.Index;
        // Bound retention across background pages and document switching.
        if (_shapeIndexes.Count >= 8 && !_shapeIndexes.ContainsKey(page)) _shapeIndexes.Clear();
        var index = new SpatialBoundsIndex(page.Shapes.Select(DataGraphicProjection.WorldBounds));
        _shapeIndexes[page] = new(revision, page.Shapes.Count, index);
        return index;
    }

    private List<int> VisibleShapes(DiagramPage page, long revision, RectD area)
    {
        _visibleShapes.Clear(); ShapeIndexFor(page, revision).Query(area, _visibleShapes);
        _visibleShapes.Sort(); // Spatial order must never change painter order.
        return _visibleShapes;
    }

    private Shape? HitIndexedShape(DiagramPage page, PointD point, long revision, double tolerance)
    {
        _hitShapes.Clear(); ShapeIndexFor(page, revision).Query(new RectD(point.X, point.Y, 0, 0).Inflate(tolerance), _hitShapes);
        _hitShapes.Sort();
        for (var pass = 0; pass < 2; pass++)
            for (var i = _hitShapes.Count - 1; i >= 0; i--)
            {
                var shape = page.Shapes[_hitShapes[i]];
                if ((shape.Kind == ShapeKind.Container) != (pass == 1)) continue;
                if (page.IsVisible(shape.LayerId) && ShapeGeometry.Contains(shape, point, tolerance)) return shape;
            }
        return null;
    }

    private sealed record ShapeIndex(long Revision, int Count, SpatialBoundsIndex Index);
}
