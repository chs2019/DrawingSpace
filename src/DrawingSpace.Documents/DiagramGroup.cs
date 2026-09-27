namespace DrawingSpace.Documents;

/// <summary>Group hierarchy is independent of shape storage and supports nested affine manipulation.</summary>
public sealed class DiagramGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Group";
    public string? ParentId { get; set; }
    public uint? VisioId { get; set; }
    public DiagramGroup Clone() => (DiagramGroup)MemberwiseClone();
}
