using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;
using DrawingSpace.ShapeSheet;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>
    /// Validates an explicitly ordered set of independent filled-shape operands. The first
    /// identity is the primary operand. Group/master/ShapeSheet materialization is an explicit
    /// separate operation; this command never silently destroys those live relationships.
    /// </summary>
    public IReadOnlyList<Shape> GetGeometryOperands(IReadOnlyList<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Count is < 2 or > 128 || orderedIds.Distinct(StringComparer.Ordinal).Count() != orderedIds.Count)
            throw new ArgumentException("Provide two to 128 distinct shape identities.", nameof(orderedIds));
        if (IsInteracting) throw new InvalidOperationException("Finish the active gesture before modifying shape geometry.");
        var index = Page.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var operands = orderedIds.Select(id => index.GetValueOrDefault(id) ?? throw new ArgumentException("An operand is not on this page.", nameof(orderedIds))).ToArray();
        foreach (var shape in operands)
        {
            if (Page.IsLocked(shape)) throw new InvalidOperationException("Unlock every operand before modifying its geometry.");
            if (shape.GroupId is not null || shape.IsGroupAnchor || shape.MasterId is not null || shape.Container is not null || shape.Kind == ShapeKind.Container
                || shape.FormulaParentId is not null || shape.UsesVisioCoordinates || shape.Cells.Count != 0)
                throw new InvalidOperationException("Boolean replacement requires independent shapes without live group, master or ShapeSheet bindings. Create path copies to operate on their rendered outlines without changing the originals.");
        }
        var ids = orderedIds.ToHashSet(StringComparer.Ordinal);
        if (Page.Shapes.Any(s => s.FormulaParentId is { } frame && ids.Contains(frame) || s.ContainerId is { } container && ids.Contains(container))
            || Page.Groups.Any(g => g.AnchorShapeId is { } anchor && ids.Contains(anchor)))
            throw new InvalidOperationException("An operand still owns a coordinate frame or container membership.");
        if (operands.Any(s => s.ContainerId != operands[0].ContainerId))
            throw new InvalidOperationException("Operands must belong to the same container or to no container.");
        if (operands[0].ContainerId is { } parent && Page.Find(parent)?.Container?.LockedMembership == true)
            throw new InvalidOperationException("Unlock container membership before replacing its members.");
        // Reject removal of a referenced shape instead of leaving a previously valid
        // formula silently pointing at a missing operand. String literals are not references.
        var references = new SheetReferenceIndex(Document, Page);
        foreach (var owner in Page.Shapes.Where(s => !ids.Contains(s.Id)))
        {
            foreach (var cell in owner.Cells.Values)
                FormulaReferenceRewriter.Rewrite(cell.Formula, token =>
                {
                    var bang = token.LastIndexOf('!');
                    if (bang > 0 && references.Resolve(owner, token[..bang]) is { } target && ids.Contains(target.Id))
                        throw new InvalidOperationException("Another shape's formula refers to an operand. Create path copies instead of replacing the live shapes.");
                    return null;
                });
        }
        return operands;
    }

    /// <summary>
    /// Atomically replaces independent operands with precomputed normalized geometry.
    /// The first operand keeps its runtime/Visio identity. All incident connectors survive,
    /// with their resolved endpoint positions/directions represented by custom result ports.
    /// Empty Boolean results should be handled as a no-op before calling this method.
    /// </summary>
    public Shape ReplaceShapesWithGeometry(IReadOnlyList<string> orderedIds, Shape replacement, string name = "Shape operation")
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var operands = GetGeometryOperands(orderedIds);
        if (replacement.Geometry.Count == 0 || replacement.MasterId is not null || replacement.GroupId is not null || replacement.IsGroupAnchor
            || replacement.Container is not null || replacement.Cells.Count != 0 || replacement.UsesVisioCoordinates || replacement.FormulaParentId is not null
            || replacement.ImageData is not null)
            throw new ArgumentException("Supply an independent editable vector result.", nameof(replacement));
        var primary = operands[0]; var ids = orderedIds.ToHashSet(StringComparer.Ordinal);
        var shapeIndex = Page.Shapes.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var affected = Page.Connectors.Where(e => e.SourceId is { } source && ids.Contains(source) || e.TargetId is { } target && ids.Contains(target)).ToArray();
        if (affected.Any(e => Page.Layers.FirstOrDefault(l => l.Id == e.LayerId)?.Locked == true))
            throw new InvalidOperationException("Unlock connected connector layers before replacing their attached shapes.");
        var result = replacement.Clone(); result.Id = primary.Id; result.VisioId = primary.VisioId;
        result.ContainerId = primary.ContainerId; result.LayerId = primary.LayerId; result.ConnectionPoints.Clear();
        if (!result.WorldMatrix.TryInvert(out var inverse)) throw new ArgumentException("The result has a singular transform.", nameof(replacement));
        var portMap = new Dictionary<(string Shape, string Point), string>();
        void AddPort(ConnectionPoint port)
        {
            if (result.ConnectionPoints.Count == 4096) throw new InvalidOperationException("The combined connection points exceed the document limit.");
            result.ConnectionPoints.Add(port);
        }
        foreach (var shape in operands)
        {
            foreach (var original in shape.ConnectionPoints)
            {
                var port = original.Clone(); port.Id = Guid.NewGuid().ToString("N");
                port.Position = inverse.Map(shape.WorldMatrix.Map(original.Position));
                port.Direction = inverse.MapVector(shape.WorldMatrix.MapVector(original.Direction));
                portMap[(shape.Id, original.Id)] = port.Id;
                AddPort(port);
            }
        }
        var changes = new List<(Connector Edge, bool Source, ResolvedEndpoint Endpoint)>();
        foreach (var edge in affected)
        {
            var source = edge.SourceId is { } sourceId ? shapeIndex.GetValueOrDefault(sourceId) : null;
            var target = edge.TargetId is { } targetId ? shapeIndex.GetValueOrDefault(targetId) : null;
            void Prepare(bool isSource, Shape? shape, string? pointId, PortSide side, PointD free, PointD toward)
            {
                if (shape is null || !ids.Contains(shape.Id)) return;
                var resolved = ConnectionEndpoints.Resolve(shape, pointId, side, free, toward);
                var key = (shape.Id, pointId ?? "cardinal:" + resolved.Side);
                if (!portMap.TryGetValue(key, out var id))
                {
                    id = Guid.NewGuid().ToString("N"); portMap[key] = id;
                    AddPort(new() { Id = id, Name = "Retained " + resolved.Side, Position = inverse.Map(resolved.Position),
                        Direction = inverse.MapVector(resolved.Direction), Incoming = true, Outgoing = true });
                }
                changes.Add((edge, isSource, resolved with { ShapeId = result.Id, PointId = id }));
            }
            Prepare(true, source, edge.SourcePointId, edge.SourcePort, edge.Start, target?.Bounds.Center ?? edge.End);
            Prepare(false, target, edge.TargetPointId, edge.TargetPort, edge.End, source?.Bounds.Center ?? edge.Start);
        }
        Execute(name, () =>
        {
            Page.Shapes.RemoveAll(s => ids.Contains(s.Id));
            Page.Shapes.Add(result);
            foreach (var change in changes) ConnectionEndpoints.Attach(change.Edge, change.Source, change.Endpoint, change.Endpoint.Position);
            Selection.Clear(); Selection.Add(result.Id);
        });
        return result;
    }
}
