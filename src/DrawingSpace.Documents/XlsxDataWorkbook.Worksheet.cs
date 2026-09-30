using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DrawingSpace.Documents;

public sealed partial class XlsxDataWorkbook
{
    private CsvDataTable ReadWorksheet(XlsxPackage package, string part, string sourceId,
        string keyColumn, int headerRow, List<string> diagnostics)
    {
        using var reader = package.OpenXml(part);
        reader.MoveToContent();
        if (reader.LocalName != "worksheet" || !IsSpreadsheetNamespace(reader.NamespaceURI))
            throw new InvalidDataException("Invalid worksheet XML.");
        var ns = reader.NamespaceURI;
        string[]? headers = null;
        var rows = new List<IReadOnlyList<string>>();
        List<string>? shared = null;
        var firstColumn = 0; var previousRow = 0; var cellCount = 0; var visitedRows = 0;
        var sawData = false; var formulaNotice = false; var hiddenNotice = false;
        long characters = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != ns) continue;
            if (reader.LocalName == "mergeCell") throw new InvalidDataException("Unmerge worksheet cells before importing tabular data.");
            if (reader.LocalName != "sheetData" || reader.Depth != 1) continue;
            if (sawData) throw new InvalidDataException("Duplicate worksheet data section.");
            sawData = true;
            using var data = reader.ReadSubtree();
            while (data.Read())
            {
                if (data.NodeType != XmlNodeType.Element || data.LocalName != "row" || data.NamespaceURI != ns || data.Depth != 1) continue;
                if (++visitedRows > CsvDataTable.MaximumRows + 1024) throw new InvalidDataException("Worksheet exceeds its scanned-row budget.");
                var number = data.GetAttribute("r") is { } rowText ? Positive(rowText, 1048576, "row") : previousRow + 1;
                if (number <= previousRow || number > 1048576) throw new InvalidDataException("Worksheet rows are duplicated or unordered.");
                previousRow = number;
                if (!hiddenNotice && number >= headerRow && data.GetAttribute("hidden") is "1" or "true")
                { diagnostics.Add("Hidden worksheet rows are included in the source snapshot."); hiddenNotice = true; }
                var cells = new SortedDictionary<int, string>(); var previousColumn = -1;
                using (var row = data.ReadSubtree())
                {
                    while (row.Read())
                    {
                        if (row.NodeType != XmlNodeType.Element || row.LocalName != "c" || row.NamespaceURI != ns || row.Depth != 1) continue;
                        if (++cellCount > CsvDataTable.MaximumCells) throw new InvalidDataException("Worksheet exceeds 250,000 scanned cells.");
                        var column = previousColumn + 1;
                        if (row.GetAttribute("r") is { } reference)
                        {
                            var address = Address(reference);
                            if (address.Row != number) throw new InvalidDataException("Cell address does not match its worksheet row.");
                            column = address.Column;
                        }
                        if (column <= previousColumn || column >= 16384) throw new InvalidDataException("Worksheet cells are duplicated or unordered.");
                        previousColumn = column;
                        if (number < headerRow) continue;
                        XElement cell;
                        using (var subtree = row.ReadSubtree()) cell = XElement.Load(subtree);
                        XNamespace cellNs = ns;
                        var formula = cell.Element(cellNs + "f");
                        var valueNode = cell.Element(cellNs + "v");
                        if (formula is not null)
                        {
                            if (valueNode is null) throw new InvalidDataException("Formula cell has no cached value. Recalculate and save in Excel first.");
                            if (!formulaNotice) { diagnostics.Add("Formula cells use saved cached results; formulas are not executed or refreshed."); formulaNotice = true; }
                        }
                        var value = ReadCell(cell, cellNs, () => shared ??= ReadSharedStrings(package));
                        characters += value.Length;
                        if (characters > CsvDataTable.MaximumCharacters) throw new InvalidDataException("Worksheet exceeds its decoded character budget.");
                        if (value.Length != 0) cells.Add(column, value);
                    }
                }
                if (number < headerRow) continue;
                if (number == headerRow)
                {
                    if (cells.Count == 0) throw new InvalidDataException("The selected header row is empty.");
                    firstColumn = cells.Keys.First();
                    var width = cells.Keys.Last() - firstColumn + 1;
                    if (width > CsvDataTable.MaximumColumns) throw new InvalidDataException("The header exceeds 128 columns.");
                    headers = new string[width];
                    for (var i = 0; i < width; i++) headers[i] = cells.GetValueOrDefault(firstColumn + i, "");
                }
                else
                {
                    if (headers is null) throw new InvalidDataException("The selected header row is missing.");
                    if (cells.Count == 0) continue;
                    if (cells.Keys.First() < firstColumn || cells.Keys.Last() >= firstColumn + headers.Length)
                        throw new InvalidDataException("A data cell is outside the selected header columns.");
                    if (rows.Count == CsvDataTable.MaximumRows || (long)(rows.Count + 1) * headers.Length > CsvDataTable.MaximumCells)
                        throw new InvalidDataException("Worksheet exceeds its dense table budget.");
                    var values = new string[headers.Length];
                    for (var i = 0; i < values.Length; i++) values[i] = cells.GetValueOrDefault(firstColumn + i, "");
                    rows.Add(values);
                }
            }
        }
        if (headers is null) throw new InvalidDataException("The selected header row is missing.");
        return CsvDataTable.FromRows(sourceId, keyColumn, headers, rows);
    }

    private static string ReadCell(XElement cell, XNamespace ns, Func<List<string>> shared)
    {
        var text = cell.Element(ns + "v")?.Value ?? "";
        string result;
        switch ((string?)cell.Attribute("t") ?? "n")
        {
            case "s":
                var index = Positive(text, 250000, "shared string", allowZero: true);
                var strings = shared();
                if (index >= strings.Count) throw new InvalidDataException("Shared-string index is out of range.");
                result = strings[index]; break;
            case "inlineStr":
                if (cell.Element(ns + "f") is not null) throw new InvalidDataException("Formula result cannot be inline text.");
                result = Text(cell.Element(ns + "is"), ns); break;
            case "str": case "d": result = text; break;
            case "b": result = text switch { "0" => "FALSE", "1" => "TRUE", _ => throw new InvalidDataException("Invalid Boolean cell.") }; break;
            case "e": throw new InvalidDataException("Resolve Excel cell error before linking: " + text[..Math.Min(text.Length, 80)]);
            case "n":
                if (text.Length != 0 && (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric) || !double.IsFinite(numeric)))
                    throw new InvalidDataException("Invalid numeric workbook value.");
                result = text; break;
            default: throw new InvalidDataException("Unsupported workbook cell type.");
        }
        if (result.Length > CsvDataTable.MaximumCellCharacters) throw new InvalidDataException("Workbook cell exceeds 4096 characters.");
        return result;
    }

    private List<string> ReadSharedStrings(XlsxPackage package)
    {
        if (_stringsPart is null) throw new InvalidDataException("Workbook uses shared strings without a shared-string table.");
        using var reader = package.OpenXml(_stringsPart);
        reader.MoveToContent();
        if (reader.LocalName != "sst" || !IsSpreadsheetNamespace(reader.NamespaceURI)) throw new InvalidDataException("Invalid shared-string table.");
        var ns = reader.NamespaceURI; var result = new List<string>(); long characters = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "si" || reader.NamespaceURI != ns || reader.Depth != 1) continue;
            if (result.Count == 250000) throw new InvalidDataException("Shared-string table exceeds its entry budget.");
            XElement item;
            using (var subtree = reader.ReadSubtree()) item = XElement.Load(subtree);
            var text = Text(item, ns); characters += text.Length;
            if (characters > CsvDataTable.MaximumCharacters) throw new InvalidDataException("Shared strings exceed their decoded character budget.");
            result.Add(text);
        }
        return result;
    }

    private static string Text(XElement? item, XNamespace ns)
    {
        if (item is null) return "";
        var output = new StringBuilder();
        foreach (var child in item.Elements())
        {
            var value = child.Name == ns + "t" ? child.Value : child.Name == ns + "r" ? child.Element(ns + "t")?.Value : null;
            if (value is null) continue; // Phonetic annotations are not part of the value.
            if (output.Length + value.Length > CsvDataTable.MaximumCellCharacters) throw new InvalidDataException("Shared/inline text exceeds its cell budget.");
            output.Append(value);
        }
        return output.ToString();
    }

    private static int Positive(string text, int maximum, string kind, bool allowZero = false)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < (allowZero ? 0 : 1) || value > maximum)
            throw new InvalidDataException("Invalid workbook " + kind + " index.");
        return value;
    }

    private static (int Column, int Row) Address(string reference)
    {
        var i = 0; var column = 0;
        while (i < reference.Length && reference[i] is >= 'A' and <= 'Z')
        {
            column = column * 26 + reference[i++] - 'A' + 1;
            if (column > 16384) throw new InvalidDataException("Cell address exceeds Excel column bounds.");
        }
        if (i == 0 || i == reference.Length) throw new InvalidDataException("Invalid workbook cell address.");
        return (column - 1, Positive(reference[i..], 1048576, "cell row"));
    }
}
