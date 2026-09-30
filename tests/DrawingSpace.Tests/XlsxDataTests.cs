using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DrawingSpace.Documents;
using DrawingSpace.Editing;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class XlsxDataTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static string Inline(string address, string text) => new XElement(XName.Get("c", Ns),
        new XAttribute("r", address), new XAttribute("t", "inlineStr"),
        new XElement(XName.Get("is", Ns), new XElement(XName.Get("t", Ns), text))).ToString(SaveOptions.DisableFormatting);
    private static string Header(int row = 1) => $"<row r=\"{row}\">{Inline("A" + row, "Id")}{Inline("B" + row, "Progress")}</row>";
    private static string Data(string id, string value, int row = 2) => $"<row r=\"{row}\">{Inline("A" + row, id)}<c r=\"B{row}\"><v>{value}</v></c></row>";

    private static byte[] Workbook(string rows, string? sharedStrings = null,
        string sheetName = "Assets", string state = "visible", string tail = "", string rootTarget = "xl/workbook.xml",
        string sheetTarget = "worksheets/sheet7.xml", bool strict = false, string? contentType = null,
        string? worksheetPrefix = null, Action<ZipArchive>? extra = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string path, string text)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(strict ? text.Replace(Ns, "http://purl.oclc.org/ooxml/spreadsheetml/main", StringComparison.Ordinal)
                    .Replace(Rel, "http://purl.oclc.org/ooxml/officeDocument/relationships", StringComparison.Ordinal) : text);
            }
            Add("[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/xl/workbook.xml\" ContentType=\"{contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"}\"/></Types>");
            Add("_rels/.rels", $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"doc\" Type=\"{Rel}/officeDocument\" Target=\"{rootTarget}\"/></Relationships>");
            Add("xl/workbook.xml", $"<workbook xmlns=\"{Ns}\" xmlns:r=\"{Rel}\"><sheets><sheet name=\"{sheetName}\" sheetId=\"7\" state=\"{state}\" r:id=\"sheet\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"sheet\" Type=\"{Rel}/worksheet\" Target=\"{sheetTarget}\"/>" +
                (sharedStrings is null ? "" : $"<Relationship Id=\"strings\" Type=\"{Rel}/sharedStrings\" Target=\"sharedStrings.xml\"/>") + "</Relationships>");
            Add("xl/worksheets/sheet7.xml", (worksheetPrefix ?? "") + $"<worksheet xmlns=\"{Ns}\"><dimension ref=\"A1:XFD1048576\"/><sheetData>{rows}</sheetData>{tail}</worksheet>");
            if (sharedStrings is not null) Add("xl/sharedStrings.xml", $"<sst xmlns=\"{Ns}\">{sharedStrings}</sst>");
            extra?.Invoke(zip);
        }
        return output.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsRelationshipSelectedWorksheetInTransitionalAndStrictPackages(bool strict)
    {
        var book = XlsxDataWorkbook.Open(Workbook(Header() + Data("0001", "25"), strict: strict));
        var result = book.ReadTable("Assets", "source", "Id");
        Assert.Equal("Assets", Assert.Single(book.Worksheets).Name);
        Assert.True(result.Table.TryGetRow("0001", out var row));
        Assert.Equal("25", row["Progress"]);
        Assert.False(result.Table.TryGetRow("1", out _));
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void RichSharedStringsExcludePhoneticAnnotationsAndDecodeBooleans()
    {
        var strings = "<si><r><t>00</t></r><r><t>01</t></r><rPh sb=\"0\" eb=\"2\"><t>phonetic</t></rPh></si>";
        var rows = Header() + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>0</v></c><c r=\"B2\" t=\"b\"><v>1</v></c></row>";
        var source = XlsxDataWorkbook.Open(Workbook(rows, strings)).ReadTable("Assets", "source", "Id").Table;
        Assert.Equal("TRUE", Assert.Single(source.Rows)["Progress"]);
        Assert.Equal("0001", source.Rows[0]["Id"]);
    }

    [Fact]
    public void FormulaUsesCacheWithDiagnosticAndNeverEvaluatesFormulaText()
    {
        var rows = Header() + $"<row r=\"2\">{Inline("A2", "0001")}<c r=\"B2\"><f>WEBSERVICE(&quot;https://example.invalid&quot;)</f><v>18</v></c></row>";
        var result = XlsxDataWorkbook.Open(Workbook(rows)).ReadTable("Assets", "source", "Id");
        Assert.Equal("18", result.Table.Rows[0]["Progress"]);
        Assert.Contains(result.Diagnostics, item => item.Contains("cached", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingFormulaCacheIsRejectedBeforeRefreshingShapes()
    {
        var rows = Header() + $"<row r=\"2\">{Inline("A2", "0001")}<c r=\"B2\"><f>1+1</f></c></row>";
        var book = XlsxDataWorkbook.Open(Workbook(rows));
        Assert.Throws<InvalidDataException>(() => book.ReadTable("Assets", "source", "Id"));
    }

    [Fact]
    public void ExplicitHeaderRowAndSparseRowsDoNotExpandDeclaredDimensions()
    {
        var rows = $"<row r=\"1\">{Inline("A1", "Notes")}</row>" + Header(5)
            + $"<row r=\"900000\">{Inline("A900000", "0001")}</row>";
        var result = XlsxDataWorkbook.Open(Workbook(rows)).ReadTable("Assets", "source", "Id", 5);
        Assert.Equal("", Assert.Single(result.Table.Rows)["Progress"]);
    }

    [Fact]
    public void WorkbookOwnsAnImmutableCopyOfTheCompressedInput()
    {
        var bytes = Workbook(Header() + Data("0001", "25"));
        var book = XlsxDataWorkbook.Open(bytes);
        Array.Clear(bytes);
        Assert.Equal("25", book.ReadTable("Assets", "source", "Id").Table.Rows[0]["Progress"]);
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("veryHidden")]
    public void HiddenSheetsAreExplicitAndReported(string state)
    {
        var book = XlsxDataWorkbook.Open(Workbook(Header() + Data("0001", "25"), state: state));
        Assert.True(book.Worksheets[0].Hidden);
        Assert.Contains(book.ReadTable("Assets", "source", "Id").Diagnostics, d => d.Contains("hidden", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../escape.xml")]
    [InlineData("https://example.invalid/data")]
    [InlineData("xl/%2e%2e/%2e%2e/escape.xml")]
    [InlineData("xl%2fworkbook.xml")]
    public void UnsafePackageTargetsAreRejected(string target)
        => Assert.Throws<InvalidDataException>(() => XlsxDataWorkbook.Open(Workbook(Header(), rootTarget: target)));

    [Fact]
    public void DuplicateZipPartAliasesAreRejected()
        => Assert.Throws<InvalidDataException>(() => XlsxDataWorkbook.Open(Workbook(Header(), extra: zip => zip.CreateEntry("XL/WORKBOOK.XML"))));

    [Fact]
    public void MacrosAreRejectedRatherThanDecodedAsOrdinaryWorkbooks()
        => Assert.Throws<InvalidDataException>(() => XlsxDataWorkbook.Open(Workbook(Header(), contentType: "application/vnd.ms-excel.sheet.macroEnabled.main+xml")));

    [Fact]
    public void XmlDtdsAreProhibited()
    {
        var book = XlsxDataWorkbook.Open(Workbook(Header(), worksheetPrefix: "<!DOCTYPE worksheet [<!ENTITY test 'value'>]>"));
        Assert.Throws<XmlException>(() => book.ReadTable("Assets", "source", "Id"));
    }

    [Fact]
    public void MergedCellsRequireExplicitCleanup()
    {
        var book = XlsxDataWorkbook.Open(Workbook(Header(), tail: "<mergeCells count=\"1\"><mergeCell ref=\"A1:B1\"/></mergeCells>"));
        Assert.Throws<InvalidDataException>(() => book.ReadTable("Assets", "source", "Id"));
    }

    [Theory]
    [InlineData("e", "#DIV/0!")]
    [InlineData("s", "999")]
    [InlineData("b", "3")]
    [InlineData("n", "NaN")]
    [InlineData("unsupported", "1")]
    public void InvalidCellValuesAreNotSilentlyImported(string type, string value)
    {
        var rows = Header() + $"<row r=\"2\">{Inline("A2", "0001")}<c r=\"B2\" t=\"{type}\"><v>{value}</v></c></row>";
        var book = XlsxDataWorkbook.Open(Workbook(rows));
        Assert.Throws<InvalidDataException>(() => book.ReadTable("Assets", "source", "Id"));
    }

    [Fact]
    public void DuplicateSourceKeysAreRejected()
    {
        var book = XlsxDataWorkbook.Open(Workbook(Header() + Data("0001", "10") + Data("0001", "20", 3)));
        Assert.Throws<InvalidDataException>(() => book.ReadTable("Assets", "source", "Id"));
    }

    [Fact]
    public void ExcelSnapshotsUseExistingConflictSafeRefreshAndUndo()
    {
        var document = new DiagramDocument(); document.Pages[0].Shapes.Add(new() { Id = "shape", Text = "0001" });
        var session = new EditorSession(document);
        var source = XlsxDataWorkbook.Open(Workbook(Header() + Data("0001", "25"))).ReadTable("Assets", "source", "Id").Table;
        session.ApplyDataRefresh(session.PreviewDataRefresh(source));
        Assert.Equal("25", session.Page.Find("shape")!.Data["Progress"]);
        session.Undo(); Assert.Empty(session.Page.Find("shape")!.Data);
        session.Redo(); Assert.Equal("0001", session.Page.Find("shape")!.DataBinding!.RowKey);
    }
}
