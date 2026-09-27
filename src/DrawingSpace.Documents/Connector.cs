using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public enum LineJumpStyle { None, Arc, Gap, Square }
public sealed class Connector
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public PortSide SourcePort { get; set; }
    public PortSide TargetPort { get; set; }
    public string? SourcePointId { get; set; }
    public string? TargetPointId { get; set; }
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
    public string? GroupId { get; set; }
    public uint? VisioId { get; set; }
    public List<PointD> Waypoints { get; set; } = [];
    public LineJumpStyle LineJumps { get; set; } = LineJumpStyle.Arc;
    public double JumpSize { get; set; } = 6;
    public double LabelPosition { get; set; } = .5;
    public PointD LabelOffset { get; set; }
    public Dictionary<string, ShapeCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Connector Clone(bool newIdentity = false)
    {
        var clone = (Connector)MemberwiseClone();
        if (newIdentity) { clone.Id = Guid.NewGuid().ToString("N"); clone.VisioId = null; }
        clone.Waypoints = [.. Waypoints];
        clone.Cells = Cells.ToDictionary(p => p.Key, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        return clone;
    }
}
