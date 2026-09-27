using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DrawingSpace.Core;
using DrawingSpace.Documents;

namespace DrawingSpace.Visio;

/// <summary>Reads actual Visio package parts into editable native entities. External resources and macros are never executed.</summary>
public static class VisioReader
{
    public static VisioReadResult Read(ReadOnlyMemory<byte> bytes, string fileName = "Drawing.vsdx", VisioReadOptions? options = null)
    {
        options ??= new(); options.CancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length >= 8 && bytes.Span[..8].SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }))
            throw new NotSupportedException("This is a legacy binary VSD drawing. Use the configured legacy conversion backend; it is not an OPC/VSDX package.");
        if (bytes.Length > 0 && bytes.Span[0] != (byte)'P') return VdxCodec.Read(bytes, fileName, options);
        var package = OpcPackage.Read(bytes, options);
        return new VisioReadContext(package, options).Read(bytes, fileName);
    }
}

internal sealed partial class VisioReadContext(OpcPackage package, VisioReadOptions options)
{
    private readonly List<VisioDiagnostic> _diagnostics = [];
    private readonly Dictionary<uint, XElement> _masterRoots = [];
    private readonly Dictionary<uint, DiagramMaster> _masters = [];
    private readonly Dictionary<uint, XElement> _styles = [];
    private readonly Dictionary<uint, string> _fonts = [];
    private readonly Dictionary<int, string> _colors = [];
    private readonly Dictionary<string, IReadOnlyList<OpcRelationship>> _relationships = new(StringComparer.OrdinalIgnoreCase);
    private int _shapeCount;
    private uint _syntheticId = 1000000;

    internal VisioReadResult Read(ReadOnlyMemory<byte> bytes, string fileName)
    {
        var documentPart = Relations("").FirstOrDefault(r => !r.External && r.Type == VisioNamespaces.VisioRelationship + "document")?.Target ?? "visio/document.xml";
        var xml = package.Xml(documentPart) ?? throw new InvalidDataException("The Visio document part is missing.");
        if (xml.Root?.Name.LocalName != "VisioDocument") throw new InvalidDataException("The package is not a Visio document.");
        var document = new DiagramDocument { Title = Path.GetFileNameWithoutExtension(fileName), Pages = [], Cells = VisioXml.ReadCells(VisioXml.Child(xml.Root, "DocumentSheet")) };
        var nativeId = (string?)xml.Root.Attribute(VisioNamespaces.DrawingSpace + "Id"); if (!string.IsNullOrWhiteSpace(nativeId)) document.Id = nativeId;
        if (package.Xml("docProps/core.xml")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "title") is { } title && title.Value.Length > 0) document.Title = title.Value;
        foreach (var font in VisioXml.Children(VisioXml.Child(xml.Root, "FaceNames"), "FaceName")) _fonts[VisioXml.Id(font)] = VisioXml.Attribute(font, "Name", "Arial");
        foreach (var color in VisioXml.Children(VisioXml.Child(xml.Root, "Colors"), "ColorEntry"))
            if (int.TryParse(VisioXml.Attribute(color, "IX"), out var index)) _colors[index] = NormalizeColor(VisioXml.Attribute(color, "RGB"), "#000000");
        foreach (var style in VisioXml.Children(VisioXml.Child(xml.Root, "StyleSheets"), "StyleSheet"))
            if (!_styles.TryAdd(VisioXml.Id(style), style)) throw new InvalidDataException("Duplicate Visio style identifier.");
        ReadMasters(documentPart, document);
        var pagesPart = Relations(documentPart).FirstOrDefault(r => !r.External && r.Type == VisioNamespaces.VisioRelationship + "pages")?.Target ?? "visio/pages/pages.xml";
        var catalog = package.Xml(pagesPart);
        var pageIds = new Dictionary<uint, string>(); var backgrounds = new Dictionary<string, uint>();
        foreach (var entry in VisioXml.Children(catalog?.Root, "Page"))
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (document.Pages.Count >= 256) throw new InvalidDataException("The drawing contains too many pages.");
            var id = VisioXml.Id(entry); var name = VisioXml.Attribute(entry, "Name", VisioXml.Attribute(entry, "NameU", "Page-" + id));
            var part = RelatedPart(pagesPart, entry) ?? throw new InvalidDataException("Page relationship is missing.");
            var contents = package.Xml(part) ?? throw new InvalidDataException("Page part is missing: " + part);
            var sheet = VisioXml.Child(entry, "PageSheet");
            var page = new DiagramPage
            {
                Id = (string?)entry.Attribute(VisioNamespaces.DrawingSpace + "Id") ?? "visio-page-" + id,
                VisioId = id, VisioPart = part, Name = name, Width = Dimension(VisioXml.Number(sheet, "PageWidth", 11.6875) * 96, "page width"),
                Height = Dimension(VisioXml.Number(sheet, "PageHeight", 8.270833333333334) * 96, "page height"),
                IsBackground = VisioXml.Attribute(entry, "Background") is "1" or "true", Cells = VisioXml.ReadCells(sheet), Shapes = [], Connectors = [], Groups = []
            };
            if (!pageIds.TryAdd(id, page.Id)) throw new InvalidDataException("Duplicate page identifier.");
            if (uint.TryParse(VisioXml.Attribute(entry, "BackPage"), out var back)) backgrounds[page.Id] = back;
            ReadLayers(sheet, page);
            var allIds = contents.Descendants().Where(e => e.Name.LocalName == "Shape").Select(e => VisioXml.Id(e)).ToArray();
            if (allIds.Length > 0) _syntheticId = Math.Max(_syntheticId, allIds.Max());
            var map = new Dictionary<uint, string>();
            foreach (var shape in VisioXml.Children(VisioXml.Child(contents.Root, "Shapes"), "Shape"))
                ReadShape(page, shape, MatrixD.Identity, null, null, null, part, page.Id, map, false, 0);
            ReadConnects(VisioXml.Child(contents.Root, "Connects"), page, map, part);
            ReadNativePageMetadata(contents.Root, page);
            document.Pages.Add(page);
        }
        foreach (var page in document.Pages)
            if (backgrounds.TryGetValue(page.Id, out var id) && pageIds.TryGetValue(id, out var background)) page.BackgroundPageId = background;
        if (document.Pages.Count == 0) BuildStencilPreview(document);
        DocumentCodec.Validate(document);
        if (options.PreserveOriginalPackage)
        {
            if (bytes.Length > DocumentCodec.MaximumPackageBytes) throw new InvalidDataException("The preserved archive exceeds the native document budget.");
            document.VisioOrigin = new()
            {
                Format = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant(), Package = bytes.ToArray(), ModelFingerprint = VisioFingerprint.Compute(document),
                Diagnostics = _diagnostics.Select(d => d.Code + ": " + d.Message).Take(4096).ToList()
            };
        }
        return new(document, _diagnostics.ToArray());
    }

    private void ReadMasters(string documentPart, DiagramDocument document)
    {
        var catalogPart = Relations(documentPart).FirstOrDefault(r => !r.External && r.Type == VisioNamespaces.VisioRelationship + "masters")?.Target ?? "visio/masters/masters.xml";
        var catalog = package.Xml(catalogPart); if (catalog?.Root is null) return;
        var entries = VisioXml.Children(catalog.Root, "Master").ToArray();
        if (entries.Length > 4096) throw new InvalidDataException("The stencil contains too many masters.");
        var sources = new Dictionary<uint, (XElement Entry, string Part, XDocument Xml)>();
        foreach (var entry in entries)
        {
            var id = VisioXml.Id(entry); var part = RelatedPart(catalogPart, entry);
            if (part is null || package.Xml(part) is not { } contents) { Warn("MissingMaster", catalogPart, "A master part is missing.", id); continue; }
            if (!sources.TryAdd(id, (entry, part, contents))) throw new InvalidDataException("Duplicate master identifier.");
            var root = VisioXml.Children(VisioXml.Child(contents.Root, "Shapes"), "Shape").FirstOrDefault();
            if (root is not null) _masterRoots[id] = root;
        }
        foreach (var (id, source) in sources)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            var root = _masterRoots.GetValueOrDefault(id);
            var width = Math.Max(1, VisioXml.Number(root, "Width", 2) * 96); var height = Math.Max(1, VisioXml.Number(root, "Height", 1) * 96);
            var master = new DiagramMaster
            {
                Id = (string?)source.Entry.Attribute(VisioNamespaces.DrawingSpace + "Id") ?? "visio-master-" + id,
                Name = VisioXml.Attribute(source.Entry, "Name", VisioXml.Attribute(source.Entry, "NameU", "Master " + id)), VisioId = id, VisioPart = source.Part
            };
            var page = new DiagramPage { Id = master.Id, Width = width, Height = height, Shapes = [], Connectors = [], Groups = [] };
            var map = new Dictionary<uint, string>();
            foreach (var shape in VisioXml.Children(VisioXml.Child(source.Xml.Root, "Shapes"), "Shape"))
                ReadShape(page, shape, MatrixD.Identity, null, null, null, source.Part, master.Id, map, true, 0);
            ReadConnects(VisioXml.Child(source.Xml.Root, "Connects"), page, map, source.Part);
            master.Shape = page.Shapes.FirstOrDefault() ?? new Shape { Id = master.Id + "-anchor", Width = width, Height = height, Text = "", IsGroupAnchor = true, Style = new() { Opacity = 0 } };
            master.Children = page.Shapes.Skip(1).ToList(); master.Connectors = page.Connectors; master.Groups = page.Groups;
            document.Masters.Add(master); _masters[id] = master;
        }
    }

    private void BuildStencilPreview(DiagramDocument document)
    {
        var page = new DiagramPage { Name = document.Masters.Count > 0 ? "Stencil preview" : "Page-1", Width = 1122, Height = 794 };
        var index = 0;
        foreach (var master in document.Masters)
        {
            var shape = master.Shape.Clone(true); shape.MasterId = master.Id; shape.MasterShapeId = master.Shape.Id;
            shape.X = 56 + index % 4 * 250; shape.Y = 56 + index / 4 * 150; shape.Width = Math.Min(190, shape.Width); shape.Height = Math.Min(100, shape.Height);
            shape.GroupId = null; shape.ContainerId = null; shape.IsGroupAnchor = false; shape.Text = master.Name; shape.Style.Opacity = 1;
            shape.Cells.Clear(); shape.LocalOverrides.AddRange(["Width", "Height", "Text"]);
            page.Shapes.Add(shape); index++;
        }
        page.Height = Math.Max(page.Height, 100 + (index + 3) / 4 * 150); document.Pages.Add(page);
    }

    private IReadOnlyList<OpcRelationship> Relations(string part)
    {
        if (_relationships.TryGetValue(part, out var cached)) return cached;
        var values = package.Relationships(part); _relationships[part] = values;
        foreach (var external in values.Where(r => r.External)) Warn("ExternalRelationship", part, "An external relationship was preserved but not fetched: " + external.Type);
        return values;
    }
    private string? RelatedPart(string source, XElement owner)
    {
        var reference = VisioXml.Child(owner, "Rel");
        var id = reference?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
        return id is null ? null : Relations(source).FirstOrDefault(r => !r.External && r.Id == id)?.Target;
    }
    private void Warn(string code, string part, string message, uint? shapeId = null)
    {
        if (_diagnostics.Count < 4096) _diagnostics.Add(new(VisioDiagnosticSeverity.Warning, code, part, message, shapeId));
    }
    private static double Dimension(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 24 or > 100000) throw new InvalidDataException("Unsupported " + name + ".");
        return value;
    }
    private static string NormalizeColor(string value, string fallback)
    {
        if (value.Length is 7 or 9 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) return value.ToUpperInvariant();
        return fallback;
    }
    private void ReadNativePageMetadata(XElement? root, DiagramPage page)
    {
        if (root?.Element(VisioNamespaces.DrawingSpace + "Page") is not { } metadata) return;
        page.Background = NormalizeColor((string?)metadata.Attribute("Background") ?? "", page.Background);
    }
}
