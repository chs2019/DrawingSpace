using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public sealed class Connector
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public PortSide SourcePort { get; set; }
    public PortSide TargetPort { get; set; }
    public PointD Start { get; set; }
    public PointD End { get; set; }
    public ConnectorKind Kind { get; set; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#507EAA";
    public double Width { get; set; } = 1.5;
    public bool Dashed { get; set; }
    public ArrowHead StartArrow { get; set; }
    public ArrowHead EndArrow { get; set; } = ArrowHead.Triangle;
    public string LayerId { get; set; } = "default";
    public Connector Clone(bool newIdentity = false)
    {
        var clone = (Connector)MemberwiseClone();
        if (newIdentity) clone.Id = Guid.NewGuid().ToString("N");
        return clone;
    }
}
