using System.Collections.ObjectModel;

namespace DrawingSpace.Documents;

public sealed partial class CsvDataTable
{
    /// <summary>Creates an immutable keyed snapshot without CSV encoding or reparsing. All input collections are copied.</summary>
    public static CsvDataTable FromRows(string sourceId, string keyColumn,
        IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        Identifier(sourceId, "source identity"); Identifier(keyColumn, "key column");
        if (columns.Count is < 1 or > MaximumColumns) throw new InvalidDataException("Invalid table column count.");
        var names = new string[columns.Count];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var keyIndex = -1;
        long characters = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var name = columns[i]?.Trim() ?? throw new InvalidDataException("Null column name.");
            Identifier(name, "column name");
            if (name.Contains('\0') || !unique.Add(name)) throw new InvalidDataException("Invalid or duplicate column: " + name);
            names[i] = name; characters += name.Length;
            if (name == keyColumn) keyIndex = i;
        }
        if (keyIndex < 0) throw new InvalidDataException("Missing key column: " + keyColumn);
        var index = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var result = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in rows)
        {
            if (row is null || row.Count != names.Length) throw new InvalidDataException("Row width differs from its header.");
            if (result.Count == MaximumRows || (long)(result.Count + 1) * names.Length > MaximumCells)
                throw new InvalidDataException("Table exceeds its row or cell budget.");
            var values = new Dictionary<string, string>(names.Length, StringComparer.Ordinal);
            for (var i = 0; i < names.Length; i++)
            {
                var value = row[i];
                if (value is null || value.Length > MaximumCellCharacters || value.Contains('\0'))
                    throw new InvalidDataException("Invalid or oversized table value.");
                characters += value.Length;
                if (characters > MaximumCharacters) throw new InvalidDataException("Table exceeds its character budget.");
                values.Add(names[i], value);
            }
            var key = row[keyIndex]; Identifier(key, "row key");
            var frozen = new ReadOnlyDictionary<string, string>(values);
            if (!index.TryAdd(key, frozen)) throw new InvalidDataException("Duplicate row key: " + key);
            result.Add(frozen);
        }
        return new(sourceId, keyColumn, names, index, result);
    }
}
