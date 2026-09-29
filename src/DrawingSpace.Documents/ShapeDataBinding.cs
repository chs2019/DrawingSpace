namespace DrawingSpace.Documents;

/// <summary>One ordinal-keyed source link. Baseline holds the last accepted imported values.</summary>
public sealed class ShapeDataBinding
{
    public string SourceId { get; set; } = "";
    public string KeyColumn { get; set; } = "";
    public string RowKey { get; set; } = "";
    public Dictionary<string, string> Baseline { get; set; } = new(StringComparer.Ordinal);

    public ShapeDataBinding Clone() => new()
    {
        SourceId = SourceId, KeyColumn = KeyColumn, RowKey = RowKey,
        Baseline = new(Baseline, StringComparer.Ordinal)
    };
}
