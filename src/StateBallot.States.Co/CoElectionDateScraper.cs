using StateBallot.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StateBallot.States.Co;

/// <summary>
/// Parses the CO SOS's statutory election calendar PDF for the year's actual
/// Primary/General election dates - the only place either date is published;
/// the candidate list pages/XLSX only ever carry a text label, never a date
/// (see CoSourceConfig.ElectionCalendarPdfUrl).
/// </summary>
public sealed class CoElectionDateScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly CoSourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/co/date_formats.json.</param>
    public CoElectionDateScraper(HttpFetcher fetcher, CoSourceConfig config, string[] dateFormats)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
    }

    /// <summary>Returns null when the requested year's calendar hasn't been published yet (404).</summary>
    public async Task<List<Election>?> TryFetchAsync(int year)
    {
        var url = _config.ElectionCalendarPdfUrl(year);
        var bytes = await _fetcher.TryGetBytesAsync(url);
        if (bytes is null)
            return null;

        using var pdf = PdfDocument.Open(bytes);
        var firstPage = pdf.GetPage(1);
        var text = ContentOrderTextExtractor.GetText(firstPage);

        var elections = new List<Election>();
        TryAddElection(elections, CoSelectors.PrimaryElectionDateLine, "Primary", url);
        TryAddElection(elections, CoSelectors.GeneralElectionDateLine, "General", url);

        ScrapeGuard.RequireAny(elections, () =>
            $"No Primary/General election date line matched on {url}'s first page. The calendar's layout may have changed.");

        return elections;

        void TryAddElection(List<Election> list, System.Text.RegularExpressions.Regex pattern, string type, string sourceUrl)
        {
            var match = pattern.Match(text);
            if (!match.Success)
                return;
            if (!DateParsing.TryParseAny(match.Groups["date"].Value, _dateFormats, out var date))
                return;
            list.Add(CoCandidateMapper.ToElection(type, date, sourceUrl));
        }
    }
}
