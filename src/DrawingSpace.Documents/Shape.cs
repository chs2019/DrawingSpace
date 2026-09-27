using System.Text.Json.Serialization;
using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public sealed class Shape
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Process";
    public string Text { get; set; } = "Process";
    public ShapeKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 144;
    public double Height { get; set; } = 64;
    public double Rotation { get; set; }
    public string LayerId { get; set; } = "default";
    public string? GroupId { get; set; }
    public bool Locked { get; set; }
    public ShapeStyle Style { get; set; } = new();
    public Dictionary<string, string> Data { get; set; } = [];
    public List<string> Comments { get; set; } = [];
    [JsonIgnore] public RectD Bounds => new(X, Y, Width, Height);
    [JsonIgnore] public RectD WorldBounds => Math.Abs(Rotation % 360) < 1e-8 ? Bounds : RectD.Bounds(Bounds.Corners.Select(p => p.Rotate(Rotation, Bounds.Center)));
    public Shape Clone(bool newIdentity = false) => new()
    {
        Id = newIdentity ? Guid.NewGuid().ToString("N") : Id, Name = Name, Text = Text, Kind = Kind,
        X = X, Y = Y, Width = Width, Height = Height, Rotation = Rotation, LayerId = LayerId,
        GroupId = GroupId, Locked = Locked, Style = Style.Clone(), Data = new(Data), Comments = [.. Comments]
    };
    public PointD Port(PortSide side)
    {
        var b = Bounds;
        var p = side switch
        {
            PortSide.North => new PointD(b.Center.X, b.Top),
            PortSide.East => new PointD(b.Right, b.Center.Y),
            PortSide.South => new PointD(b.Center.X, b.Bottom),
            PortSide.West => new PointD(b.Left, b.Center.Y),
            _ => b.Center
        };
        return p.Rotate(Rotation, b.Center);
    }
}
