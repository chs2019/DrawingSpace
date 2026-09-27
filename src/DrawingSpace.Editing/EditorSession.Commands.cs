using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    public void AddShape(Shape shape)
    {
        Execute("Insert " + shape.Name, () =>
        {
            if (!Page.Layers.Any(l => l.Id == shape.LayerId)) shape.LayerId = Page.Layers[0].Id;
            Page.Shapes.Add(shape); Selection.Clear(); Selection.Add(shape.Id);
        });
    }
    public Connector Connect(string sourceId, string targetId, PortSide from = PortSide.Auto, PortSide to = PortSide.Auto)
    {
        if (Page.Find(sourceId) is null || Page.Find(targetId) is null) throw new ArgumentException("Both connection shapes must exist on this page.");
        var connector = new Connector { SourceId = sourceId, TargetId = targetId, SourcePort = from, TargetPort = to, LayerId = Page.Layers[0].Id };
        Execute("Connect shapes", () => { Page.Connectors.Add(connector); Selection.Clear(); Selection.Add(connector.Id); });
        return connector;
    }
    public Shape AddConnected(Shape source, PortSide side, Shape? master = null)
    {
        var shape = master?.Clone(true) ?? source.Clone(true);
        shape.GroupId = null; shape.Comments.Clear();
        var direction = OrthogonalRouter.Direction(side);
        var distance = side is PortSide.East or PortSide.West ? (source.Width + shape.Width) / 2 + 64 : (source.Height + shape.Height) / 2 + 64;
        var center = source.Bounds.Center + direction * distance;
        shape.X = center.X - shape.Width / 2; shape.Y = center.Y - shape.Height / 2;
        Execute("AutoConnect shape", () =>
        {
            Page.Shapes.Add(shape);
            Page.Connectors.Add(new() { SourceId = source.Id, TargetId = shape.Id, SourcePort = side, LayerId = source.LayerId });
            Selection.Clear(); Selection.Add(shape.Id);
        });
        return shape;
    }
    public void DeleteSelection()
    {
        var shapes = EditableShapes.Select(s => s.Id).ToHashSet();
        var edges = SelectedConnectors.Where(c => !(Page.Layers.FirstOrDefault(l => l.Id == c.LayerId)?.Locked ?? false)).Select(c => c.Id).ToHashSet();
        if (shapes.Count + edges.Count == 0) return;
        Execute("Delete objects", () =>
        {
            Page.Shapes.RemoveAll(s => shapes.Contains(s.Id));
            Page.Connectors.RemoveAll(c => edges.Contains(c.Id) || c.SourceId is not null && shapes.Contains(c.SourceId) || c.TargetId is not null && shapes.Contains(c.TargetId));
            Selection.Clear();
        });
    }
    public void MoveSelection(PointD delta)
    {
        if (!delta.IsFinite || EditableShapes.Count == 0) return;
        Execute("Move shapes", () => { foreach (var shape in EditableShapes) { shape.X += delta.X; shape.Y += delta.Y; } });
    }
    public void Format(Action<ShapeStyle> change, string name = "Format shapes")
    {
        if (EditableShapes.Count == 0) return;
        Execute(name, () => { foreach (var shape in EditableShapes) change(shape.Style); });
    }
    public void FormatConnector(Action<Connector> change, string name = "Format connectors")
    {
        var connectors = SelectedConnectors.Where(c => !(Page.Layers.FirstOrDefault(l => l.Id == c.LayerId)?.Locked ?? false)).ToArray();
        if (connectors.Length == 0) return;
        Execute(name, () => { foreach (var edge in connectors) change(edge); });
    }
    public string CopySelection()
    {
        var selected = SelectedShapes.Select(s => s.Id).ToHashSet();
        var page = new DiagramPage
        {
            Shapes = SelectedShapes.Select(s => s.Clone()).ToList(),
            Connectors = Page.Connectors.Where(c => Selection.Contains(c.Id) || c.SourceId is not null && selected.Contains(c.SourceId) && c.TargetId is not null && selected.Contains(c.TargetId)).Select(c => c.Clone()).ToList(),
            Layers = Page.Layers.Select(l => new DiagramLayer { Id = l.Id, Name = l.Name }).ToList()
        };
        foreach (var c in page.Connectors)
        {
            if (c.SourceId is not null && !selected.Contains(c.SourceId)) { c.Start = Page.Find(c.SourceId)!.Bounds.Center; c.SourceId = null; }
            if (c.TargetId is not null && !selected.Contains(c.TargetId)) { c.End = Page.Find(c.TargetId)!.Bounds.Center; c.TargetId = null; }
        }
        return DocumentCodec.Save(new() { Title = "Clipboard", Pages = [page] });
    }
    public void Paste(string json, PointD offset)
    {
        var input = DocumentCodec.Load(json).Pages[0];
        var map = input.Shapes.ToDictionary(s => s.Id, _ => Guid.NewGuid().ToString("N"));
        var groups = input.Shapes.Where(s => s.GroupId is not null).Select(s => s.GroupId!).Distinct().ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
        Execute("Paste objects", () =>
        {
            Selection.Clear();
            foreach (var original in input.Shapes)
            {
                var s = original.Clone(); s.Id = map[original.Id]; s.X += offset.X; s.Y += offset.Y;
                s.GroupId = s.GroupId is null ? null : groups[s.GroupId];
                s.LayerId = Page.Layers[0].Id; s.Locked = false;
                Page.Shapes.Add(s); Selection.Add(s.Id);
            }
            foreach (var original in input.Connectors)
            {
                var c = original.Clone(true);
                c.SourceId = c.SourceId is not null && map.TryGetValue(c.SourceId, out var from) ? from : null;
                c.TargetId = c.TargetId is not null && map.TryGetValue(c.TargetId, out var to) ? to : null;
                c.Start += offset; c.End += offset; c.LayerId = Page.Layers[0].Id;
                Page.Connectors.Add(c); Selection.Add(c.Id);
            }
        });
    }
    public void Duplicate() { if (Selection.Count > 0) Paste(CopySelection(), new(24, 24)); }
    public void Align(Alignment alignment)
    {
        var shapes = EditableShapes;
        if (shapes.Count < 2) return;
        var bounds = shapes.Select(s => s.WorldBounds).Aggregate(RectD.Union);
        Execute("Align shapes", () =>
        {
            foreach (var shape in shapes)
            {
                var b = shape.WorldBounds;
                switch (alignment)
                {
                    case Alignment.Left: shape.X += bounds.Left - b.Left; break;
                    case Alignment.Center: shape.X += bounds.Center.X - b.Center.X; break;
                    case Alignment.Right: shape.X += bounds.Right - b.Right; break;
                    case Alignment.Top: shape.Y += bounds.Top - b.Top; break;
                    case Alignment.Middle: shape.Y += bounds.Center.Y - b.Center.Y; break;
                    case Alignment.Bottom: shape.Y += bounds.Bottom - b.Bottom; break;
                }
            }
        });
    }
    public void Distribute(bool horizontal)
    {
        var shapes = EditableShapes.OrderBy(s => horizontal ? s.WorldBounds.Left : s.WorldBounds.Top).ToArray();
        if (shapes.Length < 3) return;
        var first = shapes[0].WorldBounds; var last = shapes[^1].WorldBounds;
        var span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        var gap = (span - shapes.Sum(s => horizontal ? s.WorldBounds.Width : s.WorldBounds.Height)) / (shapes.Length - 1);
        Execute("Distribute shapes", () =>
        {
            var position = horizontal ? first.Left : first.Top;
            foreach (var s in shapes)
            {
                var b = s.WorldBounds;
                if (horizontal) { s.X += position - b.Left; position += b.Width + gap; }
                else { s.Y += position - b.Top; position += b.Height + gap; }
            }
        });
    }
    public void Group()
    {
        var shapes = EditableShapes;
        if (shapes.Count < 2) return;
        Execute("Group shapes", () => { var id = Guid.NewGuid().ToString("N"); foreach (var s in shapes) s.GroupId = id; });
    }
    public void Ungroup()
    {
        if (EditableShapes.All(s => s.GroupId is null)) return;
        Execute("Ungroup shapes", () => { foreach (var s in EditableShapes) s.GroupId = null; });
    }
    public void BringToFront(bool front)
    {
        var shapes = EditableShapes;
        if (shapes.Count == 0) return;
        Execute(front ? "Bring to front" : "Send to back", () =>
        {
            foreach (var s in shapes) Page.Shapes.Remove(s);
            if (front) Page.Shapes.AddRange(shapes); else Page.Shapes.InsertRange(0, shapes);
        });
    }
    public void AutoSizePage()
    {
        Execute("Auto-size page", () =>
        {
            var bounds = Page.Shapes.Count > 0 ? Page.Shapes.Select(s => s.WorldBounds).Aggregate(RectD.Union) : new RectD(0, 0, 1122, 794);
            Page.Width = Math.Clamp(Math.Max(1122, bounds.Right + 64), 24, 100000);
            Page.Height = Math.Clamp(Math.Max(794, bounds.Bottom + 64), 24, 100000);
        });
    }
    public void AutoLayout()
    {
        var nodes = Page.Shapes.Where(s => !Page.IsLocked(s) && s.Kind is not ShapeKind.Text and not ShapeKind.Container).ToArray();
        var ids = nodes.Select(s => s.Id).ToHashSet();
        var incoming = nodes.ToDictionary(s => s.Id, s => Page.Connectors.Count(c => c.TargetId == s.Id && c.SourceId is not null && ids.Contains(c.SourceId) && c.SourceId != s.Id));
        var levels = nodes.ToDictionary(s => s.Id, _ => 0);
        var queue = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key));
        var seen = new HashSet<string>();
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            foreach (var edge in Page.Connectors.Where(c => c.SourceId == id && c.TargetId is not null && ids.Contains(c.TargetId) && c.TargetId != id))
            {
                levels[edge.TargetId!] = Math.Max(levels[edge.TargetId!], levels[id] + 1);
                if (--incoming[edge.TargetId!] == 0) queue.Enqueue(edge.TargetId!);
            }
        }
        var cycleLevel = levels.Values.DefaultIfEmpty().Max() + 1;
        foreach (var s in nodes.Where(s => !seen.Contains(s.Id))) levels[s.Id] = cycleLevel++;
        Execute("Re-layout page", () =>
        {
            var y = 96d;
            foreach (var row in nodes.GroupBy(s => levels[s.Id]).OrderBy(g => g.Key))
            {
                var total = row.Sum(s => s.Width) + (row.Count() - 1) * 72;
                var x = Math.Max(64, (Page.Width - total) / 2);
                foreach (var s in row) { s.X = x; s.Y = y; x += s.Width + 72; }
                y += row.Max(s => s.Height) + 72;
            }
            Page.Height = Math.Max(Page.Height, y + 64);
        });
    }
}
