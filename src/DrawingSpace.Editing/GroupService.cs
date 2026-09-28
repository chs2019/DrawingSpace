using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static class GroupService
{
    public static DiagramGroup Create(DiagramPage page, IReadOnlyCollection<string> shapeIds, IReadOnlyCollection<string>? connectorIds = null)
    {
        var selected = shapeIds.ToHashSet();
        if (selected.Count < 2) throw new ArgumentException("Grouping requires at least two shapes.", nameof(shapeIds));
        var shapes = page.Shapes.Where(s => selected.Contains(s.Id)).ToArray();
        if (shapes.Any(page.IsLocked)) throw new InvalidOperationException("Locked shapes cannot be grouped.");
        var roots = shapes.Where(s => s.GroupId is not null).Select(s => page.RootGroup(s.GroupId!)).Distinct().ToArray();
        foreach (var root in roots)
            if (page.GroupShapes(root).Any(s => !selected.Contains(s.Id))) throw new InvalidOperationException("Select the entire group before nesting it.");
        var group = new DiagramGroup();
        page.Groups.Add(group);
        foreach (var root in roots)
        {
            var child = page.Groups.FirstOrDefault(g => g.Id == root);
            if (child is null) { child = new() { Id = root }; page.Groups.Add(child); }
            child.ParentId = group.Id;
        }
        foreach (var shape in shapes.Where(s => s.GroupId is null)) shape.GroupId = group.Id;
        foreach (var edge in page.Connectors.Where(c => connectorIds?.Contains(c.Id) == true && c.GroupId is null)) edge.GroupId = group.Id;
        return group;
    }

    public static void Ungroup(DiagramPage page, string groupId, DiagramDocument? document = null)
    {
        var group = page.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null) return;
        var members = page.GroupShapes(groupId);
        if (members.Any(page.IsLocked) || page.Connectors.Any(c => page.IsInGroup(c.GroupId, groupId)
            && page.Layers.FirstOrDefault(l => l.Id == c.LayerId)?.Locked == true))
            throw new InvalidOperationException("Unlock the complete group before ungrouping it.");
        var anchor = page.Find(group.AnchorShapeId);
        if (anchor is not null)
        {
            var children = page.Shapes.Where(s => s.FormulaParentId == anchor.Id).ToArray();
            if (children.Any(page.IsLocked)) throw new InvalidOperationException("A locked coordinate child prevents ungrouping.");
            var scope = new ShapeSheetScope(document ?? new DiagramDocument { Pages = [page] }, page);
            // Resolve references before changing ANY frame. Nested groups retain their own frames.
            foreach (var child in children) CoordinateRebase.MaterializeParentReferences(scope, child, child);
            foreach (var child in children) CoordinateRebase.Reparent(page, child, anchor.FormulaParentId);
            if (!page.Groups.Any(g => g.Id != groupId && g.AnchorShapeId == anchor.Id)) anchor.IsGroupAnchor = false;
        }
        var parent = group.ParentId;
        foreach (var shape in page.Shapes.Where(s => s.GroupId == groupId)) shape.GroupId = parent;
        foreach (var edge in page.Connectors.Where(c => c.GroupId == groupId)) edge.GroupId = parent;
        foreach (var child in page.Groups.Where(g => g.ParentId == groupId)) child.ParentId = parent;
        page.Groups.Remove(group);
    }

    public static void RemoveEmpty(DiagramPage page)
    {
        for (var pass = 0; pass < 64; pass++)
        {
            var removed = page.Groups.RemoveAll(g => !page.Shapes.Any(s => s.GroupId == g.Id) && !page.Connectors.Any(c => c.GroupId == g.Id) && !page.Groups.Any(c => c.ParentId == g.Id));
            if (removed == 0) return;
        }
    }

    public static void Transform(DiagramPage page, IEnumerable<Shape> shapes, MatrixD transform, IEnumerable<Connector>? explicitConnectors = null)
    {
        if (!transform.IsFinite || Math.Abs(transform.Determinant) < 1e-14) throw new ArgumentException("The transform must be finite and invertible.", nameof(transform));
        var closure = ContainerService.TransformClosure(page, shapes);
        var ids = closure.Select(s => s.Id).ToHashSet();
        foreach (var shape in closure) shape.ApplyWorldTransform(transform);
        var explicitIds = explicitConnectors?.Select(c => c.Id).ToHashSet() ?? [];
        foreach (var edge in page.Connectors)
        {
            var both = edge.SourceId is not null && edge.TargetId is not null && ids.Contains(edge.SourceId) && ids.Contains(edge.TargetId);
            if (!both && !explicitIds.Contains(edge.Id)) continue;
            if (page.Layers.FirstOrDefault(l => l.Id == edge.LayerId)?.Locked == true) continue;
            edge.Waypoints = edge.Waypoints.Select(transform.Map).ToList();
            if (edge.SourceId is null) edge.Start = transform.Map(edge.Start);
            if (edge.TargetId is null) edge.End = transform.Map(edge.End);
            edge.LabelOffset = transform.MapVector(edge.LabelOffset);
        }
    }
}
