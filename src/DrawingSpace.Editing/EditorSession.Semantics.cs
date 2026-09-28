using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Editing;

public sealed partial class EditorSession
{
    public IReadOnlyList<Shape> TransformShapes => ContainerService.TransformClosure(Page, EditableShapes);

    public void TransformSelection(MatrixD transform, string name = "Transform selection")
    {
        if (EditableShapes.Count == 0 && SelectedConnectors.Count == 0) return;
        Execute(name, () => GroupService.Transform(Page, EditableShapes, transform, SelectedConnectors));
    }

    public void SetFormula(string shapeId, string cellName, string formula, bool force = false)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) throw new InvalidOperationException("The shape is locked.");
        Execute("Edit " + cellName + " formula", () => ShapeSheetService.SetCell(Document, Page, shape, cellName, formula, force));
    }

    public void RemoveFormula(string shapeId, string cellName)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) throw new InvalidOperationException("The shape is locked.");
        Execute("Remove " + cellName + " formula", () =>
        {
            var key = shape.Cells.Keys.FirstOrDefault(k => k.Equals(cellName, StringComparison.OrdinalIgnoreCase));
            if (key is not null) shape.Cells.Remove(key);
        });
    }

    public DiagramMaster CreateMaster(string shapeId, string name)
    {
        var source = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        var master = MasterAuthoring.Capture(Document, Page, [source.Id], name);
        Execute("Create master", () => Document.Masters.Add(master));
        return master;
    }

    public IReadOnlyList<Shape> InsertMaster(string masterId, PointD location)
    {
        var master = Document.Masters.FirstOrDefault(m => m.Id == masterId) ?? throw new ArgumentException("Master not found.", nameof(masterId));
        var bundle = MasterService.InstantiateBundle(master, location);
        Execute("Insert " + master.Name, () =>
        {
            Selection.Clear();
            Page.Groups.AddRange(bundle.Groups);
            foreach (var shape in bundle.Shapes) { shape.LayerId = Page.Layers[0].Id; Page.Shapes.Add(shape); Selection.Add(shape.Id); }
            foreach (var edge in bundle.Connectors) { edge.LayerId = Page.Layers[0].Id; Page.Connectors.Add(edge); Selection.Add(edge.Id); }
            // Placement is instance-local. Master Pin cells must not move a new instance back onto its template.
            foreach (var shape in bundle.Shapes.Where(s => s.FormulaParentId is null))
            {
                var position = ShapeCoordinates.Read(Page, shape);
                var x = shape.UsesVisioCoordinates ? position.PinX : shape.Bounds.Center.X / 96;
                var y = shape.UsesVisioCoordinates ? position.PinY : (Page.Height - shape.Bounds.Center.Y) / 96;
                shape.Cells["PinX"] = new() { Value = x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Unit = "IN" };
                shape.Cells["PinY"] = new() { Value = y.ToString("R", System.Globalization.CultureInfo.InvariantCulture), Unit = "IN" };
            }
            if (bundle.Shapes.Count == 1) ContainerService.Assign(Page, bundle.Shapes[0], ContainerService.FindContainer(Page, bundle.Shapes[0])?.Id);
        });
        return bundle.Shapes;
    }

    public IReadOnlyList<DiagramMaster> ImportMasters(IEnumerable<DiagramMaster> masters)
    {
        IReadOnlyList<DiagramMaster> imported = [];
        Execute("Import stencil masters", () => imported = MasterService.Import(Document, masters));
        return imported;
    }

    public void UpdateMaster(string masterId, Action<DiagramMaster> update)
    {
        var master = Document.Masters.FirstOrDefault(m => m.Id == masterId) ?? throw new ArgumentException("Master not found.", nameof(masterId));
        ArgumentNullException.ThrowIfNull(update);
        Execute("Edit master", () =>
        {
            var before = master.Clone(); update(master);
            MasterAuthoring.SynchronizeCells(before, master); master.Revision++;
        });
    }

    public void ResetMasterOverride(string shapeId, string property)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) throw new InvalidOperationException("The shape is locked.");
        Execute("Restore inherited " + property, () =>
        {
            shape.LocalOverrides.RemoveAll(p => p.Equals(property, StringComparison.OrdinalIgnoreCase));
            if (shape.Cells.ContainsKey(property)) shape.Cells.Remove(property);
        });
    }

    public Shape CreateContainer(string name = "Container", ContainerLayout layout = ContainerLayout.Free)
    {
        var members = EditableShapes.ToArray();
        var bounds = members.Length > 0 ? members.Select(s => s.WorldBounds).Aggregate(RectD.Union) : new RectD(180, 180, 360, 240);
        var container = new Shape
        {
            Name = name, Text = name, Kind = ShapeKind.Container, X = bounds.X - 24, Y = bounds.Y - 56,
            Width = bounds.Width + 48, Height = bounds.Height + 80, LayerId = Page.Layers[0].Id,
            Container = new() { Layout = layout, Padding = 24, HeaderHeight = 32, AutoResize = layout == ContainerLayout.Free }
        };
        container.Style.Fill = "#F6F8FC"; container.Style.Stroke = "#607DAB";
        Execute("Insert container", () =>
        {
            Page.Shapes.Insert(0, container);
            foreach (var member in members.Where(s => s.ContainerId is null || !members.Any(m => m.Id == s.ContainerId))) ContainerService.Assign(Page, member, container.Id);
            Selection.Clear(); Selection.Add(container.Id);
        });
        return container;
    }

    public void SetContainerMembership(string shapeId, string? containerId)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) throw new InvalidOperationException("The shape is locked.");
        Execute("Change container membership", () =>
        {
            ContainerService.Assign(Page, shape, containerId);
            if (Page.Find(containerId) is { Container.AutoResize: true } container) ContainerService.Fit(Page, container);
        });
    }

    public Shape AddLane(string containerId, string name)
    {
        var parent = Page.Find(containerId) ?? throw new ArgumentException("Container not found.", nameof(containerId));
        if (parent.Container is null) throw new ArgumentException("The shape is not a semantic container.", nameof(containerId));
        var lane = new Shape { Name = name, Text = name, Kind = ShapeKind.Container, Container = new() { AutoResize = false }, ContainerId = parent.Id, LayerId = parent.LayerId };
        lane.Style.Fill = "#FFFFFF";
        Execute("Insert swimlane", () =>
        {
            if (parent.Container.Layout == ContainerLayout.Free) { parent.Container.Layout = ContainerLayout.HorizontalLanes; parent.Container.AutoResize = false; }
            Page.Shapes.Insert(Page.Shapes.IndexOf(parent) + 1, lane); ContainerService.LayoutLanes(Page, parent);
            Selection.Clear(); Selection.Add(lane.Id);
        });
        return lane;
    }

    public void FitContainer(string containerId)
    {
        var container = Page.Find(containerId) ?? throw new ArgumentException("Container not found.", nameof(containerId));
        Execute("Fit container to contents", () => ContainerService.Fit(Page, container));
    }

    public void SetText(string shapeId, string text)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) return;
        Execute("Edit shape text", () => RichTextOperations.ReplaceAll(shape, text));
    }

    public void FormatText(string shapeId, int start, int length, Action<TextSpan> update)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) return;
        Execute("Format text range", () => RichTextOperations.Format(shape, start, length, update));
    }

    public void FormatParagraph(string shapeId, int offset, Action<ParagraphFormat> update)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) return;
        Execute("Format paragraph", () => RichTextOperations.FormatParagraph(shape, offset, update));
    }

    public ConnectionPoint AddConnectionPoint(string shapeId, PointD world, PointD direction = default)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        if (Page.IsLocked(shape)) throw new InvalidOperationException("The shape is locked.");
        if (!shape.WorldMatrix.TryInvert(out var inverse)) throw new InvalidOperationException("Shape transform is singular.");
        var point = new ConnectionPoint { Position = inverse.Map(world), Direction = direction };
        Execute("Insert connection point", () =>
        {
            shape.ConnectionPoints.Add(point);
            if (!shape.LocalOverrides.Contains("ConnectionPoints")) shape.LocalOverrides.Add("ConnectionPoints");
        });
        return point;
    }

    public void ReattachConnector(string connectorId, bool source, string? shapeId, PortSide port, string? pointId, PointD freePoint)
    {
        var connector = Page.Connectors.FirstOrDefault(c => c.Id == connectorId) ?? throw new ArgumentException("Connector not found.", nameof(connectorId));
        if (Page.Layers.FirstOrDefault(l => l.Id == connector.LayerId)?.Locked == true) throw new InvalidOperationException("The connector layer is locked.");
        if (shapeId is not null && Page.Find(shapeId) is null) throw new ArgumentException("Target shape not found.", nameof(shapeId));
        if (pointId is not null && Page.Find(shapeId)?.ConnectionPoints.Any(p => p.Id == pointId) != true) throw new ArgumentException("Connection point not found.", nameof(pointId));
        if (!freePoint.IsFinite) throw new ArgumentOutOfRangeException(nameof(freePoint));
        if (shapeId is not null && Page.Find(shapeId) is { } attached && Page.IsLocked(attached)) throw new InvalidOperationException("The target shape is locked.");
        if (pointId is not null && Page.Find(shapeId)?.ConnectionPoints.First(p => p.Id == pointId) is { } point && !(source ? point.Outgoing : point.Incoming))
            throw new InvalidOperationException("The connection point does not accept this endpoint direction.");
        Execute("Reconnect endpoint", () =>
        {
            if (source) { connector.SourceId = shapeId; connector.SourcePort = port; connector.SourcePointId = pointId; connector.Start = freePoint; }
            else { connector.TargetId = shapeId; connector.TargetPort = port; connector.TargetPointId = pointId; connector.End = freePoint; }
        });
    }

    public void SetWaypoints(string connectorId, IEnumerable<PointD> waypoints)
    {
        var connector = Page.Connectors.FirstOrDefault(c => c.Id == connectorId) ?? throw new ArgumentException("Connector not found.", nameof(connectorId));
        if (Page.Layers.FirstOrDefault(l => l.Id == connector.LayerId)?.Locked == true) throw new InvalidOperationException("The connector layer is locked.");
        ArgumentNullException.ThrowIfNull(waypoints);
        var points = waypoints.Take(4097).ToList();
        if (points.Count > 4096 || points.Any(p => !p.IsFinite)) throw new ArgumentException("Waypoints must be finite and within the 4096-point limit.", nameof(waypoints));
        Execute("Edit connector route", () => connector.Waypoints = points);
    }

    public void AddComment(string shapeId, string author, string text, string? threadId = null)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(author); ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Execute("Add comment", () =>
        {
            var thread = shape.Threads.FirstOrDefault(t => t.Id == threadId);
            if (threadId is not null && thread is null) throw new ArgumentException("Comment thread not found.", nameof(threadId));
            if (thread is null) { thread = new(); shape.Threads.Add(thread); }
            thread.Messages.Add(new(Guid.NewGuid().ToString("N"), author, text, DateTimeOffset.UtcNow));
        });
    }

    public void ResolveComment(string shapeId, string threadId, bool resolved)
    {
        var shape = Page.Find(shapeId) ?? throw new ArgumentException("Shape not found.", nameof(shapeId));
        var thread = shape.Threads.FirstOrDefault(t => t.Id == threadId) ?? throw new ArgumentException("Thread not found.", nameof(threadId));
        Execute(resolved ? "Resolve comment" : "Reopen comment", () => thread.Resolved = resolved);
    }
}
