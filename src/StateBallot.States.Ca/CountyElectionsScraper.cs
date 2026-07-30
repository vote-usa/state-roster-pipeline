using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;

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
    private readonly HttpFetcher _fetcher;
    private readonly CaSourceConfig _config;

    public CountyElectionsScraper(HttpFetcher fetcher, CaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    /// <param name="expectedCounties">County names from the FIPS data file, used to validate coverage.</param>
    public async Task<List<CountyElectionEntry>> FetchAsync(ICollection<string> expectedCounties)
    {
        var html = await _fetcher.GetStringAsync(_config.CountyAdministeredElectionsUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var entries = new List<CountyElectionEntry>();
        var countiesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var heading in doc.QuerySelectorAll(CaSelectors.CountySectionHeading))
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
                    var text = Normalize(item.TextContent);
                    if (text.Length == 0 ||
                        text.Contains(CaSelectors.NoElectionsScheduledText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var match = CaSelectors.CountyElectionLine.Match(text);
                    if (!match.Success ||
                        !DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
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

        if (countiesSeen.Count == 0)
            throw new InvalidOperationException(
                $"No county sections found at {_config.CountyAdministeredElectionsUrl} " +
                $"using selector '{CaSelectors.CountySectionHeading}'. The page markup may have changed.");

        return entries;
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Replace('\u00a0', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
