namespace DrawingSpace.Documents;

public sealed class DiagramLayer
{
    public string Id { get; set; } = "default";
    public string Name { get; set; } = "Drawing";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool Printable { get; set; } = true;
}
