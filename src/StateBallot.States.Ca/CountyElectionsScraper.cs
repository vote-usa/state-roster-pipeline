using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ca;

/// <summary>One scheduled election on a county's list on the county-administered elections page.</summary>
public sealed record CountyElectionEntry(string CountyName, string? CountyUrl, DateOnly Date, string Name, string? DetailUrl);

/// <summary>
/// Parses the SoS "County Administered Elections" page: one h2 per county
/// (linking to the county elections site) followed by a list of scheduled
/// local elections ("August 25, 2026 – Special Election") or a
/// "No elections scheduled at this time." placeholder.
/// </summary>
public sealed class CountyElectionsScraper
{
    public const string Role = "county-elections";

    private readonly CaSourceConfig _config;
    private readonly string[] _dateFormats;
    private readonly CaSelectors _selectors;

    /// <param name="dateFormats">Accepted date formats, from data/input/ca/date_formats.json.</param>
    public CountyElectionsScraper(CaSourceConfig config, string[] dateFormats, CaSelectors selectors)
    {
        _config = config;
        _dateFormats = dateFormats;
        _selectors = selectors;
    }

    public async Task CaptureAsync(HttpFetcher fetcher) =>
        await fetcher.GetStringAsync(_config.CountyAdministeredElectionsUrl, FetchTag.Of(Role));

    /// <param name="expectedCounties">County names from the FIPS data file, used to validate coverage.</param>
    public List<CountyElectionEntry> Parse(string html, ICollection<string> expectedCounties)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var entries = new List<CountyElectionEntry>();
        var countiesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var heading in doc.QuerySelectorAll(_selectors.CountySectionHeading))
        {
            var countyName = heading.TextContent.Trim();
            if (!expectedCounties.Contains(countyName) || !countiesSeen.Add(countyName))
                continue;

            var countyUrl = heading.QuerySelector("a[href^='http']")?.GetAttribute("href");

            for (var sibling = heading.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
            {
                if (sibling.LocalName == heading.LocalName)
                    break;
                if (sibling.LocalName != "ul")
                    continue;

                foreach (var item in sibling.QuerySelectorAll("li"))
                {
                    var text = TextNormalization.CollapseWhitespace(item.TextContent);
                    if (text.Length == 0 ||
                        text.Contains(_selectors.NoElectionsScheduledText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = _selectors.CountyElectionLine.Match(text);
                    if (!match.Success ||
                        !DateParsing.TryParseAny(match.Groups["date"].Value, _dateFormats, out var date))
                        continue;

                    entries.Add(new CountyElectionEntry(
                        countyName,
                        countyUrl,
                        date,
                        match.Groups["name"].Value.Trim(),
                        item.QuerySelector("a[href^='http']")?.GetAttribute("href")));
                }
            }
        }

        ScrapeGuard.RequireAny(countiesSeen, () =>
            $"No county sections found at {_config.CountyAdministeredElectionsUrl} " +
            $"using selector '{_selectors.CountySectionHeading}'. The page markup may have changed.");

        return entries;
    }
}
