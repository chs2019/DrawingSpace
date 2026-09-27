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
    public bool IsBackground { get; set; }
    public string? BackgroundPageId { get; set; }
    public uint? VisioId { get; set; }
    public string? VisioPart { get; set; }
    public List<DiagramLayer> Layers { get; set; } = [new()];
    public List<Shape> Shapes { get; set; } = [];
    public List<Connector> Connectors { get; set; } = [];
    public List<DiagramGroup> Groups { get; set; } = [];
    public Dictionary<string, ShapeCell> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public RectD Bounds => new(0, 0, Width, Height);
    public Shape? Find(string? id) => id is null ? null : Shapes.FirstOrDefault(s => s.Id == id);
    public bool IsVisible(string layerId) => Layers.FirstOrDefault(l => l.Id == layerId)?.Visible ?? true;
    public bool IsLocked(Shape shape) => shape.Locked || (Layers.FirstOrDefault(l => l.Id == shape.LayerId)?.Locked ?? false);
    public bool IsPrintable(string layerId) => Layers.FirstOrDefault(l => l.Id == layerId)?.Printable ?? true;
    public string RootGroup(string groupId)
    {
        var seen = new HashSet<string>();
        while (seen.Add(groupId) && Groups.FirstOrDefault(g => g.Id == groupId)?.ParentId is { } parent) groupId = parent;
        return groupId;
    }
    public bool IsInGroup(string? groupId, string root)
    {
        var seen = new HashSet<string>();
        while (groupId is not null && seen.Add(groupId))
        {
            if (groupId == root) return true;
            groupId = Groups.FirstOrDefault(g => g.Id == groupId)?.ParentId;
        }
        return false;
    }
    public IReadOnlyList<Shape> GroupShapes(string groupId) => Shapes.Where(s => IsInGroup(s.GroupId, groupId)).ToArray();
    public IEnumerable<Shape> Descendants(string containerId)
    {
        var seen = new HashSet<string> { containerId }; var queue = new Queue<string>(); queue.Enqueue(containerId);
        while (queue.TryDequeue(out var parent))
            foreach (var child in Shapes.Where(s => s.ContainerId == parent))
                if (seen.Add(child.Id)) { yield return child; queue.Enqueue(child.Id); }
    }
}
