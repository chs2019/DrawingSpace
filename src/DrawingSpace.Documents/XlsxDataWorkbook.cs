using System.Xml.Linq;

namespace DrawingSpace.Documents;

public sealed record XlsxWorksheetInfo(string Name, bool Hidden);
public sealed record XlsxTableResult(CsvDataTable Table, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Immutable, read-only XLSX source. Stores a defensive copy of compressed bytes;
/// worksheet XML is read on demand. No formulas, external relationships, macros,
/// queries or connections are executed. Excel display formats are not evaluated.
/// </summary>
public sealed partial class XlsxDataWorkbook
{
    public const int MaximumPackageBytes = 8 * 1024 * 1024;
    public const int MaximumPartBytes = 16 * 1024 * 1024;
    public const int MaximumExpandedBytes = 64 * 1024 * 1024;
    internal const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal const string StrictSpreadsheetNamespace = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private readonly byte[] _package;
    private readonly Dictionary<string, string> _sheetParts;
    private readonly string? _stringsPart;
    private readonly string[] _notices;
    public IReadOnlyList<XlsxWorksheetInfo> Worksheets { get; }

    private XlsxDataWorkbook(byte[] bytes, List<XlsxWorksheetInfo> sheets,
        Dictionary<string, string> parts, string? stringsPart, List<string> notices)
    {
        _package = bytes; Worksheets = sheets.AsReadOnly(); _sheetParts = parts;
        _stringsPart = stringsPart; _notices = notices.ToArray();
    }

    public static XlsxDataWorkbook Open(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 4 or > MaximumPackageBytes || bytes[0] != 0x50 || bytes[1] != 0x4b)
            throw new InvalidDataException("Select an unencrypted XLSX workbook within the 8 MiB compressed limit.");
        var copy = bytes.ToArray();
        using var package = new XlsxPackage(copy);
        var office = package.Relationships("").Values.Where(r => XlsxPackage.HasType(r.Type, "officeDocument")).ToArray();
        if (office.Length != 1 || office[0].External) throw new InvalidDataException("Workbook requires one internal office-document relationship.");
        var workbookPart = office[0].Target;
        XNamespace contentNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var types = package.Metadata("[Content_Types].xml");
        if (types.Name != contentNs + "Types") throw new InvalidDataException("Invalid workbook content types.");
        var declaration = types.Elements(contentNs + "Override")
            .Where(e => (string?)e.Attribute("PartName") == "/" + workbookPart).ToArray();
        if (declaration.Length != 1 || (string?)declaration[0].Attribute("ContentType") != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")
            throw new InvalidDataException("Only ordinary XLSX workbooks are supported; XLS/XLSB and macro-enabled packages are not decoded.");
        var root = package.Metadata(workbookPart);
        var ns = root.Name.Namespace;
        if (root.Name.LocalName != "workbook" || !IsSpreadsheetNamespace(ns.NamespaceName))
            throw new InvalidDataException("Invalid workbook namespace.");
        var relationships = package.Relationships(workbookPart);
        var notices = new List<string>
        {
            "Stored cell values are imported, not Excel display formats. Numeric dates remain serials; store zero-padded identifiers as text."
        };
        if (relationships.Values.Any(r => r.External || XlsxPackage.HasType(r.Type, "externalLink") || XlsxPackage.HasType(r.Type, "connections")))
            notices.Add("External links and data connections are ignored. Cached values may be stale.");
        var strings = relationships.Values.Where(r => XlsxPackage.HasType(r.Type, "sharedStrings")).ToArray();
        if (strings.Length > 1 || strings.Any(r => r.External)) throw new InvalidDataException("Invalid shared-string relationship.");
        var sheets = new List<XlsxWorksheetInfo>(); var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = root.Element(ns + "sheets")?.Elements(ns + "sheet") ?? [];
        var sheetCount = 0;
        foreach (var node in nodes)
        {
            if (++sheetCount > 256) throw new InvalidDataException("Workbook exceeds 256 sheets.");
            var name = (string?)node.Attribute("name");
            var id = (string?)node.Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"))
                ?? (string?)node.Attribute(XName.Get("id", "http://purl.oclc.org/ooxml/officeDocument/relationships"));
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || !names.Add(name)
                || id is null || !relationships.TryGetValue(id, out var rel) || rel.External)
                throw new InvalidDataException("Invalid, duplicated or external workbook sheet.");
            if (!XlsxPackage.HasType(rel.Type, "worksheet"))
            { if (notices.Count < 64) notices.Add("Non-worksheet sheet skipped: " + name); continue; }
            var state = (string?)node.Attribute("state");
            if (state is not null and not "visible" and not "hidden" and not "veryHidden")
                throw new InvalidDataException("Invalid worksheet visibility.");
            sheets.Add(new(name, state is "hidden" or "veryHidden")); parts.Add(name, rel.Target);
        }
        if (sheets.Count == 0) throw new InvalidDataException("Workbook contains no readable worksheets.");
        return new(copy, sheets, parts, strings.FirstOrDefault()?.Target, notices);
    }

    public XlsxTableResult ReadTable(string worksheet, string sourceId, string keyColumn, int headerRow = 1)
    {
        if (!_sheetParts.TryGetValue(worksheet, out var part)) throw new ArgumentException("Unknown worksheet.", nameof(worksheet));
        if (headerRow is < 1 or > 1048576) throw new ArgumentOutOfRangeException(nameof(headerRow));
        using var package = new XlsxPackage(_package);
        var notices = _notices.ToList();
        if (Worksheets.First(s => s.Name == worksheet).Hidden) notices.Add("The selected worksheet is hidden in Excel.");
        var table = ReadWorksheet(package, part, sourceId, keyColumn, headerRow, notices);
        return new(table, notices.AsReadOnly());
    }

    internal static bool IsSpreadsheetNamespace(string value)
        => value is SpreadsheetNamespace or StrictSpreadsheetNamespace;
}
