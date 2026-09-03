using ClosedXML.Excel;

namespace StateBallot.Core;

/// <summary>
/// Generic Excel (.xlsx) table reader for states whose source format is a
/// spreadsheet rather than HTML/JSON/PDF/delimited-text. Reads one worksheet
/// (the first, by default), treating its first used row as headers - a
/// state-specific mapper is still responsible for turning named columns into
/// canonical rows (see the mapper-class convention: TxCandidateMapper,
/// WvCandidateMapper, WaMapper, CA's per-parser ToXyzRow methods).
/// </summary>
public static class XlsxTableParser
{
    /// <summary>
    /// Parses one worksheet's used range into rows keyed by header name
    /// (case-insensitive lookup, since header casing varies by source). Empty
    /// or header-only input yields an empty list, never null or an exception -
    /// an empty/missing source is a scraper-level concern (see ScrapeGuard),
    /// not a parsing concern.
    /// </summary>
    /// <param name="sheetIndex">
    /// Zero-based worksheet index (default 0, the first sheet - every prior
    /// caller's behavior is unchanged). Nebraska's statewide filing workbook is
    /// the first consumer to need a second sheet (its own judicial-retention
    /// questions live on sheet index 1, alongside sheet 0's candidates).
    /// </param>
    public static List<Dictionary<string, string>> Parse(byte[] xlsxBytes, int sheetIndex = 0)
    {
        var rows = new List<Dictionary<string, string>>();

        using var stream = new MemoryStream(xlsxBytes);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.ElementAtOrDefault(sheetIndex);
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
