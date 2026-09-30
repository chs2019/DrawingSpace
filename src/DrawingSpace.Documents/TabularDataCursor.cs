namespace DrawingSpace.Documents;

/// <summary>
/// UI-independent source browser state. Selection uses the exact stable source key,
/// never a displayed row number. Reuse this object when rebuilding presentation controls.
/// Source rows and refresh scope are unaffected by browsing. Not thread-safe.
/// </summary>
public sealed class TabularDataCursor
{
    public const int PageRows = 5;
    public const int PageColumns = 3;
    public CsvDataTable Source { get; }
    public TabularDataView View { get; private set; }
    public string FilterText { get; private set; } = "";
    public string? SortColumn { get; private set; }
    public bool Descending { get; private set; }
    public bool Numeric { get; private set; }
    public string? SelectedKey { get; private set; }
    public int FirstRow { get; private set; }
    public int FirstColumn { get; private set; }

    public TabularDataCursor(CsvDataTable source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        View = TabularDataView.Create(source);
    }

    public void Query(string filter, string? sortColumn = null, bool descending = false, bool numeric = false)
    {
        // Build first: failed queries must retain the prior view and selection.
        var view = TabularDataView.Create(Source, filter, sortColumn, descending, numeric);
        View = view; FilterText = filter; SortColumn = sortColumn;
        Descending = descending; Numeric = numeric; FirstRow = 0;
    }

    public void SelectKey(string? key)
    {
        if (key is not null && !Source.TryGetRow(key, out _))
            throw new ArgumentException("Unknown source row key.", nameof(key));
        SelectedKey = key;
    }

    /// <summary>Reveal an existing row. Explicitly clears a hiding filter but retains sort order.</summary>
    public void RevealKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Source.TryGetRow(key, out _)) throw new ArgumentException("Unknown source row key.", nameof(key));
        var index = FindKey(key);
        if (index < 0) { Query("", SortColumn, Descending, Numeric); index = FindKey(key); }
        SelectedKey = key; FirstRow = index / PageRows * PageRows;
    }

    public void MoveRows(int pages)
        => FirstRow = Move(FirstRow, pages, View.RowOrdinals.Count, PageRows);
    public void MoveColumns(int pages)
        => FirstColumn = Move(FirstColumn, pages, Source.Columns.Count, PageColumns);

    private int FindKey(string key)
    {
        for (var i = 0; i < View.RowOrdinals.Count; i++) if (View[i][Source.KeyColumn] == key) return i;
        return -1;
    }

    private static int Move(int first, int pages, int count, int size)
        => (int)Math.Clamp((long)first + (long)pages * size, 0L, (long)Math.Max(0, count - 1) / size * size);
}
