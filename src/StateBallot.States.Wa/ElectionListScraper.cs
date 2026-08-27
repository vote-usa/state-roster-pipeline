using System.Globalization;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>
/// Parses the election and county dropdowns on voter.votewa.gov/CandidateList.aspx.
/// That page enumerates every election (with VoteWA election id, date, and type)
/// and every county with its two-digit VoteWA county code.
/// </summary>
public sealed class ElectionListScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly WaSourceConfig _config;
    private readonly string[] _dateFormats;
    private readonly Selectors _selectors;

    /// <param name="dateFormats">Accepted date formats, from data/input/wa/date_formats.json.</param>
    public ElectionListScraper(HttpFetcher fetcher, WaSourceConfig config, string[] dateFormats, Selectors selectors)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
        _selectors = selectors;
    }

    public async Task<(List<Election> Elections, SortedDictionary<string, string> CountyCodes)> FetchAsync()
    {
        var html = await _fetcher.GetStringAsync(_config.CandidateListUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var electionOptions = doc.QuerySelectorAll($"{_selectors.ElectionDropdown} option")
            .OfType<IHtmlOptionElement>()
            .ToList();
        ScrapeGuard.RequireAny(electionOptions, () =>
            $"No election options found at {_config.CandidateListUrl} using selector '{_selectors.ElectionDropdown}'. The page markup may have changed.");

        var elections = new List<Election>();
        foreach (var option in electionOptions)
        {
            var text = option.TextContent.Trim();
            var match = _selectors.ElectionOptionText.Match(text);
            if (!match.Success)
                continue; // placeholder rows

            if (!DateParsing.TryParseAny(match.Groups["date"].Value, _dateFormats, out var electionDate))
                throw new InvalidOperationException(
                    $"Election option '{text}' at {_config.CandidateListUrl} has an unrecognized date format. " +
                    "Expected 'MM/dd/yyyy'.");

            elections.Add(new Election
            {
                ElectionId = option.Value.Trim(),
                Name = match.Groups["name"].Value.Trim(),
                ElectionDate = electionDate,
                ElectionType = match.Groups["type"].Value.Trim(),
                Jurisdiction = match.Groups["name"].Value.Contains("King Conservation", StringComparison.OrdinalIgnoreCase)
                    ? "King County"
                    : "state",
                SourceUrl = $"{_config.CandidateListUrl}?e={option.Value.Trim()}",
            });
        }

        ScrapeGuard.RequireAny(elections, () =>
            $"Election dropdown at {_config.CandidateListUrl} had options but none matched pattern '{_selectors.ElectionOptionText}'.");

        // County codes: "01" => "Adams". Code "99" is the synthetic "State" entry.
        var countyCodes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in doc.QuerySelectorAll($"{_selectors.CountyDropdown} option").OfType<IHtmlOptionElement>())
        {
            var code = option.Value.Trim();
            var name = option.TextContent.Trim();
            if (code.Length == 0 || name.Length == 0 || code == "99")
                continue;
            countyCodes[code] = name;
        }

        ScrapeGuard.RequireAny(countyCodes, () =>
            $"No county options found at {_config.CandidateListUrl} using selector '{_selectors.CountyDropdown}'.");

        return (elections, countyCodes);
    }
}
