using ClosedXML.Excel;

namespace StateBallot.Core.Tests;

public class XlsxTableParserTests
{
    private static byte[] BuildWorkbook(string[] headers, params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Candidates");

        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        for (var r = 0; r < rows.Length; r++)
            for (var col = 0; col < rows[r].Length; col++)
                sheet.Cell(r + 2, col + 1).Value = rows[r][col];

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildMultiSheetWorkbook(
        (string Name, string[] Headers, string[][] Rows)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, headers, rows) in sheets)
        {
            var sheet = workbook.Worksheets.Add(name);
            for (var col = 0; col < headers.Length; col++)
                sheet.Cell(1, col + 1).Value = headers[col];
            for (var r = 0; r < rows.Length; r++)
                for (var col = 0; col < rows[r].Length; col++)
                    sheet.Cell(r + 2, col + 1).Value = rows[r][col];
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Parse_ReturnsRowsKeyedByHeader()
    {
        var bytes = BuildWorkbook(
            ["Name", "Party", "District"],
            ["Jane Doe", "Democratic", "4"],
            ["John Smith", "Republican", "7"]);

        var rows = XlsxTableParser.Parse(bytes);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Jane Doe", rows[0]["Name"]);
        Assert.Equal("Democratic", rows[0]["Party"]);
        Assert.Equal("4", rows[0]["District"]);
        Assert.Equal("John Smith", rows[1]["Name"]);
    }

    [Fact]
    public void Parse_HeaderLookupIsCaseInsensitive()
    {
        var bytes = BuildWorkbook(["NAME", "party"], ["Jane Doe", "Democratic"]);

        var rows = XlsxTableParser.Parse(bytes);

        Assert.Equal("Jane Doe", rows[0]["name"]);
        Assert.Equal("Democratic", rows[0]["PARTY"]);
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmptyList()
    {
        var bytes = BuildWorkbook(["Name", "Party"]);

        var rows = XlsxTableParser.Parse(bytes);

        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_TrimsCellWhitespace()
    {
        var bytes = BuildWorkbook(["Name"], ["  Jane Doe  "]);

        var rows = XlsxTableParser.Parse(bytes);

        Assert.Equal("Jane Doe", rows[0]["Name"]);
    }

    [Fact]
    public void Parse_DefaultsToFirstSheet()
    {
        var bytes = BuildMultiSheetWorkbook([
            ("Candidates", ["Name"], [["Jane Doe"]]),
            ("Retention", ["Judge"], [["John Smith"]]),
        ]);

        var rows = XlsxTableParser.Parse(bytes);

        Assert.Single(rows);
        Assert.Equal("Jane Doe", rows[0]["Name"]);
    }

    [Fact]
    public void Parse_SheetIndex_ReadsRequestedSheetInstead()
    {
        var bytes = BuildMultiSheetWorkbook([
            ("Candidates", ["Name"], [["Jane Doe"]]),
            ("Retention", ["Judge"], [["John Smith"]]),
        ]);

        var rows = XlsxTableParser.Parse(bytes, sheetIndex: 1);

        Assert.Single(rows);
        Assert.Equal("John Smith", rows[0]["Judge"]);
    }

    [Fact]
    public void Parse_SheetIndexOutOfRange_ReturnsEmptyList()
    {
        var bytes = BuildWorkbook(["Name"], ["Jane Doe"]);

        var rows = XlsxTableParser.Parse(bytes, sheetIndex: 5);

        Assert.Empty(rows);
    }
}
