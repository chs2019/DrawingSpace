using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DrawingSpace.Visio;

public sealed record OpcRelationship(string Id, string Type, string Target, bool External);

/// <summary>Bounded in-memory OPC package. No relationship causes a network or filesystem read.</summary>
public sealed class OpcPackage
{
    private readonly Dictionary<string, byte[]> _parts = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, byte[]> Parts => _parts;
    public static OpcPackage Read(ReadOnlyMemory<byte> input, VisioReadOptions? options = null)
    {
        options ??= new();
        if (input.Length == 0 || input.Length > options.MaximumInputBytes) throw new InvalidDataException("The package exceeds the configured input budget.");
        var result = new OpcPackage(); long total = 0;
        using var stream = new MemoryStream(input.ToArray(), false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8);
        if (archive.Entries.Count > options.MaximumParts) throw new InvalidDataException("The package contains too many parts.");
        var buffer = new byte[32768];
        foreach (var entry in archive.Entries)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/')) continue;
            var name = NormalizePartName(entry.FullName);
            if (entry.Length < 0 || entry.Length > options.MaximumPartBytes || total + entry.Length > options.MaximumExpandedBytes) throw new InvalidDataException("The expanded package exceeds its budget.");
            if (result._parts.ContainsKey(name)) throw new InvalidDataException("Ambiguous duplicate package part: " + name);
            using var source = entry.Open(); using var output = new MemoryStream((int)entry.Length);
            int count;
            while ((count = source.Read(buffer)) > 0)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                total += count;
                if (output.Length + count > options.MaximumPartBytes || total > options.MaximumExpandedBytes) throw new InvalidDataException("The expanded package exceeds its budget.");
                output.Write(buffer, 0, count);
            }
            if (output.Length != entry.Length) throw new InvalidDataException("Truncated package part: " + name);
            result._parts.Add(name, output.ToArray());
        }
        if (!result._parts.ContainsKey("[Content_Types].xml")) throw new InvalidDataException("The input is not an OPC package.");
        return result;
    }

    public bool Contains(string name) => _parts.ContainsKey(NormalizePartName(name));
    public byte[]? Get(string name) => _parts.GetValueOrDefault(NormalizePartName(name));
    public void Set(string name, byte[] bytes) { ArgumentNullException.ThrowIfNull(bytes); _parts[NormalizePartName(name)] = bytes; }
    public void Remove(string name) => _parts.Remove(NormalizePartName(name));
    public XDocument? Xml(string name) => Get(name) is { } bytes ? ParseXml(bytes) : null;
    public void SetXml(string name, XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false, CheckCharacters = true })) document.Save(writer);
        Set(name, stream.ToArray());
    }
    public static XDocument ParseXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024, MaxCharactersFromEntities = 0, CloseInput = false });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    public IReadOnlyList<OpcRelationship> Relationships(string sourcePart)
    {
        var root = Xml(RelationshipPart(sourcePart))?.Root; if (root is null) return [];
        if (root.Name != VisioNamespaces.Relationships + "Relationships") throw new InvalidDataException("Invalid package relationships root.");
        var result = new List<OpcRelationship>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.Elements(VisioNamespaces.Relationships + "Relationship"))
        {
            var id = (string?)element.Attribute("Id") ?? ""; var type = (string?)element.Attribute("Type") ?? ""; var target = (string?)element.Attribute("Target") ?? "";
            if (id.Length == 0 || type.Length == 0 || target.Length == 0 || !ids.Add(id)) throw new InvalidDataException("Invalid or duplicate relationship.");
            var external = string.Equals((string?)element.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase);
            result.Add(new(id, type, external ? target : ResolvePart(sourcePart, target), external));
        }
        return result;
    }

    public byte[] Write(CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8))
            foreach (var (name, bytes) in _parts.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open(); stream.Write(bytes);
            }
        return output.ToArray();
    }

    public static string NormalizePartName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 2048 || name.IndexOfAny(['\\', '\0', ':', '?', '#']) >= 0 || name.StartsWith('/')) throw new InvalidDataException("Unsafe OPC part name.");
        var segments = name.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".." || Uri.UnescapeDataString(s) is "." or "..")) throw new InvalidDataException("Unsafe OPC part path.");
        return name;
    }

    public static string RelationshipPart(string sourcePart)
    {
        if (sourcePart.Length == 0) return "_rels/.rels";
        var name = NormalizePartName(sourcePart); var slash = name.LastIndexOf('/');
        return (slash < 0 ? "" : name[..(slash + 1)]) + "_rels/" + name[(slash + 1)..] + ".rels";
    }

    public static string ResolvePart(string sourcePart, string target)
    {
        if (target.Length > 2048 || target.IndexOfAny(['\\', '\0', ':']) >= 0) throw new InvalidDataException("Unsafe relationship target.");
        var fragment = target.IndexOf('#'); if (fragment >= 0) target = target[..fragment];
        target = Uri.UnescapeDataString(target);
        var rootRelative = target.StartsWith('/'); var segments = new List<string>();
        if (!rootRelative && sourcePart.Length > 0)
        { var slash = sourcePart.LastIndexOf('/'); if (slash >= 0) segments.AddRange(sourcePart[..slash].Split('/')); }
        foreach (var segment in target.TrimStart('/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..") { if (segments.Count == 0) throw new InvalidDataException("Relationship escapes the package root."); segments.RemoveAt(segments.Count - 1); }
            else segments.Add(segment);
        }
        return NormalizePartName(string.Join('/', segments));
    }
}
