namespace DrawingSpace.Core;

/// <summary>
/// Immutable, bulk-built bounding-volume hierarchy. Entries retain their input ordinal;
/// queries append matching ordinals to caller-owned storage without allocating. Rebuild
/// after geometry changes. Safe for concurrent readers with separate result buffers.
/// </summary>
public sealed class SpatialBoundsIndex
{
    private const int LeafSize = 8;
    private readonly RectD[] _bounds;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private int _nodeCount;
    public int Count => _bounds.Length;

    public SpatialBoundsIndex(IEnumerable<RectD> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        _bounds = bounds.Take(1_000_001).ToArray();
        if (_bounds.Length > 1_000_000) throw new ArgumentException("At most one million bounds are supported.", nameof(bounds));
        foreach (var box in _bounds)
            if (!box.IsFinite || !double.IsFinite(box.Right) || !double.IsFinite(box.Bottom) || box.Width < 0 || box.Height < 0)
                throw new ArgumentException("Bounds must be finite, nonnegative rectangles.", nameof(bounds));
        _order = new int[_bounds.Length];
        if (_bounds.Length == 0) { _nodes = []; return; }
        var extent = _bounds.Aggregate(RectD.Union);
        if (!extent.IsFinite) throw new ArgumentException("Combined bounds exceed the finite coordinate range.", nameof(bounds));
        var keys = new ulong[_bounds.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            _order[i] = i;
            var center = _bounds[i].Center;
            var x = Quantize(center.X, extent.Left, extent.Width);
            var y = Quantize(center.Y, extent.Top, extent.Height);
            keys[i] = ((ulong)(Spread(x) | (Spread(y) << 1)) << 32) | (uint)i;
        }
        Array.Sort(keys, _order);
        _nodes = new Node[checked((int)(2 * System.Numerics.BitOperations.RoundUpToPowerOf2((uint)((_bounds.Length + LeafSize - 1) / LeafSize)) - 1))];
        Build(0, _order.Length);
    }

    /// <summary>Appends inclusive-intersection matches. Existing results are not cleared or sorted.</summary>
    public void Query(RectD area, List<int> results) => Query(area, results, Count);

    /// <summary>
    /// Appends inclusive-intersection matches whose original input ordinal is less than
    /// maximumOrdinalExclusive. Prunes entire nodes outside this prefix before traversal.
    /// Existing results are retained. The limit must be between zero and Count inclusive.
    /// </summary>
    public void Query(RectD area, List<int> results, int maximumOrdinalExclusive)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (!area.IsFinite || area.Width < 0 || area.Height < 0 || !double.IsFinite(area.Right) || !double.IsFinite(area.Bottom))
            throw new ArgumentException("The query rectangle must be finite and nonnegative.", nameof(area));
        if (maximumOrdinalExclusive < 0 || maximumOrdinalExclusive > Count)
            throw new ArgumentOutOfRangeException(nameof(maximumOrdinalExclusive));
        if (_nodeCount != 0 && maximumOrdinalExclusive != 0)
            QueryNode(0, area, results, maximumOrdinalExclusive);
    }

    private void QueryNode(int index, RectD area, List<int> results, int maximumOrdinalExclusive)
    {
        ref readonly var node = ref _nodes[index];
        if (node.MinimumOrdinal >= maximumOrdinalExclusive || !node.Bounds.Intersects(area)) return;
        if (node.Count > 0)
        {
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                var ordinal = _order[i];
                if (ordinal < maximumOrdinalExclusive && _bounds[ordinal].Intersects(area)) results.Add(ordinal);
            }
            return;
        }
        QueryNode(node.Left, area, results, maximumOrdinalExclusive);
        QueryNode(node.Right, area, results, maximumOrdinalExclusive);
    }

    private int Build(int start, int count)
    {
        var index = _nodeCount++;
        if (count <= LeafSize)
        {
            var bounds = _bounds[_order[start]];
            var minimumOrdinal = _order[start];
            for (var i = start + 1; i < start + count; i++)
            {
                var ordinal = _order[i];
                minimumOrdinal = Math.Min(minimumOrdinal, ordinal);
                bounds = RectD.Union(bounds, _bounds[ordinal]);
            }
            _nodes[index] = new(bounds, start, count, -1, -1, minimumOrdinal);
        }
        else
        {
            var half = count / 2;
            var left = Build(start, half); var right = Build(start + half, count - half);
            _nodes[index] = new(RectD.Union(_nodes[left].Bounds, _nodes[right].Bounds), 0, 0, left, right,
                Math.Min(_nodes[left].MinimumOrdinal, _nodes[right].MinimumOrdinal));
        }
        return index;
    }

    private static uint Quantize(double value, double minimum, double length)
        => length <= 0 ? 0 : (uint)Math.Clamp((value - minimum) / length * 65535, 0, 65535);
    private static uint Spread(uint value)
    {
        value = (value | (value << 8)) & 0x00ff00ff;
        value = (value | (value << 4)) & 0x0f0f0f0f;
        value = (value | (value << 2)) & 0x33333333;
        return (value | (value << 1)) & 0x55555555;
    }
    private readonly record struct Node(RectD Bounds, int Start, int Count, int Left, int Right, int MinimumOrdinal);
}
