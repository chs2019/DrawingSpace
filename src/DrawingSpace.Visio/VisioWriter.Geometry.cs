using System.Globalization;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

internal sealed partial class VisioWriteContext
{
    private void WriteGeometry(XElement owner, IReadOnlyList<GeometryFigure> figures, double width, double height, double shear, string part, uint shapeId)
    {
        foreach (var old in VisioXml.Children(owner, "Section").Where(s => VisioXml.Attribute(s, "N") == "Geometry").ToArray()) old.Remove();
        if (figures.Count == 0)
        {
            var empty = new XElement(_v + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", 0));
            VisioXml.SetCell(empty, "NoFill", 1); VisioXml.SetCell(empty, "NoLine", 1); owner.Add(empty); return;
        }
        for (var index = 0; index < figures.Count; index++)
        {
            var figure = figures[index]; var section = new XElement(_v + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", index));
            VisioXml.SetCell(section, "NoFill", figure.Filled ? 0 : 1); VisioXml.SetCell(section, "NoLine", figure.Stroked ? 0 : 1); VisioXml.SetCell(section, "NoShow", 0);
            var first = new PointD(); var current = new PointD(); var rowIndex = 0;
            PointD Map(PointD p) => new(p.X + shear * height / width * (.5 - p.Y), 1 - p.Y);
            XElement Row(string type) { var row = new XElement(_v + "Row", new XAttribute("IX", ++rowIndex), new XAttribute("T", type)); section.Add(row); return row; }
            void Point(XElement row, PointD p, string x = "X", string y = "Y") { var value = Map(p); VisioXml.SetCell(row, x, value.X); VisioXml.SetCell(row, y, value.Y); }
            void Cubic(GeometrySegment segment)
            {
                var row = Row("RelCubBezTo"); Point(row, segment.End); Point(row, segment.Control1, "A", "B"); Point(row, segment.Control2, "C", "D");
            }
            foreach (var segment in figure.Segments)
            {
                switch (segment.Verb)
                {
                    case GeometryVerb.Move: first = segment.End; Point(Row("RelMoveTo"), segment.End); break;
                    case GeometryVerb.Line: Point(Row("RelLineTo"), segment.End); break;
                    case GeometryVerb.Quadratic:
                        var quadratic = Row("RelQuadBezTo"); Point(quadratic, segment.End); Point(quadratic, segment.Control1, "A", "B"); break;
                    case GeometryVerb.Cubic: Cubic(segment); break;
                    case GeometryVerb.Arc:
                        if (!ArcGeometry.TryFromEndpoint(current, segment, width, height, out var arc)) { Point(Row("RelLineTo"), segment.End); break; }
                        if (Math.Abs(shear) > 1e-10)
                        {
                            foreach (var cubic in ArcGeometry.ToCubics(arc, width, height)) Cubic(cubic);
                            Warn("ShearedArc", part, "A sheared ellipse was exported as cubic Bézier segments.", shapeId);
                        }
                        else
                        {
                            var row = Row("EllipticalArcTo"); var middle = arc.At(.5);
                            VisioXml.SetCell(row, "X", segment.End.X * width); VisioXml.SetCell(row, "Y", (1 - segment.End.Y) * height);
                            VisioXml.SetCell(row, "A", middle.X); VisioXml.SetCell(row, "B", height - middle.Y);
                            VisioXml.SetCell(row, "C", -arc.Rotation); VisioXml.SetCell(row, "D", arc.RadiusX / arc.RadiusY);
                        }
                        break;
                    case GeometryVerb.Close: Point(Row("RelLineTo"), first); current = first; continue;
                }
                current = segment.End;
            }
            owner.Add(section);
        }
    }

    private static XElement EnsureSection(XElement owner, string name)
    {
        var section = VisioXml.Children(owner, "Section").FirstOrDefault(s => VisioXml.Attribute(s, "N") == name);
        if (section is not null) return section;
        section = new XElement(VisioNamespaces.Main + "Section", new XAttribute("N", name)); owner.Add(section); return section;
    }
    private static XElement EnsureRow(XElement section, string? name, uint index)
    {
        var row = VisioXml.Children(section, "Row").FirstOrDefault(r => name is null ? VisioXml.Id(r, "IX") == index : VisioXml.Attribute(r, "N") == name);
        if (row is not null) return row;
        row = new XElement(VisioNamespaces.Main + "Row", new XAttribute("IX", index)); if (name is not null) row.SetAttributeValue("N", name); section.Add(row); return row;
    }

    private static void WriteCells(XElement owner, IReadOnlyDictionary<string, ShapeCell> cells)
    {
        foreach (var (key, value) in cells)
        {
            XElement target = owner; var name = key;
            if (key.StartsWith("User.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Prop.", StringComparison.OrdinalIgnoreCase))
            {
                var sectionName = key.StartsWith("User.", StringComparison.OrdinalIgnoreCase) ? "User" : "Property";
                var rest = key[5..]; var split = rest.LastIndexOf('.'); var rowName = split < 0 ? rest : rest[..split]; name = split < 0 ? "Value" : rest[(split + 1)..];
                var section = EnsureSection(owner, sectionName); target = EnsureRow(section, rowName, (uint)section.Elements().Count());
            }
            else if (key.StartsWith("Char.", StringComparison.OrdinalIgnoreCase) || key.StartsWith("Para.", StringComparison.OrdinalIgnoreCase))
            {
                var section = EnsureSection(owner, key.StartsWith("Char.", StringComparison.OrdinalIgnoreCase) ? "Character" : "Paragraph");
                name = key[5..]; uint index = 0; var bracket = name.IndexOf('[');
                if (bracket >= 0 && name.EndsWith(']') && uint.TryParse(name[(bracket + 1)..^1], out var parsed)) { index = parsed > 0 ? parsed - 1 : 0; name = name[..bracket]; }
                target = EnsureRow(section, null, index);
            }
            else if (key.Contains('.')) continue;
            VisioXml.SetCell(target, name, value.Value, value.Formula, value.Unit);
        }
    }

    private void WriteLayers(XElement sheet, DiagramPage page)
    {
        foreach (var old in VisioXml.Children(sheet, "Section").Where(s => VisioXml.Attribute(s, "N") == "Layer").ToArray()) old.Remove();
        var section = EnsureSection(sheet, "Layer");
        for (var index = 0; index < page.Layers.Count; index++)
        {
            var layer = page.Layers[index]; var row = EnsureRow(section, null, (uint)index);
            VisioXml.SetCell(row, "Name", layer.Name); VisioXml.SetCell(row, "Visible", layer.Visible ? 1 : 0); VisioXml.SetCell(row, "Lock", layer.Locked ? 1 : 0); VisioXml.SetCell(row, "Print", layer.Printable ? 1 : 0);
        }
    }

    private void WriteConnectionPoints(XElement owner, Shape shape, double width, double height)
    {
        foreach (var old in VisioXml.Children(owner, "Section").Where(s => VisioXml.Attribute(s, "N") == "Connection").ToArray()) old.Remove();
        if (shape.ConnectionPoints.Count == 0) return;
        var section = EnsureSection(owner, "Connection");
        for (var index = 0; index < shape.ConnectionPoints.Count; index++)
        {
            var point = shape.ConnectionPoints[index]; var row = EnsureRow(section, null, (uint)index);
            VisioXml.SetCell(row, "X", point.Position.X * width, "Width*" + VisioXml.Format(point.Position.X), "IN");
            VisioXml.SetCell(row, "Y", (1 - point.Position.Y) * height, "Height*" + VisioXml.Format(1 - point.Position.Y), "IN");
            VisioXml.SetCell(row, "DirX", point.Direction.X); VisioXml.SetCell(row, "DirY", -point.Direction.Y); VisioXml.SetCell(row, "Type", point.Incoming && point.Outgoing ? 2 : point.Outgoing ? 1 : 0);
        }
    }

    private void WriteProperties(XElement owner, Shape shape)
    {
        if (shape.Data.Count > 0)
        {
            var properties = EnsureSection(owner, "Property"); uint index = 0;
            foreach (var (name, value) in shape.Data)
            {
                var row = EnsureRow(properties, name, index++); VisioXml.SetCell(row, "Label", name); VisioXml.SetCell(row, "Value", value);
                VisioXml.SetCell(row, "Type", double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? 2 : 0);
            }
        }
        foreach (var old in VisioXml.Children(owner, "Section").Where(s => VisioXml.Attribute(s, "N") == "Hyperlink").ToArray()) old.Remove();
        if (shape.Hyperlinks.Count == 0) return;
        var links = EnsureSection(owner, "Hyperlink");
        for (var i = 0; i < shape.Hyperlinks.Count; i++)
        {
            var link = shape.Hyperlinks[i]; var row = EnsureRow(links, "Link" + i, (uint)i);
            VisioXml.SetCell(row, "Description", link.Description); VisioXml.SetCell(row, "Address", link.Address); VisioXml.SetCell(row, "SubAddress", link.SubAddress);
        }
    }

    private void WriteShapeMetadata(XElement node, Shape shape)
    {
        node.Element(_d + "Shape")?.Remove();
        var metadata = new XElement(_d + "Shape");
        if (shape.ContainerId is not null) metadata.SetAttributeValue("ContainerId", shape.ContainerId);
        if (shape.MasterInstanceId is not null) metadata.SetAttributeValue("MasterInstanceId", shape.MasterInstanceId);
        if (shape.Container is { } container) metadata.Add(new XElement(_d + "Container", new XAttribute("Layout", container.Layout), new XAttribute("Padding", VisioXml.Format(container.Padding)),
            new XAttribute("HeaderHeight", VisioXml.Format(container.HeaderHeight)), new XAttribute("AutoResize", container.AutoResize), new XAttribute("LockedMembership", container.LockedMembership)));
        if (shape.Comments.Count > 0 || shape.Threads.Count > 0)
            metadata.Add(new XElement(_d + "Comments", shape.Comments.Select(c => new XElement(_d + "Comment", c)), shape.Threads.Select(t => new XElement(_d + "Thread", ModelJson.Serialize(t)))));
        if (metadata.HasAttributes || metadata.HasElements) node.Add(metadata);
    }
}
