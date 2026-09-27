using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;
using DrawingSpace.Routing;

namespace DrawingSpace.Visio;

public static class VisioWriter
{
    public static VisioWriteResult Write(DiagramDocument document, VisioWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document); options ??= new(); DocumentCodec.Validate(document);
        var extension = options.Kind switch { VisioPackageKind.Stencil => "vssx", VisioPackageKind.Template => "vstx", _ => "vsdx" };
        var origin = document.VisioOrigin;
        if (options.PreserveOriginalWhenUnmodified && origin is not null && origin.Format == extension && origin.ModelFingerprint == VisioFingerprint.Compute(document))
            return new(origin.Package.ToArray(), [], true);
        return new VisioWriteContext(document, options).Write();
    }
}

internal sealed partial class VisioWriteContext(DiagramDocument document, VisioWriteOptions options)
{
    private OpcPackage _package = new();
    private readonly List<VisioDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, uint> _masterIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _pageIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> _fontIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly OrthogonalRouter _router = new();
    private readonly XNamespace _v = VisioNamespaces.Main;
    private readonly XNamespace _d = VisioNamespaces.DrawingSpace;
    private readonly XNamespace _r = VisioNamespaces.RelationshipReference;

    internal VisioWriteResult Write()
    {
        if (options.PreserveUnknownParts && document.VisioOrigin is { Package.Length: > 3 } origin && origin.Package[0] == (byte)'P' && origin.Package[1] == (byte)'K')
            _package = OpcPackage.Read(origin.Package);
        RemoveInvalidSignaturesAndMacros();
        var root = _package.Xml("visio/document.xml")?.Root is { } existing ? new XElement(existing) : new XElement(_v + "VisioDocument");
        root.Name = _v + "VisioDocument"; root.SetAttributeValue(XNamespace.Xmlns + "r", _r.NamespaceName);
        if (options.IncludeDrawingSpaceMetadata) { root.SetAttributeValue(XNamespace.Xmlns + "ds", _d.NamespaceName); root.SetAttributeValue(_d + "Id", document.Id); }
        var fonts = new XElement(_v + "FaceNames");
        foreach (var font in AllShapes().Select(s => s.Style.FontFamily).Concat(AllShapes().SelectMany(s => s.TextSpans).Select(s => s.FontFamily).OfType<string>()).Append("Arial").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = (uint)_fontIds.Count; _fontIds.Add(font, id);
            fonts.Add(new XElement(_v + "FaceName", new XAttribute("ID", id), new XAttribute("Name", font), new XAttribute("UnicodeRanges", "-1 -1 -1 -1"), new XAttribute("CharSets", "0")));
        }
        ReplaceChild(root, "FaceNames", fonts);
        var sheet = VisioXml.Child(root, "DocumentSheet") ?? new XElement(_v + "DocumentSheet");
        WriteCells(sheet, document.Cells); ReplaceChild(root, "DocumentSheet", sheet);
        AssignIds(document.Masters.Select(m => (m.Id, m.VisioId)), _masterIds);
        AssignIds(document.Pages.Select(p => (p.Id, p.VisioId)), _pageIds);
        WriteMasters(); WritePages();
        SetXml("visio/document.xml", root, options.Kind switch
        {
            VisioPackageKind.Stencil => "application/vnd.ms-visio.stencil.main+xml", VisioPackageKind.Template => "application/vnd.ms-visio.template.main+xml", _ => "application/vnd.ms-visio.drawing.main+xml"
        });
        SetRelationship("", "rIdDrawingSpaceDocument", VisioNamespaces.VisioRelationship + "document", "visio/document.xml");
        SetRelationship("visio/document.xml", "rIdDrawingSpacePages", VisioNamespaces.VisioRelationship + "pages", "pages/pages.xml");
        SetRelationship("visio/document.xml", "rIdDrawingSpaceMasters", VisioNamespaces.VisioRelationship + "masters", "masters/masters.xml");
        WriteCoreProperties(); WriteContentTypes();
        var bytes = _package.Write(options.CancellationToken);
        return new(bytes, _diagnostics.ToArray(), false);
    }
    private IEnumerable<Shape> AllShapes() => document.Pages.SelectMany(p => p.Shapes).Concat(document.Masters.SelectMany(m => m.Children.Prepend(m.Shape)));

    private void WritePages()
    {
        var original = _package.Xml("visio/pages/pages.xml");
        var catalog = new XElement(_v + "Pages", new XAttribute(XNamespace.Xmlns + "r", _r.NamespaceName));
        var relations = new XElement(VisioNamespaces.Relationships + "Relationships");
        var index = 0;
        foreach (var page in document.Pages)
        {
            options.CancellationToken.ThrowIfCancellationRequested(); index++;
            var part = page.VisioPart is { } saved && saved.StartsWith("visio/pages/", StringComparison.Ordinal) ? saved : $"visio/pages/page{index}.xml";
            if (_contentTypes.ContainsKey(part)) part = $"visio/pages/page-{Guid.NewGuid():N}.xml";
            var old = VisioXml.Children(original?.Root, "Page").FirstOrDefault(e => VisioXml.Id(e) == _pageIds[page.Id]);
            var entry = old is null ? new XElement(_v + "Page") : new XElement(old);
            entry.SetAttributeValue("ID", _pageIds[page.Id]); entry.SetAttributeValue("Name", page.Name); entry.SetAttributeValue("NameU", page.Name);
            entry.SetAttributeValue("Background", page.IsBackground ? "1" : "0");
            entry.SetAttributeValue("BackPage", page.BackgroundPageId is { } background && _pageIds.TryGetValue(background, out var back) ? back.ToString(CultureInfo.InvariantCulture) : null);
            if (options.IncludeDrawingSpaceMetadata) entry.SetAttributeValue(_d + "Id", page.Id);
            var sheet = VisioXml.Child(entry, "PageSheet") ?? new XElement(_v + "PageSheet");
            WriteCells(sheet, page.Cells); VisioXml.SetCell(sheet, "PageWidth", page.Width / 96, "", "IN"); VisioXml.SetCell(sheet, "PageHeight", page.Height / 96, "", "IN");
            WriteLayers(sheet, page); ReplaceChild(entry, "PageSheet", sheet);
            ReplaceChild(entry, "Rel", new XElement(_v + "Rel", new XAttribute(_r + "id", "rId" + index)));
            catalog.Add(entry); relations.Add(Relationship("rId" + index, VisioNamespaces.VisioRelationship + "page", "/" + part));
            WritePageContents(page, part, false);
        }
        SetXml("visio/pages/pages.xml", catalog, "application/vnd.ms-visio.pages+xml");
        _package.SetXml("visio/pages/_rels/pages.xml.rels", new(relations));
    }

    private void WriteMasters()
    {
        var original = _package.Xml("visio/masters/masters.xml");
        var catalog = new XElement(_v + "Masters", new XAttribute(XNamespace.Xmlns + "r", _r.NamespaceName));
        var relations = new XElement(VisioNamespaces.Relationships + "Relationships"); var index = 0;
        foreach (var master in document.Masters)
        {
            options.CancellationToken.ThrowIfCancellationRequested(); index++;
            var part = master.VisioPart is { } saved && saved.StartsWith("visio/masters/", StringComparison.Ordinal) ? saved : $"visio/masters/master{index}.xml";
            if (_contentTypes.ContainsKey(part)) part = $"visio/masters/master-{Guid.NewGuid():N}.xml";
            var old = VisioXml.Children(original?.Root, "Master").FirstOrDefault(e => VisioXml.Id(e) == _masterIds[master.Id]);
            var entry = old is null ? new XElement(_v + "Master") : new XElement(old);
            entry.SetAttributeValue("ID", _masterIds[master.Id]); entry.SetAttributeValue("Name", master.Name); entry.SetAttributeValue("NameU", master.Name);
            if (options.IncludeDrawingSpaceMetadata) entry.SetAttributeValue(_d + "Id", master.Id);
            ReplaceChild(entry, "Rel", new XElement(_v + "Rel", new XAttribute(_r + "id", "rId" + index)));
            catalog.Add(entry); relations.Add(Relationship("rId" + index, VisioNamespaces.VisioRelationship + "master", "/" + part));
            var page = new DiagramPage
            {
                Id = master.Id, Name = master.Name, Width = Math.Max(24, master.Shape.Width), Height = Math.Max(24, master.Shape.Height),
                Shapes = master.Children.Prepend(master.Shape).ToList(), Connectors = master.Connectors, Groups = master.Groups
            };
            WritePageContents(page, part, true);
        }
        SetXml("visio/masters/masters.xml", catalog, "application/vnd.ms-visio.masters+xml");
        _package.SetXml("visio/masters/_rels/masters.xml.rels", new(relations));
    }

    private void WriteCoreProperties()
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var root = _package.Xml("docProps/core.xml")?.Root ?? new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "dc", dc.NamespaceName));
        root.Element(dc + "title")?.Remove(); root.Add(new XElement(dc + "title", document.Title));
        SetXml("docProps/core.xml", root, "application/vnd.openxmlformats-package.core-properties+xml");
        SetRelationship("", "rIdDrawingSpaceCore", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
    }

    private void WriteContentTypes()
    {
        var ns = VisioNamespaces.ContentTypes;
        var root = _package.Xml("[Content_Types].xml")?.Root ?? new XElement(ns + "Types");
        foreach (var item in root.Elements(ns + "Override").Where(e => !_package.Contains(((string?)e.Attribute("PartName") ?? "").TrimStart('/'))).ToArray()) item.Remove();
        foreach (var (part, type) in _contentTypes)
        {
            foreach (var old in root.Elements(ns + "Override").Where(e => ((string?)e.Attribute("PartName"))?.Equals("/" + part, StringComparison.OrdinalIgnoreCase) == true).ToArray()) old.Remove();
            root.Add(new XElement(ns + "Override", new XAttribute("PartName", "/" + part), new XAttribute("ContentType", type)));
        }
        foreach (var pair in new[] { ("rels", "application/vnd.openxmlformats-package.relationships+xml"), ("xml", "application/xml") })
            if (!root.Elements(ns + "Default").Any(e => (string?)e.Attribute("Extension") == pair.Item1)) root.Add(new XElement(ns + "Default", new XAttribute("Extension", pair.Item1), new XAttribute("ContentType", pair.Item2)));
        _package.SetXml("[Content_Types].xml", new(root));
    }

    private void RemoveInvalidSignaturesAndMacros()
    {
        var removed = _package.Parts.Keys.Where(p => p.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) || p.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var part in removed) _package.Remove(part);
        if (removed.Length == 0) return;
        Warn("ActiveContentRemoved", "", "Editing invalidates signatures. Signature parts and VBA projects were removed from the macro-free output.");
        foreach (var part in _package.Parts.Keys.Where(p => p.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var xml = _package.Xml(part); if (xml?.Root is null) continue;
            foreach (var relation in xml.Root.Elements().Where(e => VisioXml.Attribute(e, "Type").Contains("digital-signature", StringComparison.OrdinalIgnoreCase) || VisioXml.Attribute(e, "Type").Contains("vbaProject", StringComparison.OrdinalIgnoreCase)).ToArray()) relation.Remove();
            _package.SetXml(part, xml);
        }
    }

    private static void AssignIds(IEnumerable<(string Id, uint? Visio)> entities, Dictionary<string, uint> output)
    {
        var items = entities.ToArray(); var used = new HashSet<uint>(); uint next = 1;
        foreach (var item in items) if (item.Visio is { } number && used.Add(number)) output[item.Id] = number;
        foreach (var item in items) if (!output.ContainsKey(item.Id)) { while (used.Contains(next)) next = checked(next + 1); output[item.Id] = next; used.Add(next); }
    }
    private void SetXml(string part, XElement root, string contentType) { _package.SetXml(part, new(root)); _contentTypes[part] = contentType; }
    private void SetRelationship(string source, string id, string type, string target)
    {
        var part = OpcPackage.RelationshipPart(source); var root = _package.Xml(part)?.Root ?? new XElement(VisioNamespaces.Relationships + "Relationships");
        foreach (var previous in root.Elements().Where(e => VisioXml.Attribute(e, "Type") == type || VisioXml.Attribute(e, "Id") == id).ToArray()) previous.Remove();
        root.Add(Relationship(id, type, target)); _package.SetXml(part, new(root));
    }
    private static XElement Relationship(string id, string type, string target) => new(VisioNamespaces.Relationships + "Relationship", new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));
    private static void ReplaceChild(XElement parent, string name, XElement child)
    {
        foreach (var old in parent.Elements().Where(e => e.Name.LocalName == name).ToArray()) old.Remove(); parent.Add(child);
    }
    private void Warn(string code, string part, string message, uint? shapeId = null)
    { if (_diagnostics.Count < 4096) _diagnostics.Add(new(VisioDiagnosticSeverity.Warning, code, part, message, shapeId)); }
}
