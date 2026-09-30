using System.Collections;
using System.Globalization;

namespace DrawingSpace.Documents;

/// <summary>Immutable source-ordinal view. Filtering and stable sorting never copy source rows.</summary>
public sealed class TabularDataView
{
    public CsvDataTable Source { get; }
    public IReadOnlyList<int> RowOrdinals { get; }
    public IReadOnlyDictionary<string, string> this[int row] => Source.Rows[RowOrdinals[row]];
    private TabularDataView(CsvDataTable source, IReadOnlyList<int> ordinals)
    { Source = source; RowOrdinals = ordinals; }

    public static TabularDataView Create(CsvDataTable source, string filter = "", string? sortColumn = null,
        bool descending = false, bool numeric = false, string? filterColumn = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(filter);
        if (filter.Length > 256) throw new ArgumentOutOfRangeException(nameof(filter));
        if (sortColumn is not null && !source.Columns.Contains(sortColumn)) throw new ArgumentException("Unknown sort column.", nameof(sortColumn));
        if (filterColumn is not null && !source.Columns.Contains(filterColumn)) throw new ArgumentException("Unknown filter column.", nameof(filterColumn));
        // The identity view needs no O(row count) ordinal buffer or temporary List.
        if (filter.Length == 0 && sortColumn is null) return new(source, new IdentityOrdinals(source.Rows.Count));
        var ordinals = new List<int>(source.Rows.Count);
        for (var i = 0; i < source.Rows.Count; i++)
        {
            var row = source.Rows[i]; var matches = filter.Length == 0;
            if (!matches && filterColumn is not null) matches = row[filterColumn].Contains(filter, StringComparison.OrdinalIgnoreCase);
            else if (!matches)
                foreach (var column in source.Columns)
                    if (row[column].Contains(filter, StringComparison.OrdinalIgnoreCase)) { matches = true; break; }
            if (matches) ordinals.Add(i);
        }
        var values = ordinals.ToArray();
        if (sortColumn is not null)
        {
            double?[]? numbers = numeric ? new double?[source.Rows.Count] : null;
            if (numbers is not null)
                foreach (var i in values)
                    if (double.TryParse(source.Rows[i][sortColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)) numbers[i] = number;
            Array.Sort(values, (a, b) =>
            {
                int comparison;
                if (numbers is not null)
                {
                    var x = numbers[a]; var y = numbers[b];
                    if (x.HasValue != y.HasValue) return x.HasValue ? -1 : 1;
                    comparison = x.HasValue ? x.Value.CompareTo(y!.Value) : StringComparer.Ordinal.Compare(source.Rows[a][sortColumn], source.Rows[b][sortColumn]);
                }
                else comparison = StringComparer.Ordinal.Compare(source.Rows[a][sortColumn], source.Rows[b][sortColumn]);
                if (comparison != 0) return descending ? -comparison : comparison;
                return a.CompareTo(b);
            });
        }
        return new(source, Array.AsReadOnly(values));
    }

    private sealed class IdentityOrdinals(int count) : IReadOnlyList<int>
    {
        public int Count => count;
        public int this[int index] => (uint)index < (uint)count ? index : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<int> GetEnumerator() { for (var i = 0; i < count; i++) yield return i; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
