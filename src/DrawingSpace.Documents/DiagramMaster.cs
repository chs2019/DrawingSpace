namespace DrawingSpace.Documents;

public sealed class DiagramMaster
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Master";
    public uint? VisioId { get; set; }
    public string? VisioPart { get; set; }
    public long Revision { get; set; }
    public Shape Shape { get; set; } = new();
    public List<Shape> Children { get; set; } = [];
    public DiagramMaster Clone() => new() { Id = Id, Name = Name, VisioId = VisioId, VisioPart = VisioPart, Revision = Revision, Shape = Shape.Clone(), Children = Children.Select(s => s.Clone()).ToList() };
}
