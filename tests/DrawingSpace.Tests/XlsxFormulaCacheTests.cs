using System.IO.Compression;
using DrawingSpace.Documents;
using Xunit;

namespace DrawingSpace.Tests;

public sealed class XlsxFormulaCacheTests
{
    [Theory]
    [InlineData("")]
    [InlineData("<v/>")]
    [InlineData("<v></v>")]
    public void UnevaluatedNumericFormulaCannotReplaceAValueWithEmptyText(string cache)
        => Assert.Throws<InvalidDataException>(() => Read("n", cache));

    [Fact]
    public void ExplicitlyStringTypedEmptyFormulaResultIsValid()
        => Assert.Equal("", Read("str", "<v/>").Table.Rows[0]["Value"]);

    private static XlsxTableResult Read(string type, string cache)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Add(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>");
            Add("_rels/.rels", $"<Relationships xmlns=\"{pkg}\"><Relationship Id=\"doc\" Type=\"{rel}/officeDocument\" Target=\"workbook.xml\"/></Relationships>");
            Add("workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sheets><sheet name=\"Data\" r:id=\"sheet\"/></sheets></workbook>");
            Add("_rels/workbook.xml.rels", $"<Relationships xmlns=\"{pkg}\"><Relationship Id=\"sheet\" Type=\"{rel}/worksheet\" Target=\"sheet.xml\"/></Relationships>");
            Add("sheet.xml", $"<worksheet xmlns=\"{ns}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Id</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Value</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>key</t></is></c><c r=\"B2\" t=\"{type}\"><f>1+1</f>{cache}</c></row></sheetData></worksheet>");
        }
        return XlsxDataWorkbook.Open(output.ToArray()).ReadTable("Data", "source", "Id");
    }
}
