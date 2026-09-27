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
            Page.Shapes.Add(shape);
            if (shape.ContainerId is null) ContainerService.Assign(Page, shape, ContainerService.FindContainer(Page, shape)?.Id);
            Selection.Clear(); Selection.Add(shape.Id);
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
        shape.GroupId = null; shape.ContainerId = null; shape.Comments.Clear(); shape.Threads.Clear();
        var direction = OrthogonalRouter.Direction(side);
        var distance = side is PortSide.East or PortSide.West ? (source.Width + shape.Width) / 2 + 64 : (source.Height + shape.Height) / 2 + 64;
        var center = source.Bounds.Center + direction * distance;
        shape.X = center.X - shape.Width / 2; shape.Y = center.Y - shape.Height / 2;
        Execute("AutoConnect shape", () =>
        {
            Page.Shapes.Add(shape);
            ContainerService.Assign(Page, shape, ContainerService.FindContainer(Page, shape)?.Id);
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
            // Deleting a container does not silently delete its unselected contents.
            foreach (var shape in Page.Shapes.Where(s => s.ContainerId is not null && shapes.Contains(s.ContainerId))) shape.ContainerId = null;
            Page.Shapes.RemoveAll(s => shapes.Contains(s.Id));
            Page.Connectors.RemoveAll(c => edges.Contains(c.Id) || c.SourceId is not null && shapes.Contains(c.SourceId) || c.TargetId is not null && shapes.Contains(c.TargetId));
            GroupService.RemoveEmpty(Page); Selection.Clear();
        });
    }

    public void MoveSelection(PointD delta)
    {
        if (!delta.IsFinite || EditableShapes.Count == 0 && SelectedConnectors.Count == 0) return;
        TransformSelection(MatrixD.Translation(delta.X, delta.Y), "Move shapes");
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
        foreach (var container in SelectedShapes) foreach (var member in Page.Descendants(container.Id)) selected.Add(member.Id);
        var page = new DiagramPage
        {
            Width = Page.Width, Height = Page.Height,
            Shapes = Page.Shapes.Where(s => selected.Contains(s.Id)).Select(s => s.Clone()).ToList(),
            Connectors = Page.Connectors.Where(c => Selection.Contains(c.Id) || c.SourceId is not null && selected.Contains(c.SourceId) && c.TargetId is not null && selected.Contains(c.TargetId)).Select(c => c.Clone()).ToList(),
            Layers = Page.Layers.Select(l => new DiagramLayer { Id = l.Id, Name = l.Name }).ToList()
        };
        var groupIds = new HashSet<string>();
        foreach (var groupId in page.Shapes.Select(s => s.GroupId).Concat(page.Connectors.Select(c => c.GroupId)))
        {
            var id = groupId;
            while (id is not null && groupIds.Add(id)) id = Page.Groups.FirstOrDefault(g => g.Id == id)?.ParentId;
        }
        page.Groups = Page.Groups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Clone()).ToList();
        foreach (var shape in page.Shapes) if (shape.ContainerId is not null && !selected.Contains(shape.ContainerId)) shape.ContainerId = null;
        var routes = new OrthogonalRouter();
        foreach (var edge in page.Connectors)
        {
            var route = routes.Route(Page, edge);
            if (edge.SourceId is not null && !selected.Contains(edge.SourceId)) { edge.Start = route.Points[0]; edge.SourceId = null; edge.SourcePointId = null; }
            if (edge.TargetId is not null && !selected.Contains(edge.TargetId)) { edge.End = route.Points[^1]; edge.TargetId = null; edge.TargetPointId = null; }
        }
        var masterIds = page.Shapes.Select(s => s.MasterId).OfType<string>().ToHashSet();
        return DocumentCodec.Save(new()
        {
            Title = "Clipboard", Pages = [page], Masters = Document.Masters.Where(m => masterIds.Contains(m.Id)).Select(m => m.Clone()).ToList()
        });
    }

    public void Paste(string json, PointD offset)
    {
        if (!offset.IsFinite) throw new ArgumentOutOfRangeException(nameof(offset));
        var clipboard = DocumentCodec.Load(json); var input = clipboard.Pages[0];
        var map = input.Shapes.ToDictionary(s => s.Id, _ => Guid.NewGuid().ToString("N"));
        var groups = input.Groups.Select(g => g.Id).Concat(input.Shapes.Select(s => s.GroupId).OfType<string>()).Distinct().ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
        var masterMap = new Dictionary<string, string>(); var templateMap = new Dictionary<string, string>();
        Execute("Paste objects", () =>
        {
            foreach (var original in clipboard.Masters)
            {
                var existing = Document.Masters.FirstOrDefault(m => m.Id == original.Id);
                if (existing is not null && ModelJson.Serialize(existing) == ModelJson.Serialize(original)) { masterMap[original.Id] = existing.Id; continue; }
                var master = original.Clone(); master.Id = Guid.NewGuid().ToString("N"); master.VisioId = null; master.VisioPart = null;
                foreach (var template in master.Children.Prepend(master.Shape))
                { var id = template.Id; template.Id = Guid.NewGuid().ToString("N"); templateMap[id] = template.Id; template.VisioId = null; }
                foreach (var template in master.Children.Prepend(master.Shape))
                    if (template.ContainerId is { } parent && templateMap.TryGetValue(parent, out var replacement)) template.ContainerId = replacement;
                Document.Masters.Add(master); masterMap[original.Id] = master.Id;
            }
            foreach (var original in input.Groups)
            {
                var group = original.Clone(); group.Id = groups[original.Id]; group.VisioId = null;
                group.AnchorShapeId = group.AnchorShapeId is { } anchor && map.TryGetValue(anchor, out var mappedAnchor) ? mappedAnchor : null;
                group.ParentId = original.ParentId is not null && groups.TryGetValue(original.ParentId, out var parent) ? parent : null;
                Page.Groups.Add(group);
            }
            Selection.Clear();
            foreach (var original in input.Shapes)
            {
                var shape = original.Clone(true); shape.Id = map[original.Id]; shape.X += offset.X; shape.Y += offset.Y;
                shape.GroupId = shape.GroupId is not null && groups.TryGetValue(shape.GroupId, out var group) ? group : null;
                shape.ContainerId = shape.ContainerId is not null && map.TryGetValue(shape.ContainerId, out var container) ? container : null;
                shape.MasterId = shape.MasterId is not null && masterMap.TryGetValue(shape.MasterId, out var master) ? master : null;
                if (shape.MasterShapeId is { } template && templateMap.TryGetValue(template, out var replacement)) shape.MasterShapeId = replacement;
                shape.FormulaParentId = shape.FormulaParentId is { } formulaParent && map.TryGetValue(formulaParent, out var mappedParent) ? mappedParent : null;
                shape.MasterInstanceId = shape.MasterInstanceId is { } instance && map.TryGetValue(instance, out var mappedInstance) ? mappedInstance : null;
                shape.LayerId = Page.Layers[0].Id; shape.Locked = false;
                RemapFormulaReferences(shape.Cells, map);
                Page.Shapes.Add(shape); Selection.Add(shape.Id);
            }
            foreach (var original in input.Connectors)
            {
                var edge = original.Clone(true);
                edge.SourceId = edge.SourceId is not null && map.TryGetValue(edge.SourceId, out var from) ? from : null;
                edge.TargetId = edge.TargetId is not null && map.TryGetValue(edge.TargetId, out var to) ? to : null;
                edge.GroupId = edge.GroupId is not null && groups.TryGetValue(edge.GroupId, out var group) ? group : null;
                edge.Start += offset; edge.End += offset; edge.Waypoints = edge.Waypoints.Select(p => p + offset).ToList(); edge.LayerId = Page.Layers[0].Id;
                RemapFormulaReferences(edge.Cells, map);
                Page.Connectors.Add(edge); Selection.Add(edge.Id);
            }
        });
    }

    private static void RemapFormulaReferences(Dictionary<string, ShapeCell> cells, IReadOnlyDictionary<string, string> map)
    {
        foreach (var cell in cells.Values)
            foreach (var (oldId, newId) in map) cell.Formula = cell.Formula.Replace("Sheet." + oldId + "!", "Sheet." + newId + "!", StringComparison.OrdinalIgnoreCase);
    }

    public void Duplicate() { if (Selection.Count > 0) Paste(CopySelection(), new(24, 24)); }

    private sealed record ArrangeUnit(Shape[] Members, RectD Bounds);
    private ArrangeUnit[] ArrangeUnits()
    {
        var selected = EditableShapes.Select(s => s.Id).ToHashSet(); var assigned = new HashSet<string>(); var units = new List<ArrangeUnit>();
        foreach (var shape in EditableShapes)
        {
            if (assigned.Contains(shape.Id)) continue;
            var parent = shape.ContainerId; var nested = false; var seen = new HashSet<string>();
            while (parent is not null && seen.Add(parent)) { if (selected.Contains(parent)) { nested = true; break; } parent = Page.Find(parent)?.ContainerId; }
            if (nested) continue;
            var group = shape.GroupId is null ? null : Page.RootGroup(shape.GroupId);
            var roots = group is null ? new[] { shape } : Page.GroupShapes(group).Where(s => selected.Contains(s.Id)).ToArray();
            var members = ContainerService.TransformClosure(Page, roots).Where(s => !assigned.Contains(s.Id)).ToArray();
            if (members.Length == 0) continue;
            foreach (var member in members) assigned.Add(member.Id);
            units.Add(new(members, members.Select(s => s.WorldBounds).Aggregate(RectD.Union)));
        }
        return units.ToArray();
    }

    public void Align(Alignment alignment)
    {
        var units = ArrangeUnits(); if (units.Length < 2) return;
        var bounds = units.Select(u => u.Bounds).Aggregate(RectD.Union);
        Execute("Align shapes", () =>
        {
            foreach (var unit in units)
            {
                var box = unit.Bounds;
                var delta = alignment switch
                {
                    Alignment.Left => new PointD(bounds.Left - box.Left, 0), Alignment.Center => new(bounds.Center.X - box.Center.X, 0),
                    Alignment.Right => new(bounds.Right - box.Right, 0), Alignment.Top => new(0, bounds.Top - box.Top),
                    Alignment.Middle => new(0, bounds.Center.Y - box.Center.Y), Alignment.Bottom => new(0, bounds.Bottom - box.Bottom), _ => default
                };
                GroupService.Transform(Page, unit.Members, MatrixD.Translation(delta.X, delta.Y));
            }
        });
    }

    public void Distribute(bool horizontal)
    {
        var units = ArrangeUnits().OrderBy(u => horizontal ? u.Bounds.Left : u.Bounds.Top).ToArray(); if (units.Length < 3) return;
        var first = units[0].Bounds; var last = units[^1].Bounds;
        var span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        var gap = (span - units.Sum(u => horizontal ? u.Bounds.Width : u.Bounds.Height)) / (units.Length - 1);
        Execute("Distribute shapes", () =>
        {
            var position = horizontal ? first.Left : first.Top;
            foreach (var unit in units)
            {
                var delta = horizontal ? new PointD(position - unit.Bounds.Left, 0) : new PointD(0, position - unit.Bounds.Top);
                GroupService.Transform(Page, unit.Members, MatrixD.Translation(delta.X, delta.Y));
                position += (horizontal ? unit.Bounds.Width : unit.Bounds.Height) + gap;
            }
        });
    }

    public void Group()
    {
        var shapes = EditableShapes; if (shapes.Count < 2) return;
        Execute("Group shapes", () => GroupService.Create(Page, shapes.Select(s => s.Id).ToArray(), SelectedConnectors.Select(c => c.Id).ToArray()));
    }
    public void Ungroup()
    {
        var groups = EditableShapes.Select(s => s.GroupId).OfType<string>().Select(Page.RootGroup).Distinct().ToArray(); if (groups.Length == 0) return;
        Execute("Ungroup shapes", () => { foreach (var group in groups) GroupService.Ungroup(Page, group); });
    }
    public void BringToFront(bool front)
    {
        var shapes = EditableShapes; if (shapes.Count == 0) return;
        Execute(front ? "Bring to front" : "Send to back", () =>
        {
            foreach (var shape in shapes) Page.Shapes.Remove(shape);
            if (front) Page.Shapes.AddRange(shapes); else Page.Shapes.InsertRange(0, shapes);
        });
    }
    public void AutoSizePage()
    {
        Execute("Auto-size page", () =>
        {
            var bounds = Page.Shapes.Count > 0 ? Page.Shapes.Select(s => s.WorldBounds).Aggregate(RectD.Union) : new RectD(0, 0, 1122, 794);
            Page.Width = Math.Clamp(Math.Max(1122, bounds.Right + 64), 24, 100000); Page.Height = Math.Clamp(Math.Max(794, bounds.Bottom + 64), 24, 100000);
        });
    }
    public void AutoLayout()
    {
        var nodes = Page.Shapes.Where(s => !Page.IsLocked(s) && s.Kind is not ShapeKind.Text and not ShapeKind.Container && s.ContainerId is null).ToArray();
        var ids = nodes.Select(s => s.Id).ToHashSet();
        var incoming = nodes.ToDictionary(s => s.Id, s => Page.Connectors.Count(c => c.TargetId == s.Id && c.SourceId is not null && ids.Contains(c.SourceId) && c.SourceId != s.Id));
        var levels = nodes.ToDictionary(s => s.Id, _ => 0); var queue = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key)); var seen = new HashSet<string>();
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            foreach (var edge in Page.Connectors.Where(c => c.SourceId == id && c.TargetId is not null && ids.Contains(c.TargetId) && c.TargetId != id))
            { levels[edge.TargetId!] = Math.Max(levels[edge.TargetId!], levels[id] + 1); if (--incoming[edge.TargetId!] == 0) queue.Enqueue(edge.TargetId!); }
        }
        var cycleLevel = levels.Values.DefaultIfEmpty().Max() + 1;
        foreach (var shape in nodes.Where(s => !seen.Contains(s.Id))) levels[shape.Id] = cycleLevel++;
        Execute("Re-layout page", () =>
        {
            var y = 96d;
            foreach (var row in nodes.GroupBy(s => levels[s.Id]).OrderBy(g => g.Key))
            {
                var total = row.Sum(s => s.Width) + (row.Count() - 1) * 72; var x = Math.Max(64, (Page.Width - total) / 2);
                foreach (var shape in row) { shape.X = x; shape.Y = y; x += shape.Width + 72; }
                y += row.Max(s => s.Height) + 72;
            }
            Page.Height = Math.Min(100000, Math.Max(Page.Height, y + 64));
        });
    }
}
