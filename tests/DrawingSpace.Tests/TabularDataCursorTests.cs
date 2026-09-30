using DrawingSpace.Documents;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class TabularDataCursorTests
{
    private static CsvDataTable Source(int rows = 12)
        => CsvDataTable.Parse("Id,Value,A,B\n" + string.Join("\n", Enumerable.Range(0, rows).Select(i => $"{i:0000},{rows - i},x,y")), "Assets", "Id");

    [Fact] public void SelectionSurvivesSortingFilteringAndPagingWithoutChangingTheSource()
    {
        var source = Source(); var cursor = new TabularDataCursor(source); cursor.SelectKey("0008");
        cursor.Query("", "Value", numeric: true); Assert.Equal("0011", cursor.View[0]["Id"]);
        Assert.Equal("0008", cursor.SelectedKey); Assert.Equal("0000", source.Rows[0]["Id"]);
        cursor.Query("0001"); Assert.Single(cursor.View.RowOrdinals); Assert.Equal("0008", cursor.SelectedKey);
        cursor.RevealKey("0008"); Assert.Equal("", cursor.FilterText); Assert.Equal(5, cursor.FirstRow);
        Assert.Same(source.Rows[8], cursor.View[8]);
    }

    [Fact] public void RevealClearsOnlyAHidingFilterAndRetainsOrder()
    {
        var cursor = new TabularDataCursor(Source()); cursor.Query("", "Value", descending: true, numeric: true);
        cursor.RevealKey("0011"); Assert.Equal(10, cursor.FirstRow); Assert.Equal("Value", cursor.SortColumn);
        cursor.Query("000", "Value", numeric: true); cursor.RevealKey("0008"); Assert.Equal("000", cursor.FilterText);
        cursor.RevealKey("0011"); Assert.Equal("", cursor.FilterText); Assert.Equal("Value", cursor.SortColumn); Assert.True(cursor.Numeric);
    }

    [Fact] public void InvalidQueriesAndKeysPreserveTheCursor()
    {
        var cursor = new TabularDataCursor(Source()); cursor.SelectKey("0001"); cursor.MoveRows(1);
        var view = cursor.View;
        Assert.Throws<ArgumentException>(() => cursor.Query("", "Missing"));
        Assert.Throws<ArgumentException>(() => cursor.RevealKey("1"));
        Assert.Throws<ArgumentException>(() => cursor.SelectKey("1"));
        Assert.Same(view, cursor.View); Assert.Equal(5, cursor.FirstRow); Assert.Equal("0001", cursor.SelectedKey);
    }

    [Fact] public void NavigationClampsWithoutIntegerOverflow()
    {
        var cursor = new TabularDataCursor(Source()); cursor.MoveRows(int.MaxValue); Assert.Equal(10, cursor.FirstRow);
        cursor.MoveRows(int.MinValue); Assert.Equal(0, cursor.FirstRow);
        cursor.MoveColumns(int.MaxValue); Assert.Equal(3, cursor.FirstColumn);
        cursor.MoveColumns(int.MinValue); Assert.Equal(0, cursor.FirstColumn);
        cursor.Query("not present"); cursor.MoveRows(int.MaxValue); Assert.Equal(0, cursor.FirstRow);
    }

    [Fact] public void IdentityOrdinalsHaveReadOnlyBoundsCheckedSequenceSemantics()
    {
        var source = Source(); var view = TabularDataView.Create(source);
        Assert.Equal(Enumerable.Range(0, source.Rows.Count), view.RowOrdinals);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.RowOrdinals[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.RowOrdinals[source.Rows.Count]);
        Assert.Same(source.Rows[7], view[7]);
        Assert.Empty(TabularDataView.Create(Source(0)).RowOrdinals);
    }

    [Fact] public void IdentityViewAllocationDoesNotScaleWithSourceRowCount()
    {
        var source = Source(10000);
        for (var i = 0; i < 32; i++) _ = TabularDataView.Create(source);
        var before = GC.GetAllocatedBytesForCurrentThread(); var count = 0;
        for (var i = 0; i < 100; i++) count += TabularDataView.Create(source).RowOrdinals.Count;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1000000, count); Assert.InRange(allocated, 0L, 128000L);
    }
}
