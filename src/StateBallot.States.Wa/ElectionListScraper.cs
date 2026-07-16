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

    public ElectionListScraper(HttpFetcher fetcher, WaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<(List<Election> Elections, SortedDictionary<string, string> CountyCodes)> FetchAsync()
    {
        var html = await _fetcher.GetStringAsync(_config.CandidateListUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var electionOptions = doc.QuerySelectorAll($"{Selectors.ElectionDropdown} option")
            .OfType<IHtmlOptionElement>()
            .ToList();
        if (electionOptions.Count == 0)
            throw new InvalidOperationException(
                $"No election options found at {_config.CandidateListUrl} using selector '{Selectors.ElectionDropdown}'. The page markup may have changed.");

        var elections = new List<Election>();
        foreach (var option in electionOptions)
        {
            var text = option.TextContent.Trim();
            var match = Selectors.ElectionOptionText.Match(text);
            if (!match.Success)
                continue; // placeholder rows

            elections.Add(new Election
            {
                ElectionId = option.Value.Trim(),
                Name = match.Groups["name"].Value.Trim(),
                ElectionDate = DateOnly.ParseExact(match.Groups["date"].Value, "MM/dd/yyyy", CultureInfo.InvariantCulture),
                ElectionType = match.Groups["type"].Value.Trim(),
                Jurisdiction = match.Groups["name"].Value.Contains("King Conservation", StringComparison.OrdinalIgnoreCase)
                    ? "King County"
                    : "state",
                SourceUrl = $"{_config.CandidateListUrl}?e={option.Value.Trim()}",
            });
        }

        if (elections.Count == 0)
            throw new InvalidOperationException(
                $"Election dropdown at {_config.CandidateListUrl} had options but none matched pattern '{Selectors.ElectionOptionText}'.");

        // County codes: "01" => "Adams". Code "99" is the synthetic "State" entry.
        var countyCodes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in doc.QuerySelectorAll($"{Selectors.CountyDropdown} option").OfType<IHtmlOptionElement>())
        {
            var code = option.Value.Trim();
            var name = option.TextContent.Trim();
            if (code.Length == 0 || name.Length == 0 || code == "99")
                continue;
            countyCodes[code] = name;
        }

        if (countyCodes.Count == 0)
            throw new InvalidOperationException(
                $"No county options found at {_config.CandidateListUrl} using selector '{Selectors.CountyDropdown}'.");

        return (elections, countyCodes);
    }
}
