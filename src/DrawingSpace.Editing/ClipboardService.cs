using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

/// <summary>Identity-safe diagram graph clipboard. Paste is executed inside the caller's document transaction.</summary>
public static class ClipboardService
{
    public static string Copy(DiagramDocument document, DiagramPage source, IEnumerable<string> selection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selection);
        var selected = selection.ToHashSet(StringComparer.Ordinal);
        var shapes = source.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var shape in source.Shapes)
            foreach (var parent in new[] { shape.ContainerId, shape.FormulaParentId }.OfType<string>().Distinct())
            {
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(shape.Id);
            }
        var queue = new Queue<string>(selected.Where(shapes.ContainsKey));
        while (queue.TryDequeue(out var id))
            if (children.TryGetValue(id, out var members))
                foreach (var child in members) if (selected.Add(child)) queue.Enqueue(child);
        var page = new DiagramPage
        {
            Width = source.Width, Height = source.Height,
            Shapes = source.Shapes.Where(s => selected.Contains(s.Id)).Select(s => s.Clone()).ToList(),
            Connectors = source.Connectors.Where(c => selected.Contains(c.Id) || c.SourceId is not null && c.TargetId is not null
                && selected.Contains(c.SourceId) && selected.Contains(c.TargetId)).Select(c => c.Clone()).ToList()
        };
        var included = page.Shapes.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var scope = new ShapeSheetScope(document, source);
        foreach (var shape in page.Shapes)
        {
            var original = shapes[shape.Id];
            RewriteCells(scope, original, shape, included, null);
            if (shape.ContainerId is { } container && !included.Contains(container)) shape.ContainerId = null;
            if (shape.FormulaParentId is { } parent && !included.Contains(parent)) CoordinateRebase.Reparent(page, shape, null);
        }
        var groups = source.Groups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in page.Shapes.Select(s => s.GroupId).Concat(page.Connectors.Select(c => c.GroupId)))
            for (var id = group; id is not null && groupIds.Add(id); id = groups.GetValueOrDefault(id)?.ParentId) { }
        page.Groups = source.Groups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Clone()).ToList();
        foreach (var group in page.Groups)
            if (group.AnchorShapeId is { } anchor && !included.Contains(anchor)) group.AnchorShapeId = null;
        var layers = page.Shapes.Select(s => s.LayerId).Concat(page.Connectors.Select(c => c.LayerId)).ToHashSet(StringComparer.Ordinal);
        page.Layers = source.Layers.Where(l => layers.Contains(l.Id)).Select(l => new DiagramLayer
            { Id = l.Id, Name = l.Name, Visible = true, Locked = false, Printable = l.Printable }).ToList();
        if (page.Layers.Count == 0) page.Layers.Add(new());
        var router = new OrthogonalRouter();
        foreach (var edge in page.Connectors)
        {
            var detachStart = edge.SourceId is { } a && !included.Contains(a);
            var detachEnd = edge.TargetId is { } b && !included.Contains(b);
            if (!detachStart && !detachEnd) continue;
            var route = router.Route(source, edge);
            if (detachStart) { edge.Start = route.Points[0]; edge.SourceId = null; edge.SourcePointId = null; }
            if (detachEnd) { edge.End = route.Points[^1]; edge.TargetId = null; edge.TargetPointId = null; }
        }
        var masterIds = page.Shapes.Select(s => s.MasterId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var clipboard = new DiagramDocument { Title = "Clipboard", Pages = [page],
            Masters = document.Masters.Where(m => masterIds.Contains(m.Id)).Select(m => m.Clone()).ToList() };
        DocumentCodec.Validate(clipboard);
        return DocumentCodec.Save(clipboard);
    }

    public static IReadOnlyList<string> Paste(DiagramDocument document, DiagramPage target, DiagramDocument clipboard, PointD offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(clipboard);
        if (!offset.IsFinite) throw new ArgumentOutOfRangeException(nameof(offset));
        var source = clipboard.Pages[0];
        var identities = source.Shapes.Select(s => s.Id).Concat(source.Connectors.Select(c => c.Id)).Concat(source.Groups.Select(g => g.Id))
            .Concat(source.Shapes.Select(s => s.MasterInstanceId).OfType<string>()).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => NewId(), StringComparer.Ordinal);
        string? Map(string? id) => id is not null ? identities.GetValueOrDefault(id) : null;
        var templates = new Dictionary<string, string>(StringComparer.Ordinal);
        var masters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var original in clipboard.Masters)
        {
            var existing = document.Masters.FirstOrDefault(m => m.Id == original.Id);
            if (existing is not null && ModelJson.Serialize(existing) == ModelJson.Serialize(original))
            { masters[original.Id] = existing.Id; continue; }
            var master = CloneMaster(original, templates);
            masters[original.Id] = master.Id; document.Masters.Add(master);
        }
        var layerMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var layer in source.Layers)
        {
            var existing = target.Layers.FirstOrDefault(l => l.Name == layer.Name && !l.Locked && l.Visible);
            if (existing is null)
            {
                if (target.Layers.Count == 256) throw new InvalidDataException("The paste would exceed the layer limit.");
                existing = new DiagramLayer { Id = NewId(), Name = layer.Name, Printable = layer.Printable };
                target.Layers.Add(existing);
            }
            layerMap[layer.Id] = existing.Id;
        }
        foreach (var original in source.Groups)
        {
            var group = original.Clone(); group.Id = identities[original.Id]; group.VisioId = null;
            group.ParentId = Map(original.ParentId); group.AnchorShapeId = Map(original.AnchorShapeId); target.Groups.Add(group);
        }
        var included = source.Shapes.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var scope = new ShapeSheetScope(clipboard, source);
        var pasted = new List<string>(source.Shapes.Count + source.Connectors.Count);
        var newShapes = new List<Shape>(source.Shapes.Count);
        foreach (var original in source.Shapes)
        {
            var shape = original.Clone(true); shape.Id = identities[original.Id]; shape.X += offset.X; shape.Y += offset.Y;
            shape.GroupId = Map(original.GroupId); shape.ContainerId = Map(original.ContainerId); shape.FormulaParentId = Map(original.FormulaParentId);
            shape.MasterInstanceId = Map(original.MasterInstanceId);
            shape.MasterId = original.MasterId is { } masterId ? masters.GetValueOrDefault(masterId) : null;
            if (original.MasterShapeId is { } template && templates.TryGetValue(template, out var mapped)) shape.MasterShapeId = mapped;
            shape.LayerId = layerMap[original.LayerId]; shape.Locked = false;
            RewriteCells(scope, original, shape, included, identities);
            // Numeric Sheet.n references are resolved in their SOURCE instance before Visio IDs disappear.
            target.Shapes.Add(shape); newShapes.Add(shape); pasted.Add(shape.Id);
        }
        // All new parents must exist before deriving their children's local placement.
        foreach (var shape in newShapes.Where(s => s.FormulaParentId is null)) CoordinateRebase.SynchronizePlacement(target, shape);
        foreach (var original in source.Connectors)
        {
            var edge = original.Clone(true); edge.Id = identities[original.Id]; edge.SourceId = Map(original.SourceId); edge.TargetId = Map(original.TargetId);
            edge.GroupId = Map(original.GroupId); edge.LayerId = layerMap[original.LayerId];
            if (edge.SourceId is null) edge.SourcePointId = null;
            if (edge.TargetId is null) edge.TargetPointId = null;
            edge.Start += offset; edge.End += offset; edge.Waypoints = edge.Waypoints.Select(p => p + offset).ToList();
            foreach (var cell in edge.Cells.Values) cell.Formula = FormulaReferenceRewriter.RemapSheets(cell.Formula, identities);
            target.Connectors.Add(edge); pasted.Add(edge.Id);
        }
        return pasted;
    }

    private static void RewriteCells(ShapeSheetScope scope, Shape original, Shape shape, HashSet<string> included, IReadOnlyDictionary<string, string>? identities)
    {
        foreach (var name in scope.Names(original).ToArray())
        {
            if (scope.Cell(original, name) is not { } cell || string.IsNullOrEmpty(cell.Formula)) continue;
            var formula = FormulaReferenceRewriter.Rewrite(cell.Formula, token =>
            {
                var separator = token.IndexOf('!'); var sheet = token[..separator];
                var referenced = scope.FindSheet(original, sheet);
                if (referenced is not null && included.Contains(referenced.Id))
                {
                    // Keep inherited instance-relative references inherited. Their remapped template still resolves them.
                    if (identities is null || sheet.Equals("ParentShape", StringComparison.OrdinalIgnoreCase)
                        || !original.Cells.TryGetValue(name, out var local) || local.Inherited
                        || local.Formula.Equals("Inh", StringComparison.OrdinalIgnoreCase)) return null;
                    return "Sheet." + identities[referenced.Id] + token[separator..];
                }
                var value = scope.Resolve(original, token);
                if (value.IsError) throw new InvalidOperationException($"Cannot copy unresolved external reference '{token}' in '{name}'.");
                return FormulaReferenceRewriter.Literal(value);
            });
            if (formula == cell.Formula) continue;
            var local = cell.Clone(); local.Formula = formula; local.Inherited = false; shape.Cells[name] = local;
        }
    }

    private static DiagramMaster CloneMaster(DiagramMaster original, Dictionary<string, string> templateMap)
    {
        var master = original.Clone(); master.Id = NewId(); master.VisioId = null; master.VisioPart = null;
        var members = master.Children.Prepend(master.Shape).ToArray();
        var map = members.Select(s => s.Id).Concat(master.Groups.Select(g => g.Id)).Concat(master.Connectors.Select(c => c.Id))
            .Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => NewId(), StringComparer.Ordinal);
        string? Map(string? id) => id is not null ? map.GetValueOrDefault(id) : null;
        foreach (var shape in members)
        {
            templateMap[shape.Id] = map[shape.Id]; shape.Id = map[shape.Id];
            shape.GroupId = Map(shape.GroupId); shape.ContainerId = Map(shape.ContainerId); shape.FormulaParentId = Map(shape.FormulaParentId);
            shape.MasterId = null; shape.MasterShapeId = null; shape.MasterInstanceId = null;
            foreach (var cell in shape.Cells.Values) cell.Formula = FormulaReferenceRewriter.RemapSheets(cell.Formula, map);
        }
        foreach (var group in master.Groups)
        { group.Id = map[group.Id]; group.ParentId = Map(group.ParentId); group.AnchorShapeId = Map(group.AnchorShapeId); }
        foreach (var edge in master.Connectors)
        {
            edge.Id = map[edge.Id]; edge.SourceId = Map(edge.SourceId); edge.TargetId = Map(edge.TargetId); edge.GroupId = Map(edge.GroupId);
            foreach (var cell in edge.Cells.Values) cell.Formula = FormulaReferenceRewriter.RemapSheets(cell.Formula, map);
        }
        return master;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
}
