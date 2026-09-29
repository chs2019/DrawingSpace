using DrawingSpace.Documents;
using SkiaSharp;

namespace DrawingSpace.Skia;

public sealed partial class SceneRenderer
{
    private readonly Dictionary<string, LinkedListNode<GraphicCacheEntry>> _graphicCache = new(StringComparer.Ordinal);
    private readonly LinkedList<GraphicCacheEntry> _graphicLru = new();
    public long DataGraphicCacheMisses { get; private set; }

    private IReadOnlyList<Shape> DataGraphicsFor(Shape owner)
    {
        if (owner.DataGraphics.Count == 0) return Array.Empty<Shape>();
        if (_graphicCache.TryGetValue(owner.Id, out var node))
        {
            if (node.Value.Matches(owner))
            { _graphicLru.Remove(node); _graphicLru.AddFirst(node); return node.Value.Shapes; }
            _graphicLru.Remove(node); _graphicCache.Remove(owner.Id);
        }
        DataGraphicCacheMisses++;
        var entry = new GraphicCacheEntry(owner);
        _graphicCache[owner.Id] = _graphicLru.AddFirst(entry);
        while (_graphicCache.Count > 256 && _graphicLru.Last is { } last)
        { _graphicCache.Remove(last.Value.Id); _graphicLru.RemoveLast(); }
        return entry.Shapes;
    }

    private void DrawDataGraphics(SKCanvas canvas, Shape owner)
    {
        if (owner.DataGraphics.Count == 0) return;
        canvas.Save();
        try
        {
            canvas.Translate((float)owner.X, (float)owner.Y);
            foreach (var graphic in DataGraphicsFor(owner)) DrawShape(canvas, graphic);
        }
        finally { canvas.Restore(); }
    }

    private void ClearDataGraphics()
    {
        _graphicCache.Clear(); _graphicLru.Clear();
    }

    private sealed class GraphicCacheEntry
    {
        public string Id { get; }
        public IReadOnlyList<Shape> Shapes { get; }
        private readonly double _width, _height, _opacity;
        private readonly string _family;
        private readonly ShapeDataGraphic[] _rules;
        private readonly string?[] _values;
        public GraphicCacheEntry(Shape owner)
        {
            Id = owner.Id; _width = owner.Width; _height = owner.Height;
            _opacity = owner.Style.Opacity; _family = owner.Style.FontFamily;
            _rules = owner.DataGraphics.ToArray();
            _values = _rules.Select(rule => owner.Data.GetValueOrDefault(rule.Field)).ToArray();
            Shapes = DataGraphicProjection.Create(owner);
        }
        public bool Matches(Shape owner)
        {
            if (_width != owner.Width || _height != owner.Height || _opacity != owner.Style.Opacity
                || _family != owner.Style.FontFamily || _rules.Length != owner.DataGraphics.Count) return false;
            for (var i = 0; i < _rules.Length; i++)
                if (_rules[i] != owner.DataGraphics[i] || _values[i] != owner.Data.GetValueOrDefault(_rules[i].Field))
                    return false;
            return true;
        }
    }
}
