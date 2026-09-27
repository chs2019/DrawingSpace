using System.Text.Json.Serialization;
using DrawingSpace.Core;

namespace DrawingSpace.Documents;

public sealed class DiagramPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Page-1";
    public double Width { get; set; } = 1122;
    public double Height { get; set; } = 794;
    public string Background { get; set; } = "#FFFFFF";
    public List<DiagramLayer> Layers { get; set; } = [new()];
    public List<Shape> Shapes { get; set; } = [];
    public List<Connector> Connectors { get; set; } = [];
    [JsonIgnore] public RectD Bounds => new(0, 0, Width, Height);
    public Shape? Find(string? id) => id is null ? null : Shapes.FirstOrDefault(s => s.Id == id);
    public bool IsVisible(string layerId) => Layers.FirstOrDefault(l => l.Id == layerId)?.Visible ?? true;
    public bool IsLocked(Shape shape) => shape.Locked || (Layers.FirstOrDefault(l => l.Id == shape.LayerId)?.Locked ?? false);
    public bool IsPrintable(string layerId) => Layers.FirstOrDefault(l => l.Id == layerId)?.Printable ?? true;
}
