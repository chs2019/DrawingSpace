using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public static partial class MasterService
{
    public static MasterBundle InstantiateBundle(DiagramMaster master, PointD position)
    {
        ArgumentNullException.ThrowIfNull(master);
        if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position));
        var templates = master.Children.Prepend(master.Shape).ToArray();
        var identities = templates.Select(s => s.Id).Concat(master.Connectors.Select(c => c.Id))
            .Concat(master.Groups.Select(g => g.Id)).Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        string? Map(string? id) => id is not null && identities.TryGetValue(id, out var mapped) ? mapped : null;
        var instanceId = identities[master.Shape.Id];
        var delta = position - new PointD(master.Shape.X, master.Shape.Y);
        var shapes = templates.Select(template =>
        {
            var shape = template.Clone(true);
            shape.Id = identities[template.Id]; shape.X += delta.X; shape.Y += delta.Y;
            shape.MasterId = master.Id; shape.MasterShapeId = template.Id; shape.MasterInstanceId = instanceId;
            shape.GroupId = Map(template.GroupId); shape.ContainerId = Map(template.ContainerId);
            shape.FormulaParentId = Map(template.FormulaParentId);
            shape.LocalOverrides.Clear(); shape.Cells.Clear(); shape.Comments.Clear(); shape.Threads.Clear();
            return shape;
        }).ToArray();
        var groups = master.Groups.Select(original =>
        {
            var group = original.Clone(); group.Id = identities[original.Id]; group.VisioId = null;
            group.ParentId = Map(original.ParentId); group.AnchorShapeId = Map(original.AnchorShapeId);
            return group;
        }).ToList();
        var connectors = master.Connectors.Select(original =>
        {
            var edge = original.Clone(true); edge.Id = identities[original.Id];
            edge.SourceId = Map(original.SourceId); edge.TargetId = Map(original.TargetId); edge.GroupId = Map(original.GroupId);
            if (edge.SourceId is null) edge.SourcePointId = null;
            if (edge.TargetId is null) edge.TargetPointId = null;
            edge.Start += delta; edge.End += delta; edge.Waypoints = edge.Waypoints.Select(p => p + delta).ToList();
            RemapCells(edge.Cells, identities);
            return edge;
        }).ToArray();
        if (shapes.Length > 1 || connectors.Length > 0)
        {
            var outer = new DiagramGroup { Name = master.Name };
            foreach (var group in groups.Where(g => g.ParentId is null)) group.ParentId = outer.Id;
            foreach (var shape in shapes.Where(s => s.GroupId is null)) shape.GroupId = outer.Id;
            foreach (var edge in connectors.Where(c => c.GroupId is null)) edge.GroupId = outer.Id;
            groups.Add(outer);
        }
        return new(instanceId, shapes, connectors, groups);
    }

    /// <summary>Copies a library into a drawing without sharing identities or mutating the source.</summary>
    public static IReadOnlyList<DiagramMaster> Import(DiagramDocument target, IEnumerable<DiagramMaster> masters)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(masters);
        var result = new List<DiagramMaster>();
        foreach (var original in masters)
        {
            var master = original.Clone(); master.Id = Guid.NewGuid().ToString("N"); master.VisioPart = null;
            var shapes = master.Children.Prepend(master.Shape).ToArray();
            var identities = shapes.Select(s => s.Id).Concat(master.Connectors.Select(c => c.Id)).Concat(master.Groups.Select(g => g.Id))
                .Distinct().ToDictionary(id => id, _ => Guid.NewGuid().ToString("N"));
            string? Map(string? id) => id is not null && identities.TryGetValue(id, out var value) ? value : null;
            foreach (var shape in shapes)
            {
                shape.Id = identities[shape.Id]; shape.GroupId = Map(shape.GroupId); shape.ContainerId = Map(shape.ContainerId);
                shape.FormulaParentId = Map(shape.FormulaParentId); shape.MasterId = null; shape.MasterShapeId = null; shape.MasterInstanceId = null;
                RemapCells(shape.Cells, identities);
            }
            foreach (var group in master.Groups)
            { group.Id = identities[group.Id]; group.ParentId = Map(group.ParentId); group.AnchorShapeId = Map(group.AnchorShapeId); }
            foreach (var edge in master.Connectors)
            {
                edge.Id = identities[edge.Id]; edge.SourceId = Map(edge.SourceId); edge.TargetId = Map(edge.TargetId);
                edge.GroupId = Map(edge.GroupId); RemapCells(edge.Cells, identities);
            }
            result.Add(master);
        }
        if (target.Masters.Count + result.Count > 4096) throw new InvalidDataException("The drawing would exceed the master limit.");
        target.Masters.AddRange(result);
        return result;
    }

    private static void RemapCells(Dictionary<string, ShapeCell> cells, IReadOnlyDictionary<string, string> identities)
    {
        foreach (var cell in cells.Values)
            foreach (var (oldId, newId) in identities)
                cell.Formula = cell.Formula.Replace("Sheet." + oldId + "!", "Sheet." + newId + "!", StringComparison.OrdinalIgnoreCase);
    }
}
