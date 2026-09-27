using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioWriteContext
{
    private readonly Dictionary<string, Dictionary<string, uint>> _masterShapeIds = new(StringComparer.Ordinal);

    private void WritePageContents(DiagramPage page, string part, bool master)
    {
        var oldRoot = _package.Xml(part)?.Root;
        var oldShapes = oldRoot?.Descendants().Where(e => e.Name.LocalName == "Shape").GroupBy(e => VisioXml.Id(e)).ToDictionary(g => g.Key, g => g.First()) ?? [];
        var ids = new Dictionary<string, uint>(StringComparer.Ordinal);
        var groups = page.Groups.ToDictionary(g => g.Id);
        var anchors = page.Groups.Where(g => g.AnchorShapeId is not null && page.Find(g.AnchorShapeId) is not null).ToDictionary(g => g.Id, g => page.Find(g.AnchorShapeId)!);
        AssignIds(page.Shapes.Select(s => (s.Id, s.VisioId)).Concat(page.Connectors.Select(c => (c.Id, c.VisioId)))
            .Concat(page.Groups.Where(g => !anchors.ContainsKey(g.Id)).Select(g => (g.Id, g.VisioId))), ids);
        foreach (var (id, anchor) in anchors) ids[id] = ids[anchor.Id];
        if (master) _masterShapeIds[page.Id] = ids;
        var root = oldRoot is null ? new XElement(_v + (master ? "MasterContents" : "PageContents")) : new XElement(oldRoot);
        root.Name = _v + (master ? "MasterContents" : "PageContents"); root.SetAttributeValue(XNamespace.Xmlns + "r", _r.NamespaceName);
        if (options.IncludeDrawingSpaceMetadata) root.SetAttributeValue(XNamespace.Xmlns + "ds", _d.NamespaceName);
        var shapes = new XElement(_v + "Shapes"); var connections = new XElement(_v + "Connects");
        var pageFrame = ShapeCoordinates.PageFrame(page); var active = new HashSet<string>();
        XElement ShapeNode(Shape shape, MatrixD parent)
            => WriteShape(page, shape, ids[shape.Id], parent, oldShapes.GetValueOrDefault(ids[shape.Id]), part, master);
        XElement GroupNode(DiagramGroup group, MatrixD parent)
        {
            if (!active.Add(group.Id) || active.Count > 64) throw new InvalidDataException("Cyclic export group hierarchy.");
            var members = page.GroupShapes(group.Id);
            var bounds = members.Count > 0 ? members.Select(s => s.WorldBounds).Aggregate(RectD.Union) : new RectD(0, 0, 96, 96);
            var anchor = anchors.GetValueOrDefault(group.Id) ?? new Shape
            {
                Id = group.Id + "-anchor", Name = group.Name, Text = "", X = bounds.X, Y = bounds.Y, Width = Math.Max(1, bounds.Width), Height = Math.Max(1, bounds.Height),
                IsGroupAnchor = true, Style = new() { Opacity = 0 }
            };
            var value = LocalTransform(anchor, parent);
            var node = WriteShape(page, anchor, ids[group.Id], parent, oldShapes.GetValueOrDefault(ids[group.Id]), part, master);
            node.SetAttributeValue("Type", "Group");
            if (options.IncludeDrawingSpaceMetadata) node.SetAttributeValue(_d + "GroupId", group.Id);
            var childFrame = parent * MatrixD.Translation(value.PinX, value.PinY) * MatrixD.Rotation(value.Angle * 180 / Math.PI)
                * MatrixD.Scale(value.FlipX ? -1 : 1, value.FlipY ? -1 : 1) * MatrixD.Translation(-value.LocPinX, -value.LocPinY);
            var children = new XElement(_v + "Shapes");
            foreach (var shape in page.Shapes.Where(s => s.GroupId == group.Id && s.Id != group.AnchorShapeId)) children.Add(ShapeNode(shape, childFrame));
            foreach (var child in page.Groups.Where(g => g.ParentId == group.Id)) children.Add(GroupNode(child, childFrame));
            foreach (var edge in page.Connectors.Where(c => c.GroupId == group.Id)) children.Add(WriteConnector(page, edge, ids, childFrame, connections, oldShapes.GetValueOrDefault(ids[edge.Id]), part));
            ReplaceChild(node, "Shapes", children); active.Remove(group.Id); return node;
        }
        foreach (var shape in page.Shapes.Where(s => s.GroupId is null || !groups.ContainsKey(s.GroupId))) shapes.Add(ShapeNode(shape, pageFrame));
        foreach (var group in page.Groups.Where(g => g.ParentId is null || !groups.ContainsKey(g.ParentId))) shapes.Add(GroupNode(group, pageFrame));
        foreach (var edge in page.Connectors.Where(c => c.GroupId is null || !groups.ContainsKey(c.GroupId))) shapes.Add(WriteConnector(page, edge, ids, pageFrame, connections, oldShapes.GetValueOrDefault(ids[edge.Id]), part));
        ReplaceChild(root, "Shapes", shapes); ReplaceChild(root, "Connects", connections);
        if (options.IncludeDrawingSpaceMetadata)
        {
            root.Element(_d + "Page")?.Remove(); root.Add(new XElement(_d + "Page", new XAttribute("Background", page.Background)));
        }
        SetXml(part, root, master ? "application/vnd.ms-visio.master+xml" : "application/vnd.ms-visio.page+xml");
    }

    private static LocalShapeTransform LocalTransform(Shape shape, MatrixD parent)
    {
        if (!parent.TryInvert(out var inverse)) throw new InvalidDataException("The export coordinate frame is singular.");
        var matrix = inverse * shape.WorldMatrix * new MatrixD(1, 0, 0, -1, 0, 1);
        var width = Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B); var height = Math.Abs(matrix.Determinant) / Math.Max(width, 1e-16);
        var pin = matrix.Map(new PointD(.5, .5));
        return new(width, height, pin.X, pin.Y, width / 2, height / 2, Math.Atan2(matrix.B, matrix.A), false, matrix.Determinant < 0,
            (matrix.A * matrix.C + matrix.B * matrix.D) / Math.Max(width * height, 1e-16));
    }

    private XElement WriteShape(DiagramPage page, Shape shape, uint id, MatrixD parent, XElement? old, string part, bool inMaster)
    {
        var node = old is null ? new XElement(_v + "Shape") : new XElement(old);
        node.Name = _v + "Shape"; node.SetAttributeValue("ID", id); node.SetAttributeValue("Name", shape.Name); node.SetAttributeValue("NameU", shape.Name);
        node.SetAttributeValue("Type", shape.IsGroupAnchor ? "Group" : shape.ImageData is not null ? "Foreign" : "Shape"); node.SetAttributeValue("OneD", "0");
        VisioXml.Child(node, "Shapes")?.Remove();
        if (options.IncludeDrawingSpaceMetadata) { node.SetAttributeValue(_d + "Id", shape.Id); node.SetAttributeValue(_d + "Kind", shape.Kind); }
        if (!inMaster && shape.MasterId is { } master && _masterIds.TryGetValue(master, out var masterId))
        {
            node.SetAttributeValue("Master", masterId);
            node.SetAttributeValue("MasterShape", shape.MasterShapeId is { } template && _masterShapeIds.TryGetValue(master, out var templates) && templates.TryGetValue(template, out var templateId) ? templateId.ToString(CultureInfo.InvariantCulture) : null);
        }
        else { node.SetAttributeValue("Master", null); node.SetAttributeValue("MasterShape", null); }
        WriteCells(node, shape.Cells);
        var value = LocalTransform(shape, parent);
        void GeometryCell(string name, double number, string unit)
        {
            var original = shape.Cells.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            var formula = original is not null && double.TryParse(original.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cached) && Math.Abs(cached - number) <= 1e-8 * Math.Max(1, Math.Abs(number)) ? original.Formula : "";
            if (formula.Length == 0 && original?.Formula.Length > 0 && original.Formula != "Inh") Warn("CoordinateFormulaBaked", part, "The edited coordinate was materialized because its original formula uses a different coordinate frame: " + name, id);
            VisioXml.SetCell(node, name, number, formula, unit);
        }
        GeometryCell("Width", value.Width, "IN"); GeometryCell("Height", value.Height, "IN"); GeometryCell("PinX", value.PinX, "IN"); GeometryCell("PinY", value.PinY, "IN");
        GeometryCell("LocPinX", value.LocPinX, "IN"); GeometryCell("LocPinY", value.LocPinY, "IN"); GeometryCell("Angle", value.Angle, "RAD");
        GeometryCell("FlipX", value.FlipX ? 1 : 0, "BOOL"); GeometryCell("FlipY", value.FlipY ? 1 : 0, "BOOL");
        VisioXml.SetCell(node, "LockMoveX", shape.Locked ? 1 : 0, ""); VisioXml.SetCell(node, "LockMoveY", shape.Locked ? 1 : 0, "");
        var layer = page.Layers.FindIndex(l => l.Id == shape.LayerId); VisioXml.SetCell(node, "LayerMember", layer < 0 ? "" : layer.ToString(CultureInfo.InvariantCulture), "");
        WriteStyle(node, shape.Style); WriteText(node, shape);
        WriteGeometry(node, shape.IsGroupAnchor && shape.Geometry.Count == 0 ? [] : ShapeOutlines.Create(shape), value.Width, value.Height, value.Shear, part, id);
        WriteConnectionPoints(node, shape, value.Width, value.Height);
        WriteProperties(node, shape); WriteImage(node, shape, part, id);
        if (shape.TextBounds is { } box)
        {
            VisioXml.SetCell(node, "TxtWidth", box.Width * value.Width, "", "IN"); VisioXml.SetCell(node, "TxtHeight", box.Height * value.Height, "", "IN");
            VisioXml.SetCell(node, "TxtPinX", (box.X + box.Width / 2) * value.Width, "", "IN"); VisioXml.SetCell(node, "TxtPinY", (1 - box.Y - box.Height / 2) * value.Height, "", "IN");
            VisioXml.SetCell(node, "TxtLocPinX", box.Width * value.Width / 2, "", "IN"); VisioXml.SetCell(node, "TxtLocPinY", box.Height * value.Height / 2, "", "IN");
            VisioXml.SetCell(node, "TxtAngle", -shape.TextRotation * Math.PI / 180, "", "RAD");
        }
        if (Math.Abs(value.Shear) > 1e-8) Warn("AffineText", part, "Affine shear was baked into vector geometry. Visio text and raster-image shear require a native grouped transform.", id);
        if (options.IncludeDrawingSpaceMetadata) WriteShapeMetadata(node, shape);
        return node;
    }

    private XElement WriteConnector(DiagramPage page, Connector edge, Dictionary<string, uint> ids, MatrixD parent, XElement connects, XElement? old, string part)
    {
        var id = ids[edge.Id]; var node = old is null ? new XElement(_v + "Shape") : new XElement(old);
        node.Name = _v + "Shape"; node.SetAttributeValue("ID", id); node.SetAttributeValue("Type", "Shape"); node.SetAttributeValue("OneD", "1"); node.SetAttributeValue("NameU", "Dynamic connector." + id);
        if (options.IncludeDrawingSpaceMetadata) { node.SetAttributeValue(_d + "Id", edge.Id); node.SetAttributeValue(_d + "ConnectorKind", edge.Kind); }
        WriteCells(node, edge.Cells);
        if (!parent.TryInvert(out var inverse)) throw new InvalidDataException("The connector coordinate frame is singular.");
        var route = edge.Waypoints.Count > 0 ? new[] { Endpoint(page, edge, true) }.Concat(edge.Waypoints).Append(Endpoint(page, edge, false)).ToArray() : _router.Route(page, edge).Points.ToArray();
        var points = route.Select(inverse.Map).ToArray();
        if (points.Length < 2) points = [inverse.Map(edge.Start), inverse.Map(edge.End)];
        var bounds = RectD.Bounds(points); var width = Math.Max(1d / 96, bounds.Width); var height = Math.Max(1d / 96, bounds.Height);
        VisioXml.SetCell(node, "BeginX", points[0].X, "", "IN"); VisioXml.SetCell(node, "BeginY", points[0].Y, "", "IN");
        VisioXml.SetCell(node, "EndX", points[^1].X, "", "IN"); VisioXml.SetCell(node, "EndY", points[^1].Y, "", "IN");
        VisioXml.SetCell(node, "Width", width, "", "IN"); VisioXml.SetCell(node, "Height", height, "", "IN");
        VisioXml.SetCell(node, "PinX", bounds.Left + width / 2, "", "IN"); VisioXml.SetCell(node, "PinY", bounds.Top + height / 2, "", "IN");
        VisioXml.SetCell(node, "LocPinX", width / 2, "", "IN"); VisioXml.SetCell(node, "LocPinY", height / 2, "", "IN"); VisioXml.SetCell(node, "Angle", 0, "", "RAD");
        VisioXml.SetCell(node, "ShapeRouteStyle", edge.Kind == ConnectorKind.Straight ? 16 : 1, "");
        var style = new ShapeStyle { Fill = "#00FFFFFF", Stroke = edge.Color, StrokeWidth = edge.Width, Dashed = edge.Dashed, TextColor = edge.Color, FontSize = 12 };
        WriteStyle(node, style); VisioXml.SetCell(node, "BeginArrow", Arrow(edge.StartArrow), ""); VisioXml.SetCell(node, "EndArrow", Arrow(edge.EndArrow), "");
        var figure = new GeometryFigure { Filled = false };
        for (var i = 0; i < points.Length; i++) figure.Segments.Add(new() { Verb = i == 0 ? GeometryVerb.Move : GeometryVerb.Line, End = new((points[i].X - bounds.Left) / width, 1 - (points[i].Y - bounds.Top) / height) });
        WriteGeometry(node, [figure], width, height, 0, part, id); WriteText(node, new Shape { Text = edge.Text, Style = style });
        void Connect(bool source)
        {
            var shapeId = source ? edge.SourceId : edge.TargetId; if (shapeId is null || !ids.TryGetValue(shapeId, out var targetId)) return;
            var pointId = source ? edge.SourcePointId : edge.TargetPointId; var shape = page.Find(shapeId);
            var pointIndex = shape?.ConnectionPoints.FindIndex(p => p.Id == pointId) ?? -1;
            connects.Add(new XElement(_v + "Connect", new XAttribute("FromSheet", id), new XAttribute("FromCell", source ? "BeginX" : "EndX"), new XAttribute("FromPart", source ? 9 : 12),
                new XAttribute("ToSheet", targetId), new XAttribute("ToCell", pointIndex >= 0 ? "Connections.X" + (pointIndex + 1) : "PinX"), new XAttribute("ToPart", pointIndex >= 0 ? 100 + pointIndex : 3)));
        }
        Connect(true); Connect(false);
        if (options.IncludeDrawingSpaceMetadata)
        {
            node.Element(_d + "Connector")?.Remove();
            node.Add(new XElement(_d + "Connector", new XAttribute("LineJumps", edge.LineJumps), new XAttribute("JumpSize", VisioXml.Format(edge.JumpSize)), new XAttribute("LabelPosition", VisioXml.Format(edge.LabelPosition)),
                new XAttribute("LabelOffsetX", VisioXml.Format(edge.LabelOffset.X)), new XAttribute("LabelOffsetY", VisioXml.Format(edge.LabelOffset.Y))));
        }
        return node;
    }
    private static PointD Endpoint(DiagramPage page, Connector edge, bool source)
    {
        var shape = page.Find(source ? edge.SourceId : edge.TargetId);
        if (shape is null) return source ? edge.Start : edge.End;
        var side = source ? edge.SourcePort : edge.TargetPort;
        if (side == PortSide.Auto)
        {
            var other = page.Find(source ? edge.TargetId : edge.SourceId)?.Bounds.Center ?? (source ? edge.End : edge.Start); var delta = other - shape.Bounds.Center;
            side = Math.Abs(delta.X) > Math.Abs(delta.Y) ? delta.X >= 0 ? PortSide.East : PortSide.West : delta.Y >= 0 ? PortSide.South : PortSide.North;
        }
        return shape.Port(source ? edge.SourcePointId : edge.TargetPointId, side);
    }
    private static int Arrow(ArrowHead value) => value switch { ArrowHead.None => 0, ArrowHead.Open => 1, ArrowHead.Diamond => 4, _ => 5 };

    private void WriteImage(XElement node, Shape shape, string part, uint id)
    {
        VisioXml.Child(node, "ForeignData")?.Remove();
        if (shape.ImageData is not { Length: > 0 } bytes) return;
        var extension = shape.ImageContentType switch { "image/png" => "png", "image/jpeg" => "jpg", "image/gif" => "gif", "image/webp" => "webp", "image/bmp" => "bmp", _ => null };
        if (extension is null) { Warn("ImageType", part, "The image content type is not supported for Visio export.", id); return; }
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes)); var imagePart = "visio/media/image-" + hash + "." + extension;
        _package.Set(imagePart, bytes); _contentTypes[imagePart] = shape.ImageContentType!;
        var reference = "rIdImage" + id;
        // Images have multiple relationships of the same type, unlike the single document/page catalogs.
        var relationsPart = OpcPackage.RelationshipPart(part); var relations = _package.Xml(relationsPart)?.Root ?? new XElement(VisioNamespaces.Relationships + "Relationships");
        foreach (var old in relations.Elements().Where(r => VisioXml.Attribute(r, "Id") == reference).ToArray()) old.Remove();
        relations.Add(Relationship(reference, VisioNamespaces.OfficeRelationship + "image", "/" + imagePart)); _package.SetXml(relationsPart, new(relations));
        node.Add(new XElement(_v + "ForeignData", new XAttribute("ForeignType", "Bitmap"), new XAttribute("CompressionType", extension.ToUpperInvariant()), new XElement(_v + "Rel", new XAttribute(_r + "id", reference))));
    }
}
