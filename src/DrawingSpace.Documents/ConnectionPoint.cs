using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public sealed class ConnectionPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Connection point";
    public PointD Position { get; set; } = new(.5, .5);
    public PointD Direction { get; set; }
    public bool Incoming { get; set; } = true;
    public bool Outgoing { get; set; } = true;
    public ConnectionPoint Clone() => (ConnectionPoint)MemberwiseClone();
}
