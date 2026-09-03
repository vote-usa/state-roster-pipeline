using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace StateBallot.Core;

/// <summary>
/// Generic delimited-text (CSV/TSV) table reader for states whose source
/// format is a flat delimited file rather than HTML/JSON/PDF. Deliberately
/// format-agnostic about column names - a state-specific mapper is still
/// responsible for turning named columns into canonical rows (see the
/// mapper-class convention: TxCandidateMapper, WvCandidateMapper, WaMapper,
/// CA's per-parser ToXyzRow methods).
/// </summary>
public static class DelimitedTableParser
{
    /// <summary>
    /// Parses delimited text into rows keyed by header name (case-insensitive
    /// lookup, since header casing varies by source). The first non-empty line
    /// is treated as the header row. Empty input yields an empty list, never
    /// null or an exception - an empty/missing source is a scraper-level
    /// concern (see ScrapeGuard), not a parsing concern.
    /// </summary>
    public static List<Dictionary<string, string>> Parse(string text, char delimiter = ',')
    {
        var rows = new List<Dictionary<string, string>>();
        using var reader = new StringReader(text);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null,
        };
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { Length: > 0 } headers)
            return rows;

        while (csv.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                if (header.Length == 0)
                    continue;
                row[header] = csv.GetField(header) ?? "";
            }
            rows.Add(row);
        }

        return rows;
    }
}
