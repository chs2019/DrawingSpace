using DrawingSpace.Documents;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class TabularKeyboardNavigationTests
{
    private static CsvDataTable Source() => CsvDataTable.Parse("Id,Value\n0001,10\n0002,30\n0003,20", "source", "Id");

    [Fact]
    public void SelectionIndexTracksSortedAndHiddenKeysWithoutClearingFilters()
    {
        var cursor = new TabularDataCursor(Source()); cursor.SelectKey("0003");
        Assert.Equal(2, cursor.SelectedViewIndex);
        cursor.Query("", "Value", numeric: true); Assert.Equal(1, cursor.SelectedViewIndex);
        Assert.True(cursor.MoveSelection(1)); Assert.Equal("0002", cursor.SelectedKey);
        cursor.Query("0001"); Assert.Equal(-1, cursor.SelectedViewIndex);
        Assert.True(cursor.MoveSelection(1)); Assert.Equal("0001", cursor.SelectedKey);
        Assert.Equal("0001", cursor.FilterText); Assert.Single(cursor.View.RowOrdinals);
    }

    [Fact]
    public void EmptyAndSaturatingNavigationDoNotLoseIdentityOrOverflow()
    {
        var cursor = new TabularDataCursor(Source()); cursor.SelectKey("0002");
        Assert.True(cursor.MoveSelection(int.MaxValue)); Assert.Equal("0003", cursor.SelectedKey);
        Assert.False(cursor.MoveSelection(int.MaxValue));
        Assert.True(cursor.MoveSelection(int.MinValue)); Assert.Equal("0001", cursor.SelectedKey);
        cursor.Query("absent"); var view = cursor.View;
        Assert.False(cursor.MoveSelection(1)); Assert.Same(view, cursor.View);
        Assert.Equal("0001", cursor.SelectedKey); Assert.Equal(-1, cursor.SelectedViewIndex);
    }

    [Fact]
    public void InvalidSelectionAndQueriesRetainThePriorState()
    {
        var cursor = new TabularDataCursor(Source()); cursor.SelectVisibleRow(1); var view = cursor.View;
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.SelectVisibleRow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.SelectVisibleRow(3));
        Assert.Throws<ArgumentException>(() => cursor.Query("", "Missing"));
        Assert.Equal(1, cursor.SelectedViewIndex); Assert.Equal("0002", cursor.SelectedKey);
        Assert.Same(view, cursor.View);
    }

    [Fact]
    public void RevealHiddenKeyRestoresItsIndexAndKeepsStableSortOrder()
    {
        var cursor = new TabularDataCursor(Source()); cursor.Query("0001", "Value", descending: true, numeric: true);
        cursor.SelectKey("0003"); Assert.Equal(-1, cursor.SelectedViewIndex);
        cursor.RevealKey("0003"); Assert.Equal(1, cursor.SelectedViewIndex);
        Assert.Equal("", cursor.FilterText); Assert.True(cursor.Descending); Assert.True(cursor.Numeric);
        cursor.SelectKey(null); Assert.Equal(-1, cursor.SelectedViewIndex);
    }

    [Fact]
    public void RepeatedRowNavigationUsesTheExistingViewWithoutAllocating()
    {
        var text = "Id,Value\n" + string.Join("\n", Enumerable.Range(0, 10000).Select(i => $"{i:D5},{i}"));
        var cursor = new TabularDataCursor(CsvDataTable.Parse(text, "source", "Id"));
        cursor.SelectVisibleRow(5000);
        for (var i = 0; i < 100; i++) { cursor.MoveSelection(1); cursor.MoveSelection(-1); }
        var view = cursor.View; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) { cursor.MoveSelection(1); cursor.MoveSelection(-1); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0L, 1024L); Assert.Same(view, cursor.View);
        Assert.Equal("05000", cursor.SelectedKey); Assert.Equal(5000, cursor.SelectedViewIndex);
        Assert.Equal(5000, cursor.FirstRow);
    }
}
