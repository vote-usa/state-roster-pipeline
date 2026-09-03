using ClosedXML.Excel;

namespace StateBallot.Core;

/// <summary>
/// Generic Excel (.xlsx) table reader for states whose source format is a
/// spreadsheet rather than HTML/JSON/PDF/delimited-text. Reads the first
/// worksheet only, treating its first used row as headers - a state-specific
/// mapper is still responsible for turning named columns into canonical rows
/// (see the mapper-class convention: TxCandidateMapper, WvCandidateMapper,
/// WaMapper, CA's per-parser ToXyzRow methods).
/// </summary>
public static class XlsxTableParser
{
    /// <summary>
    /// Parses the first worksheet's used range into rows keyed by header name
    /// (case-insensitive lookup, since header casing varies by source). Empty
    /// or header-only input yields an empty list, never null or an exception -
    /// an empty/missing source is a scraper-level concern (see ScrapeGuard),
    /// not a parsing concern.
    /// </summary>
    public static List<Dictionary<string, string>> Parse(byte[] xlsxBytes)
    {
        var rows = new List<Dictionary<string, string>>();

        using var stream = new MemoryStream(xlsxBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        var usedRange = worksheet?.RangeUsed();
        if (usedRange is null)
            return rows;

        var usedRows = usedRange.RowsUsed().ToList();
        if (usedRows.Count < 2)
            return rows; // header only, or empty

        var headerCells = usedRows[0].Cells(usedRange.FirstColumn().ColumnNumber(), usedRange.LastColumn().ColumnNumber()).ToList();
        var headers = headerCells.Select(c => c.GetString().Trim()).ToList();

        foreach (var dataRow in usedRows.Skip(1))
        {
            var cells = dataRow.Cells(usedRange.FirstColumn().ColumnNumber(), usedRange.LastColumn().ColumnNumber()).ToList();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count && i < cells.Count; i++)
            {
                if (headers[i].Length == 0)
                    continue;
                row[headers[i]] = cells[i].GetString().Trim();
            }
            rows.Add(row);
        }

        return rows;
    }
}
