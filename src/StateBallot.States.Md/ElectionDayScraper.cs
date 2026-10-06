using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Md;

/// <summary>
/// Parses the MD SBE's yearly elections landing page for "Primary Election
/// Day" / "General Election Day" entries. MD comments an election's timeline
/// block out of the page (rather than removing the text) once it has passed,
/// so this naturally surfaces only the still-current election(s) - no
/// separate "already happened" filtering is needed the way WA/CA/TX do it.
/// </summary>
public sealed class ElectionDayScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly MdSourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/md/date_formats.json.</param>
    public ElectionDayScraper(HttpFetcher fetcher, MdSourceConfig config, string[] dateFormats)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
    }

    public async Task<List<Election>> FetchAsync(int year)
    {
        var url = _config.ElectionsPageUrl(year);
        var html = await _fetcher.GetStringAsync(url);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var elections = new List<Election>();
        foreach (var dt in doc.QuerySelectorAll("dt"))
        {
            var match = MdSelectors.ElectionDayLabel.Match(dt.TextContent.Trim());
            if (!match.Success)
                continue;

            var dateText = dt.NextElementSibling?.TextContent.Trim();
            if (dateText is null || !DateParsing.TryParseAny(dateText, _dateFormats, out var date))
                continue;

            elections.Add(MdCandidateMapper.ToElection(year, match.Groups["type"].Value, date, url));
        }

        ScrapeGuard.RequireAny(elections, () =>
            $"No election day entries parsed from {url} using pattern '{MdSelectors.ElectionDayLabel}'. The page markup may have changed.");

        return elections;
    }
}
