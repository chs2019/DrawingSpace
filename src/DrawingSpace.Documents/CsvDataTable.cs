using System.Collections.ObjectModel;
using System.Text;

namespace DrawingSpace.Documents;

/// <summary>Immutable CSV snapshot. Identifiers remain strings and keys use ordinal comparison.</summary>
public sealed class CsvDataTable
{
    public const int MaximumCharacters = 4 * 1024 * 1024;
    public const int MaximumRows = 10000;
    public const int MaximumColumns = 128;
    public const int MaximumCells = 250000;
    public const int MaximumCellCharacters = 4096;

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _index;
    public string SourceId { get; }
    public string KeyColumn { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; }

    private CsvDataTable(string sourceId, string keyColumn, string[] columns,
        Dictionary<string, IReadOnlyDictionary<string, string>> index,
        List<IReadOnlyDictionary<string, string>> rows)
    {
        SourceId = sourceId; KeyColumn = keyColumn;
        Columns = Array.AsReadOnly(columns); _index = index; Rows = rows.AsReadOnly();
    }

    public bool TryGetRow(string key, out IReadOnlyDictionary<string, string> row)
        => _index.TryGetValue(key, out row!);

    public static CsvDataTable Parse(string text, string sourceId, string keyColumn, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(text);
        Identifier(sourceId, "source identity"); Identifier(keyColumn, "key column");
        if (delimiter is not ',' and not ';' and not '\t')
            throw new ArgumentOutOfRangeException(nameof(delimiter));
        if (text.Length > MaximumCharacters || text.IndexOf('\0') >= 0)
            throw new InvalidDataException("CSV exceeds its text budget or contains a null character.");

        var index = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        string[]? columns = null;
        var keyIndex = -1;
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false; var afterQuote = false; var started = false; var cells = 0;

        void Append(char value)
        {
            if (field.Length == MaximumCellCharacters)
                throw new InvalidDataException("A CSV cell exceeds 4096 characters.");
            field.Append(value);
        }
        void EndField()
        {
            if (fields.Count == MaximumColumns)
                throw new InvalidDataException("CSV exceeds 128 columns.");
            fields.Add(field.ToString()); field.Clear(); afterQuote = false;
        }
        void EndRecord()
        {
            if (!started && fields.Count == 0 && field.Length == 0) return;
            EndField();
            if (columns is null)
            {
                columns = fields.Select(value => value.Trim()).ToArray();
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < columns.Length; i++)
                {
                    Identifier(columns[i], "column name");
                    if (!unique.Add(columns[i])) throw new InvalidDataException("Duplicate CSV column: " + columns[i]);
                    if (columns[i] == keyColumn) keyIndex = i;
                }
                if (keyIndex < 0) throw new InvalidDataException("Missing CSV key column: " + keyColumn);
            }
            else
            {
                if (fields.Count != columns.Length) throw new InvalidDataException("CSV row width differs from its header.");
                if (rows.Count == MaximumRows || (cells += fields.Count) > MaximumCells)
                    throw new InvalidDataException("CSV exceeds its row or cell budget.");
                var key = fields[keyIndex]; Identifier(key, "row key");
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < columns.Length; i++) values.Add(columns[i], fields[i]);
                var row = new ReadOnlyDictionary<string, string>(values);
                if (!index.TryAdd(key, row)) throw new InvalidDataException("Duplicate CSV row key: " + key);
                rows.Add(row);
            }
            fields.Clear(); started = false;
        }

        for (var i = text.StartsWith('\uFEFF') ? 1 : 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { Append('"'); i++; }
                else { quoted = false; afterQuote = true; }
            }
            else if (c == delimiter) { EndField(); started = true; }
            else if (c is '\r' or '\n')
            {
                EndRecord();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (afterQuote) throw new InvalidDataException("Unexpected text after a closing CSV quote.");
            else if (c == '"')
            {
                if (field.Length != 0) throw new InvalidDataException("A CSV quote must start a field.");
                quoted = true; started = true;
            }
            else { Append(c); started = true; }
        }
        if (quoted) throw new InvalidDataException("Unterminated quoted CSV field.");
        EndRecord();
        if (columns is null) throw new InvalidDataException("CSV requires a header.");
        return new(sourceId, keyColumn, columns, index, rows);
    }

    private static void Identifier(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new InvalidDataException("Missing or oversized CSV " + description + ".");
    }
}
