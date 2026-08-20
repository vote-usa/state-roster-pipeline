using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>
/// Parses sos.ca.gov/elections/upcoming-elections: the "Statewide Elections"
/// and "Special Vacancy Elections" sections each contain a list of links like
/// "General Election - November 3, 2026" pointing at the election's detail page.
/// </summary>
public sealed class UpcomingElectionsScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly CaSourceConfig _config;

    public UpcomingElectionsScraper(HttpFetcher fetcher, CaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<List<Election>> FetchAsync()
    {
        var html = await _fetcher.GetStringAsync(_config.UpcomingElectionsUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var elections = new List<Election>();
        string? currentSection = null;
        foreach (var element in doc.QuerySelectorAll($"{CaSelectors.UpcomingSectionHeading}, ul"))
        {
            if (element.LocalName is "h2" or "h3")
            {
                currentSection = element.TextContent.Trim();
                continue;
            }

            var isStatewide = currentSection?.EndsWith(CaSelectors.StatewideSectionTitle, StringComparison.OrdinalIgnoreCase) == true;
            var isSpecialVacancy = currentSection?.EndsWith(CaSelectors.SpecialVacancySectionTitle, StringComparison.OrdinalIgnoreCase) == true;
            if (!isStatewide && !isSpecialVacancy)
                continue;

            foreach (var anchor in element.QuerySelectorAll("li a"))
            {
                var text = anchor.TextContent.Trim();
                var match = CaSelectors.ElectionLinkText.Match(text);
                if (!match.Success)
                    continue; // e.g. link to the county-administered elections page

                if (!DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                var name = match.Groups["name"].Value.Trim().TrimEnd(',');
                var url = ResolveUrl(anchor.GetAttribute("href"));
                elections.Add(new Election
                {
                    ElectionId = url is null ? name : url.TrimEnd('/').Split('/')[^1],
                    Name = text,
                    ElectionDate = date,
                    ElectionType = InferElectionType(name),
                    Jurisdiction = isStatewide ? "state" : JurisdictionFromName(name),
                    SourceUrl = url ?? _config.UpcomingElectionsUrl,
                });
            }
        }

        if (elections.Count == 0)
            throw new InvalidOperationException(
                $"No elections parsed from {_config.UpcomingElectionsUrl} " +
                $"(sections '{CaSelectors.StatewideSectionTitle}' / '{CaSelectors.SpecialVacancySectionTitle}', " +
                $"pattern '{CaSelectors.ElectionLinkText}'). The page markup may have changed.");

        return elections;
    }

    private string? ResolveUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : new Uri(new Uri(_config.UpcomingElectionsUrl), href).ToString();
    }

    internal static string InferElectionType(string name)
    {
        var isSpecial = name.Contains("Special", StringComparison.OrdinalIgnoreCase);
        if (name.Contains("Primary", StringComparison.OrdinalIgnoreCase))
            return isSpecial ? "Special Primary" : "Primary";
        if (name.Contains("General", StringComparison.OrdinalIgnoreCase))
            return isSpecial ? "Special General" : "General";
        return isSpecial ? "Special" : "Other";
    }

    /// <summary>"Congressional District 14, Special General Election" => "Congressional District 14".</summary>
    private static string JurisdictionFromName(string name)
    {
        var comma = name.IndexOf(',');
        return comma > 0 ? name[..comma].Trim() : name;
    }
}
