using System.Text;
using System.Xml.Linq;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

/// <summary>Legacy XML VDX is normalized into the same managed OPC reader; binary VSD is a separate format.</summary>
public static class VdxCodec
{
    private static readonly HashSet<string> Flatten = new(StringComparer.Ordinal)
    {
        "XForm", "XForm1D", "Line", "Fill", "TextBlock", "Protection", "Layout", "Misc", "PageProps", "PrintProps", "TextXForm", "Image", "Foreign", "Group"
    };
    private static readonly IReadOnlyDictionary<string, string> Sections = new Dictionary<string, string>
    { ["Geom"] = "Geometry", ["Char"] = "Character", ["Para"] = "Paragraph", ["Connection"] = "Connection", ["Prop"] = "Property", ["User"] = "User", ["Hyperlink"] = "Hyperlink", ["Layer"] = "Layer", ["Control"] = "Control", ["Action"] = "Action", ["Scratch"] = "Scratch", ["Tabs"] = "Tabs" };

    public static VisioReadResult Read(ReadOnlyMemory<byte> bytes, string fileName = "Drawing.vdx", VisioReadOptions? options = null)
    {
        options ??= new();
        if (bytes.Length == 0 || bytes.Length > options.MaximumInputBytes) throw new InvalidDataException("The VDX input exceeds its budget.");
        var document = OpcPackage.ParseXml(bytes.ToArray());
        if (document.Root?.Name.LocalName != "VisioDocument") throw new InvalidDataException("This XML is not a Visio VDX document.");
        var root = Normalize(document.Root); var package = new OpcPackage();
        var pages = new XElement(VisioNamespaces.Main + "Pages"); var masters = new XElement(VisioNamespaces.Main + "Masters");
        var pageRelations = new XElement(VisioNamespaces.Relationships + "Relationships"); var masterRelations = new XElement(VisioNamespaces.Relationships + "Relationships");
        var index = 0;
        foreach (var page in VisioXml.Children(VisioXml.Child(root, "Pages"), "Page").ToArray())
        {
            options.CancellationToken.ThrowIfCancellationRequested(); index++;
            var entry = new XElement(page); var content = new XElement(VisioNamespaces.Main + "PageContents");
            foreach (var item in entry.Elements().Where(e => e.Name.LocalName is "Shapes" or "Connects").ToArray()) { item.Remove(); content.Add(item); }
            var reference = "rId" + index; entry.Add(new XElement(VisioNamespaces.Main + "Rel", new XAttribute(VisioNamespaces.RelationshipReference + "id", reference))); pages.Add(entry);
            package.SetXml($"visio/pages/page{index}.xml", new(content));
            pageRelations.Add(new XElement(VisioNamespaces.Relationships + "Relationship", new XAttribute("Id", reference), new XAttribute("Type", VisioNamespaces.VisioRelationship + "page"), new XAttribute("Target", $"page{index}.xml")));
        }
        index = 0;
        foreach (var master in VisioXml.Children(VisioXml.Child(root, "Masters"), "Master").ToArray())
        {
            options.CancellationToken.ThrowIfCancellationRequested(); index++;
            var entry = new XElement(master); var content = new XElement(VisioNamespaces.Main + "MasterContents");
            foreach (var item in entry.Elements().Where(e => e.Name.LocalName is "Shapes" or "Connects").ToArray()) { item.Remove(); content.Add(item); }
            var reference = "rId" + index; entry.Add(new XElement(VisioNamespaces.Main + "Rel", new XAttribute(VisioNamespaces.RelationshipReference + "id", reference))); masters.Add(entry);
            package.SetXml($"visio/masters/master{index}.xml", new(content));
            masterRelations.Add(new XElement(VisioNamespaces.Relationships + "Relationship", new XAttribute("Id", reference), new XAttribute("Type", VisioNamespaces.VisioRelationship + "master"), new XAttribute("Target", $"master{index}.xml")));
        }
        VisioXml.Child(root, "Pages")?.Remove(); VisioXml.Child(root, "Masters")?.Remove();
        package.SetXml("visio/document.xml", new(root)); package.SetXml("visio/pages/pages.xml", new(pages)); package.SetXml("visio/masters/masters.xml", new(masters));
        package.SetXml("visio/pages/_rels/pages.xml.rels", new(pageRelations)); package.SetXml("visio/masters/_rels/masters.xml.rels", new(masterRelations));
        package.SetXml("[Content_Types].xml", new(new XElement(VisioNamespaces.ContentTypes + "Types")));
        return new VisioReadContext(package, options).Read(bytes, Path.ChangeExtension(fileName, ".vdx"));
    }

    private static XElement Normalize(XElement source)
    {
        var result = new XElement(VisioNamespaces.Main + source.Name.LocalName);
        foreach (var attribute in source.Attributes().Where(a => !a.IsNamespaceDeclaration)) result.SetAttributeValue(attribute.Name.LocalName, attribute.Value);
        var rows = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var child in source.Nodes())
        {
            if (child is XText text) { result.Add(new XText(text.Value)); continue; }
            if (child is not XElement element) continue;
            var name = element.Name.LocalName;
            if (source.Name.LocalName == "Text") { result.Add(Normalize(element)); continue; }
            if (Flatten.Contains(name))
            {
                foreach (var cell in element.Elements()) result.Add(Cell(cell));
            }
            else if (name == "Geom")
            {
                var section = new XElement(VisioNamespaces.Main + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", VisioXml.Attribute(element, "IX", "0")));
                foreach (var item in element.Elements())
                {
                    if (item.Name.LocalName is "NoFill" or "NoLine" or "NoShow" or "NoSnap") section.Add(Cell(item));
                    else
                    {
                        var row = new XElement(VisioNamespaces.Main + "Row", new XAttribute("T", item.Name.LocalName), new XAttribute("IX", VisioXml.Attribute(item, "IX", "1")));
                        foreach (var cell in item.Elements()) row.Add(Cell(cell)); section.Add(row);
                    }
                }
                result.Add(section);
            }
            else if (Sections.TryGetValue(name, out var sectionName))
            {
                if (!rows.TryGetValue(sectionName, out var section)) { section = new(VisioNamespaces.Main + "Section", new XAttribute("N", sectionName)); rows.Add(sectionName, section); result.Add(section); }
                var row = new XElement(VisioNamespaces.Main + "Row", new XAttribute("IX", VisioXml.Attribute(element, "IX", "0")));
                if (element.Attribute("NameU") is { } universal) row.SetAttributeValue("N", universal.Value);
                else if (element.Attribute("Name") is { } named) row.SetAttributeValue("N", named.Value);
                foreach (var cell in element.Elements()) row.Add(Cell(cell)); section.Add(row);
            }
            else result.Add(Normalize(element));
        }
        return result;
    }
    private static XElement Cell(XElement element)
    {
        if (element.Name.LocalName == "Cell") return Normalize(element);
        var result = new XElement(VisioNamespaces.Main + "Cell", new XAttribute("N", element.Name.LocalName), new XAttribute("V", element.Value));
        foreach (var name in new[] { "F", "U", "E" }) if (element.Attribute(name) is { } attribute) result.SetAttributeValue(name, attribute.Value);
        return result;
    }
}
