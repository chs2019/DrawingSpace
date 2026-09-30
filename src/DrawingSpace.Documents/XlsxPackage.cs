using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace DrawingSpace.Documents;

/// <summary>Read-only bounded OPC access. Targets are package paths, never network requests or local filesystem paths.</summary>
internal sealed class XlsxPackage : IDisposable
{
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private readonly MemoryStream _input;
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);
    private long _readBytes;

    public XlsxPackage(byte[] bytes)
    {
        _input = new(bytes, writable: false);
        try { _zip = new(_input, ZipArchiveMode.Read, leaveOpen: true); }
        catch { _input.Dispose(); throw; }
        try
        {
            if (_zip.Entries.Count > 2048) throw new InvalidDataException("Workbook exceeds 2048 package entries.");
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in _zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var path = Resolve("", entry.FullName);
                if (path != entry.FullName || !aliases.Add(path)) throw new InvalidDataException("Ambiguous or noncanonical workbook part path.");
                if (entry.Length > XlsxDataWorkbook.MaximumPartBytes || (expanded += entry.Length) > XlsxDataWorkbook.MaximumExpandedBytes)
                    throw new InvalidDataException("Workbook exceeds its expanded package budget.");
                _entries.Add(path, entry);
            }
        }
        catch { Dispose(); throw; }
    }

    public XmlReader OpenXml(string path, int limit = XlsxDataWorkbook.MaximumPartBytes)
    {
        if (!_entries.TryGetValue(path, out var entry)) throw new InvalidDataException("Missing workbook part: " + path);
        if (entry.Length > limit) throw new InvalidDataException("Oversized workbook XML part: " + path);
        using var input = entry.Open();
        var output = new MemoryStream();
        try
        {
            var buffer = new byte[32768]; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                _readBytes += count;
                if (output.Length + count > limit || _readBytes > XlsxDataWorkbook.MaximumExpandedBytes)
                    throw new InvalidDataException("Workbook exceeds its decompression budget.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length) throw new InvalidDataException("Workbook part length differs from its ZIP directory.");
            output.Position = 0;
            return new XlsxXmlReader(XmlReader.Create(output, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = limit, MaxCharactersFromEntities = 1024,
                CloseInput = true, IgnoreComments = true, IgnoreProcessingInstructions = true
            }));
        }
        catch { output.Dispose(); throw; }
    }

    public XElement Metadata(string path)
    {
        using var reader = OpenXml(path, 1024 * 1024);
        return XElement.Load(reader);
    }

    public Dictionary<string, XlsxRelationship> Relationships(string owner)
    {
        var slash = owner.LastIndexOf('/');
        var path = owner.Length == 0 ? "_rels/.rels"
            : owner[..(slash + 1)] + "_rels/" + owner[(slash + 1)..] + ".rels";
        var root = Metadata(path);
        XNamespace ns = RelationshipNamespace;
        if (root.Name != ns + "Relationships") throw new InvalidDataException("Invalid workbook relationship namespace.");
        var result = new Dictionary<string, XlsxRelationship>(StringComparer.Ordinal);
        foreach (var node in root.Elements(ns + "Relationship"))
        {
            var id = (string?)node.Attribute("Id"); var type = (string?)node.Attribute("Type");
            var target = (string?)node.Attribute("Target"); var mode = (string?)node.Attribute("TargetMode");
            if (string.IsNullOrEmpty(id) || id.Length > 256 || string.IsNullOrEmpty(type) || type.Length > 512
                || string.IsNullOrEmpty(target) || target.Length > 1024 || mode is not null and not "Internal" and not "External")
                throw new InvalidDataException("Invalid workbook relationship.");
            var external = mode == "External";
            if (!result.TryAdd(id, new(type, external ? target : Resolve(owner, target), external)))
                throw new InvalidDataException("Duplicate workbook relationship identity.");
        }
        return result;
    }

    public static bool HasType(string type, string name)
        => type == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + name
        || type == "http://purl.oclc.org/ooxml/officeDocument/relationships/" + name;

    public static string Resolve(string owner, string target)
    {
        if (target.Length == 0 || target.Length > 1024 || target.StartsWith("//", StringComparison.Ordinal)
            || target.IndexOfAny(['\\', ':', '?', '#']) >= 0 || target.Any(char.IsControl))
            throw new InvalidDataException("Unsafe workbook relationship target.");
        var parts = target.StartsWith('/') ? new List<string>()
            : owner.Split('/').SkipLast(1).ToList();
        foreach (var raw in target.TrimStart('/').Split('/'))
        {
            var part = Uri.UnescapeDataString(raw);
            if (part.Length == 0 || part.IndexOfAny(['/', '\\', ':', '?', '#']) >= 0 || part.Any(char.IsControl))
                throw new InvalidDataException("Unsafe encoded workbook path.");
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new InvalidDataException("Workbook target escapes the package.");
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        var result = string.Join('/', parts);
        if (result.Length == 0 || result.Length > 512) throw new InvalidDataException("Invalid workbook part path.");
        return result;
    }

    public void Dispose() { _zip.Dispose(); _input.Dispose(); }
}

internal sealed record XlsxRelationship(string Type, string Target, bool External);
