using DrawingSpace.Documents;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class TabularDataViewTests
{
    private static CsvDataTable Source() => CsvDataTable.Parse("Id,Owner,Value\n0001,Alice,10\n0002,Bob,2\n0003,ALICE,10\n0004,Carol,n/a", "source", "Id");

    [Fact]
    public void FiltersCaseInsensitivelyWithoutCopyingOrMutatingSourceRows()
    {
        var source = Source(); var view = TabularDataView.Create(source, "alice");
        Assert.Equal(new[] { 0, 2 }, view.RowOrdinals.ToArray());
        Assert.Same(source.Rows[2], view[1]);
        Assert.Equal(4, source.Rows.Count);
        Assert.Equal("0001", source.Rows[0]["Id"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualNumericValuesRetainSourceOrderAndMissingValuesStayLast(bool descending)
    {
        var view = TabularDataView.Create(Source(), sortColumn: "Value", numeric: true, descending: descending);
        Assert.Equal(descending ? new[] { 0, 2, 1, 3 } : new[] { 1, 0, 2, 3 }, view.RowOrdinals.ToArray());
    }

    [Fact]
    public void TextSortDoesNotImplicitlyCoerceIdentifiers()
    {
        var source = CsvDataTable.Parse("Id\n10\n2\n0001", "source", "Id");
        Assert.Equal(new[] { 2, 0, 1 }, TabularDataView.Create(source, sortColumn: "Id").RowOrdinals.ToArray());
        Assert.Equal(new[] { 2, 1, 0 }, TabularDataView.Create(source, sortColumn: "Id", numeric: true).RowOrdinals.ToArray());
    }

    [Fact]
    public void ColumnFilterDoesNotMatchUnrelatedProperties()
        => Assert.Empty(TabularDataView.Create(Source(), "alice", filterColumn: "Value").RowOrdinals);

    [Fact]
    public void ViewRejectsUnknownFieldsAndExcessiveFilterLength()
    {
        Assert.Throws<ArgumentException>(() => TabularDataView.Create(Source(), sortColumn: "missing"));
        Assert.Throws<ArgumentException>(() => TabularDataView.Create(Source(), filterColumn: "missing"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TabularDataView.Create(Source(), new string('x', 257)));
    }

    [Fact]
    public void FromRowsCopiesInputAndUsesOrdinalKeys()
    {
        var columns = new[] { "Id", "Value" }; var row = new[] { "0001", "10" };
        var source = CsvDataTable.FromRows("source", "Id", columns, new[] { row });
        columns[0] = "changed"; row[0] = "other";
        Assert.Equal("Id", source.Columns[0]);
        Assert.True(source.TryGetRow("0001", out var result));
        Assert.Equal("10", result["Value"]);
    }

    [Fact]
    public void FromRowsRejectsNullsDuplicateHeadersAndDuplicateKeys()
    {
        Assert.Throws<InvalidDataException>(() => CsvDataTable.FromRows("source", "Id", new[] { "Id", "Id" }, Array.Empty<string[]>()));
        Assert.Throws<InvalidDataException>(() => CsvDataTable.FromRows("source", "Id", new[] { "Id" }, new[] { new[] { "x" }, new[] { "x" } }));
        Assert.Throws<InvalidDataException>(() => CsvDataTable.FromRows("source", "Id", new[] { "Id", "Value" }, new[] { new[] { "x", null! } }));
    }
}
