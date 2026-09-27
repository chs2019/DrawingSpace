using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioReadContext
{
    private void ReadShape(DiagramPage page, XElement local, MatrixD parent, string? parentAnchor, string? groupId, uint? inheritedMaster,
        string part, string prefix, Dictionary<uint, string> shapeIds, bool inMaster, int depth)
    {
        options.CancellationToken.ThrowIfCancellationRequested();
        if (depth > options.MaximumGroupDepth || ++_shapeCount > options.MaximumShapes) throw new InvalidDataException("The Visio shape hierarchy exceeds its budget.");
        if (VisioXml.Attribute(local, "Del") == "1") return;
        var effective = EffectiveShape(local, inheritedMaster, out var masterId, out var templateId);
        var id = VisioXml.Id(local); var nativeId = (string?)local.Attribute(VisioNamespaces.DrawingSpace + "Id") ?? prefix + "-s" + id;
        if (!shapeIds.TryAdd(id, nativeId)) throw new InvalidDataException("Duplicate shape identifier in " + part + ".");
        var screen = new MatrixD(96, 0, 0, -96, 0, page.Height);
        var oneDimensional = VisioXml.Attribute(effective, "OneD") is "1" or "true" || VisioXml.Cell(effective, "BeginX") is not null && VisioXml.Cell(effective, "EndX") is not null;
        if (oneDimensional)
        {
            ReadConnector(page, local, effective, screen * parent, nativeId, id, groupId, part); return;
        }
        var width = Math.Clamp(VisioXml.Number(effective, "Width", 1), 1d / 96, 100000d / 96);
        var height = Math.Clamp(VisioXml.Number(effective, "Height", .5), 1d / 96, 100000d / 96);
        var pinX = VisioXml.Number(effective, "PinX", width / 2); var pinY = VisioXml.Number(effective, "PinY", height / 2);
        var locX = VisioXml.Number(effective, "LocPinX", width / 2); var locY = VisioXml.Number(effective, "LocPinY", height / 2);
        var rotation = VisioXml.Number(effective, "Angle") * 180 / Math.PI;
        var transform = MatrixD.Translation(pinX, pinY) * MatrixD.Rotation(rotation) * MatrixD.Scale(VisioXml.Boolean(effective, "FlipX") ? -1 : 1, VisioXml.Boolean(effective, "FlipY") ? -1 : 1) * MatrixD.Translation(-locX, -locY);
        var world = screen * parent * transform * new MatrixD(width, 0, 0, -height, 0, height);
        var shape = new Shape
        {
            Id = nativeId, VisioId = id, VisioMasterId = masterId, Name = VisioXml.Attribute(local, "Name", VisioXml.Attribute(local, "NameU", "Shape " + id)),
            Text = "", Style = ReadStyle(effective), GroupId = groupId, LayerId = Layer(effective, page),
            Locked = VisioXml.Boolean(effective, "LockMoveX") && VisioXml.Boolean(effective, "LockMoveY"), Cells = VisioXml.ReadCells(effective),
            UsesVisioCoordinates = true, FormulaParentId = parentAnchor, CoordinateWidth = width, CoordinateHeight = height
        };
        shape.WorldMatrix.TryInvert(out var inverse); shape.ApplyWorldTransform(world * inverse);
        shape.Geometry = ReadGeometry(effective, width, height, part, id);
        var hasForeign = VisioXml.Child(effective, "ForeignData") is not null;
        shape.Kind = Enum.TryParse<ShapeKind>((string?)local.Attribute(VisioNamespaces.DrawingSpace + "Kind"), out var kind) && Enum.IsDefined(kind)
            ? kind : shape.Geometry.Count == 0 && !hasForeign ? ShapeKind.Text : ShapeKind.Rectangle;
        ReadText(effective, shape); ReadProperties(effective, shape); ReadConnectionPoints(effective, shape, width, height);
        ReadForeignData(effective, shape, part); ReadNativeShapeMetadata(local, shape);
        if (VisioXml.Cell(effective, "TxtWidth") is not null || VisioXml.Cell(effective, "TxtHeight") is not null)
        {
            var textWidth = VisioXml.Number(effective, "TxtWidth", width); var textHeight = VisioXml.Number(effective, "TxtHeight", height);
            var tx = VisioXml.Number(effective, "TxtPinX", width / 2) - VisioXml.Number(effective, "TxtLocPinX", textWidth / 2);
            var ty = VisioXml.Number(effective, "TxtPinY", height / 2) - VisioXml.Number(effective, "TxtLocPinY", textHeight / 2);
            shape.TextBounds = new(tx / width, (height - ty - textHeight) / height, textWidth / width, textHeight / height);
            shape.TextRotation = -VisioXml.Number(effective, "TxtAngle") * 180 / Math.PI;
        }
        if (!inMaster && masterId is { } masterNumber && _masters.TryGetValue(masterNumber, out var master))
        {
            shape.MasterId = master.Id;
            shape.MasterShapeId = master.Children.Prepend(master.Shape).FirstOrDefault(s => s.VisioId == templateId)?.Id ?? master.Shape.Id;
            var localCells = VisioXml.ReadCells(local);
            foreach (var (cellName, cellValue) in shape.Cells)
                if (!localCells.TryGetValue(cellName, out var localCell) || localCell.Inherited) cellValue.Inherited = true;
            // Imported cached appearance is an explicit override only where the instance actually writes a cell.
            foreach (var pair in new[] { ("Width", "Width"), ("Height", "Height"), ("FillForegnd", "Style.Fill"), ("LineColor", "Style.Stroke"), ("LineWeight", "Style.StrokeWidth"), ("Char.Size", "Style.FontSize"), ("Char.Color", "Style.TextColor"), ("Char.Style", "Style.Bold") })
                if (localCells.TryGetValue(pair.Item1, out var cell) && !cell.Inherited) shape.LocalOverrides.Add(pair.Item2);
            if (VisioXml.Child(local, "Text") is not null) shape.LocalOverrides.Add("Text");
            if (VisioXml.Children(local, "Section").Any(s => VisioXml.Attribute(s, "N") == "Geometry")) shape.LocalOverrides.Add("Geometry");
            if (VisioXml.Children(local, "Section").Any(s => VisioXml.Attribute(s, "N") == "Connection")) shape.LocalOverrides.Add("ConnectionPoints");
        }
        page.Shapes.Add(shape);
        var isGroup = VisioXml.Attribute(effective, "Type") == "Group" || VisioXml.Child(effective, "Shapes") is not null;
        if (!isGroup) return;
        shape.IsGroupAnchor = true;
        if (shape.Geometry.Count == 0 && shape.Text.Length == 0) { shape.Style.Opacity = 0; shape.Kind = ShapeKind.Rectangle; }
        var group = new DiagramGroup { Id = (string?)local.Attribute(VisioNamespaces.DrawingSpace + "GroupId") ?? "group-" + nativeId, Name = shape.Name, ParentId = groupId, VisioId = id, AnchorShapeId = nativeId };
        shape.GroupId = group.Id; page.Groups.Add(group);
        var ownChildren = VisioXml.Child(local, "Shapes");
        var children = VisioXml.Children(ownChildren ?? VisioXml.Child(effective, "Shapes"), "Shape").ToArray();
        foreach (var child in children)
        {
            var item = child;
            if (ownChildren is null && masterId is not null)
            {
                item = new XElement(child); item.SetAttributeValue("MasterShape", VisioXml.Id(child)); item.SetAttributeValue("ID", checked(++_syntheticId));
                item.Attribute(VisioNamespaces.DrawingSpace + "Id")?.Remove();
            }
            ReadShape(page, item, parent * transform, shape.Id, group.Id, masterId, part, prefix, shapeIds, inMaster, depth + 1);
        }
    }

    private void ReadConnector(DiagramPage page, XElement local, XElement effective, MatrixD parent, string nativeId, uint id, string? groupId, string part)
    {
        var style = ReadStyle(effective);
        var start = parent.Map(new PointD(VisioXml.Number(effective, "BeginX"), VisioXml.Number(effective, "BeginY")));
        var end = parent.Map(new PointD(VisioXml.Number(effective, "EndX", 1), VisioXml.Number(effective, "EndY", 0)));
        var label = new Shape { Text = "", Style = style }; ReadText(effective, label);
        var edge = new Connector
        {
            Id = nativeId, VisioId = id, Start = start, End = end, Text = label.Text, Color = style.Stroke,
            Width = Math.Max(.1, style.StrokeWidth), Dashed = style.Dashed, LayerId = Layer(effective, page), GroupId = groupId,
            StartArrow = ReadArrow((int)VisioXml.Number(effective, "BeginArrow")), EndArrow = ReadArrow((int)VisioXml.Number(effective, "EndArrow")), Cells = VisioXml.ReadCells(effective)
        };
        var points = new List<PointD>();
        var width = Math.Max(1d / 96, VisioXml.Number(effective, "Width", start.Distance(end) / 96)); var height = Math.Max(1d / 96, VisioXml.Number(effective, "Height", 1d / 96));
        var x = VisioXml.Number(effective, "PinX", (VisioXml.Number(effective, "BeginX") + VisioXml.Number(effective, "EndX", 1)) / 2);
        var y = VisioXml.Number(effective, "PinY", (VisioXml.Number(effective, "BeginY") + VisioXml.Number(effective, "EndY")) / 2);
        var angle = VisioXml.Number(effective, "Angle", Math.Atan2(VisioXml.Number(effective, "EndY") - VisioXml.Number(effective, "BeginY"), VisioXml.Number(effective, "EndX", 1) - VisioXml.Number(effective, "BeginX")));
        var geometryToWorld = parent * MatrixD.Translation(x, y) * MatrixD.Rotation(angle * 180 / Math.PI)
            * MatrixD.Translation(-VisioXml.Number(effective, "LocPinX", width / 2), -VisioXml.Number(effective, "LocPinY", height / 2)) * new MatrixD(width, 0, 0, -height, 0, height);
        foreach (var segment in ReadGeometry(effective, width, height, part, id).SelectMany(g => g.Segments))
            if (segment.Verb is GeometryVerb.Move or GeometryVerb.Line) points.Add(geometryToWorld.Map(segment.End));
        if (points.Count > 2 && points[0].Distance(start) < 2 && points[^1].Distance(end) < 2) edge.Waypoints = points.Skip(1).SkipLast(1).ToList();
        edge.Kind = edge.Waypoints.Count > 0 || VisioXml.Number(effective, "ShapeRouteStyle", 1) != 16 ? ConnectorKind.Orthogonal : ConnectorKind.Straight;
        if (Enum.TryParse<ConnectorKind>((string?)local.Attribute(VisioNamespaces.DrawingSpace + "ConnectorKind"), out var kind) && Enum.IsDefined(kind)) edge.Kind = kind;
        if (local.Element(VisioNamespaces.DrawingSpace + "Connector") is { } metadata)
        {
            if (Enum.TryParse<LineJumpStyle>((string?)metadata.Attribute("LineJumps"), out var jumps)) edge.LineJumps = jumps;
            if (double.TryParse((string?)metadata.Attribute("JumpSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) edge.JumpSize = size;
            if (double.TryParse((string?)metadata.Attribute("LabelPosition"), NumberStyles.Float, CultureInfo.InvariantCulture, out var position)) edge.LabelPosition = position;
        }
        page.Connectors.Add(edge);
    }

    private static ArrowHead ReadArrow(int value) => value switch { 0 => ArrowHead.None, 1 => ArrowHead.Open, 4 or 10 or 11 => ArrowHead.Diamond, _ => ArrowHead.Triangle };

    private void ReadConnects(XElement? connects, DiagramPage page, Dictionary<uint, string> ids, string part)
    {
        foreach (var link in VisioXml.Children(connects, "Connect"))
        {
            var from = VisioXml.Id(link, "FromSheet"); var to = VisioXml.Id(link, "ToSheet");
            if (!ids.TryGetValue(from, out var edgeId) || page.Connectors.FirstOrDefault(c => c.Id == edgeId) is not { } edge || !ids.TryGetValue(to, out var shapeId) || page.Find(shapeId) is not { } shape)
            { Warn("UnmappedConnection", part, "A connection record references an unsupported or missing object.", from); continue; }
            var cell = VisioXml.Attribute(link, "FromCell"); var targetCell = VisioXml.Attribute(link, "ToCell");
            string? point = null;
            if (targetCell.StartsWith("Connections.X", StringComparison.OrdinalIgnoreCase) && int.TryParse(targetCell[13..], out var index)) point = "connection-" + Math.Max(0, index - 1);
            if (point is not null && shape.ConnectionPoints.All(p => p.Id != point)) point = null;
            if (cell.StartsWith("Begin", StringComparison.OrdinalIgnoreCase)) { edge.SourceId = shapeId; edge.SourcePointId = point; edge.SourcePort = PortSide.Auto; }
            else if (cell.StartsWith("End", StringComparison.OrdinalIgnoreCase)) { edge.TargetId = shapeId; edge.TargetPointId = point; edge.TargetPort = PortSide.Auto; }
        }
    }

    private static void ReadConnectionPoints(XElement effective, Shape shape, double width, double height)
    {
        foreach (var row in SectionRows(effective, "Connection"))
        {
            var type = (int)VisioXml.Number(row, "Type");
            shape.ConnectionPoints.Add(new()
            {
                Id = "connection-" + VisioXml.Id(row, "IX"), Name = VisioXml.Attribute(row, "N", "Connection point"),
                Position = new(VisioXml.Number(row, "X", width / 2) / width, 1 - VisioXml.Number(row, "Y", height / 2) / height),
                Direction = new(VisioXml.Number(row, "DirX"), -VisioXml.Number(row, "DirY")), Incoming = type is 0 or 2, Outgoing = type is 1 or 2
            });
        }
    }

    private static void ReadProperties(XElement effective, Shape shape)
    {
        foreach (var row in SectionRows(effective, "Property"))
        { var name = VisioXml.Attribute(row, "N", VisioXml.Value(row, "Label", "Property")); shape.Data[name] = VisioXml.Value(row, "Value"); }
        foreach (var row in SectionRows(effective, "Hyperlink"))
            shape.Hyperlinks.Add(new(VisioXml.Value(row, "Description"), VisioXml.Value(row, "Address"), VisioXml.Value(row, "SubAddress")));
    }

    private void ReadForeignData(XElement effective, Shape shape, string part)
    {
        var foreign = VisioXml.Child(effective, "ForeignData"); if (foreign is null) return;
        var imagePart = RelatedPart(part, foreign); if (imagePart is null || package.Get(imagePart) is not { } bytes) { Warn("MissingImage", part, "An embedded image could not be resolved.", shape.VisioId); return; }
        if (bytes.Length > 8 * 1024 * 1024) { Warn("ImageBudget", part, "An embedded image exceeds the editing budget and is preserved only in the original package.", shape.VisioId); return; }
        var extension = Path.GetExtension(imagePart).ToLowerInvariant();
        var contentType = extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".bmp" => "image/bmp", ".webp" => "image/webp", _ => null };
        if (contentType is null) { Warn("ForeignData", part, "Unsupported embedded image type is preserved without executing a converter: " + extension, shape.VisioId); return; }
        shape.ImageData = bytes.ToArray(); shape.ImageContentType = contentType;
    }

    private void ReadNativeShapeMetadata(XElement local, Shape shape)
    {
        if (local.Element(VisioNamespaces.DrawingSpace + "Shape") is not { } metadata) return;
        shape.ContainerId = (string?)metadata.Attribute("ContainerId"); shape.MasterInstanceId = (string?)metadata.Attribute("MasterInstanceId");
        if (metadata.Element(VisioNamespaces.DrawingSpace + "Container") is { } container)
        {
            shape.Container = new();
            if (Enum.TryParse<ContainerLayout>((string?)container.Attribute("Layout"), out var layout)) shape.Container.Layout = layout;
            if (double.TryParse((string?)container.Attribute("Padding"), NumberStyles.Float, CultureInfo.InvariantCulture, out var padding)) shape.Container.Padding = padding;
            if (double.TryParse((string?)container.Attribute("HeaderHeight"), NumberStyles.Float, CultureInfo.InvariantCulture, out var header)) shape.Container.HeaderHeight = header;
            shape.Container.AutoResize = (string?)container.Attribute("AutoResize") != "false"; shape.Container.LockedMembership = (string?)container.Attribute("LockedMembership") == "true";
        }
        if (metadata.Element(VisioNamespaces.DrawingSpace + "Comments") is { } comments)
        {
            foreach (var item in comments.Elements(VisioNamespaces.DrawingSpace + "Comment")) shape.Comments.Add(item.Value);
            foreach (var item in comments.Elements(VisioNamespaces.DrawingSpace + "Thread"))
            {
                try { shape.Threads.Add(ModelJson.Deserialize<CommentThread>(item.Value)); }
                catch (JsonException) { Warn("CommentMetadata", "", "Invalid DrawingSpace comment metadata was ignored.", shape.VisioId); }
            }
        }
    }
}
