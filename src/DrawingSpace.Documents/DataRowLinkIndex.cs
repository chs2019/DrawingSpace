namespace DrawingSpace.Documents;

/// <summary>
/// Immutable page-local snapshot of source-row links. Build once per document revision,
/// not once per displayed cell. Includes hidden and locked shapes and missing source rows.
/// Stores identities, not mutable Shape references; rebuild after edits, undo, or page changes.
/// </summary>
public sealed class DataRowLinkIndex
{
    private readonly Dictionary<string, IReadOnlyList<string>> _links = new(StringComparer.Ordinal);
    public string PageId { get; }
    public string SourceId { get; }
    public string KeyColumn { get; }
    public int LinkedShapeCount { get; }

    public DataRowLinkIndex(DiagramPage page, CsvDataTable source)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(source);
        PageId = page.Id; SourceId = source.SourceId; KeyColumn = source.KeyColumn;
        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var shape in page.Shapes)
        {
            if (shape.DataBinding is not { } binding || binding.SourceId != SourceId || binding.KeyColumn != KeyColumn) continue;
            if (!lists.TryGetValue(binding.RowKey, out var ids)) lists.Add(binding.RowKey, ids = []);
            ids.Add(shape.Id); LinkedShapeCount++;
        }
        foreach (var (key, ids) in lists) _links.Add(key, ids.AsReadOnly());
    }

    public IReadOnlyList<string> ShapesFor(string rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        return _links.TryGetValue(rowKey, out var ids) ? ids : Array.Empty<string>();
    }
}
