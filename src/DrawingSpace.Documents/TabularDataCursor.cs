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
    /// <summary>The selected key's position in this view, or -1 when hidden or absent.</summary>
    public int SelectedViewIndex { get; private set; } = -1;
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
        var selectedIndex = SelectedKey is { } key ? FindKey(view, key) : -1;
        View = view; FilterText = filter; SortColumn = sortColumn;
        Descending = descending; Numeric = numeric; FirstRow = 0;
        SelectedViewIndex = selectedIndex;
    }

    public void SelectKey(string? key)
    {
        if (key is not null && !Source.TryGetRow(key, out _))
            throw new ArgumentException("Unknown source row key.", nameof(key));
        if (SelectedKey == key) return;
        var index = key is null ? -1 : FindKey(View, key);
        SelectedKey = key; SelectedViewIndex = index;
    }

    /// <summary>Reveal an existing row. Explicitly clears a hiding filter but retains sort order.</summary>
    public void RevealKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Source.TryGetRow(key, out _)) throw new ArgumentException("Unknown source row key.", nameof(key));
        var index = SelectedKey == key ? SelectedViewIndex : FindKey(View, key);
        if (index < 0)
        {
            Query("", SortColumn, Descending, Numeric);
            index = FindKey(View, key);
        }
        SelectVisibleRow(index);
    }

    /// <summary>
    /// Select a view position and reveal its row page. Does not scan or rebuild the view,
    /// clear filters, or allocate. Returns whether the selected key changed.
    /// </summary>
    public bool SelectVisibleRow(int index)
    {
        if ((uint)index >= (uint)View.RowOrdinals.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var key = View[index][Source.KeyColumn];
        var changed = key != SelectedKey;
        SelectedKey = key; SelectedViewIndex = index; FirstRow = index / PageRows * PageRows;
        return changed;
    }

    /// <summary>
    /// Move selection by a signed row offset in O(1), saturating at the view bounds.
    /// With no visible selection, select the first row of the current page instead.
    /// An empty view remains empty; navigation never removes a filter.
    /// </summary>
    public bool MoveSelection(int rows)
    {
        var count = View.RowOrdinals.Count;
        if (count == 0) return false;
        var index = SelectedViewIndex < 0 ? FirstRow
            : (int)Math.Clamp((long)SelectedViewIndex + rows, 0L, count - 1L);
        return SelectVisibleRow(index);
    }

    public void MoveRows(int pages)
        => FirstRow = Move(FirstRow, pages, View.RowOrdinals.Count, PageRows);
    public void MoveColumns(int pages)
        => FirstColumn = Move(FirstColumn, pages, Source.Columns.Count, PageColumns);

    private int FindKey(TabularDataView view, string key)
    {
        for (var i = 0; i < view.RowOrdinals.Count; i++) if (view[i][Source.KeyColumn] == key) return i;
        return -1;
    }

    private static int Move(int first, int pages, int count, int size)
        => (int)Math.Clamp((long)first + (long)pages * size, 0L, (long)Math.Max(0, count - 1) / size * size);
}
