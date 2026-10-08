using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ca;

/// <summary>
/// Parses sos.ca.gov/elections/upcoming-elections: the "Statewide Elections"
/// and "Special Vacancy Elections" sections each contain a list of links like
/// "General Election - November 3, 2026" pointing at the election's detail page.
/// </summary>
public sealed class UpcomingElectionsScraper
{
    public const string Role = "upcoming-elections";

    private readonly CaSourceConfig _config;
    private readonly string[] _dateFormats;
    private readonly CaSelectors _selectors;

    /// <param name="dateFormats">Accepted date formats, from data/input/ca/date_formats.json.</param>
    public UpcomingElectionsScraper(CaSourceConfig config, string[] dateFormats, CaSelectors selectors)
    {
        _config = config;
        _dateFormats = dateFormats;
        _selectors = selectors;
    }

    public async Task<List<Election>> CaptureAsync(HttpFetcher fetcher) =>
        Parse(await fetcher.GetStringAsync(_config.UpcomingElectionsUrl, FetchTag.Of(Role)));

    public List<Election> Parse(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var elections = new List<Election>();
        string? currentSection = null;
        foreach (var element in doc.QuerySelectorAll($"{_selectors.UpcomingSectionHeading}, ul"))
        {
            if (element.LocalName is "h2" or "h3")
            {
                currentSection = element.TextContent.Trim();
                continue;
            }

            var isStatewide = currentSection?.EndsWith(_selectors.StatewideSectionTitle, StringComparison.OrdinalIgnoreCase) == true;
            var isSpecialVacancy = currentSection?.EndsWith(_selectors.SpecialVacancySectionTitle, StringComparison.OrdinalIgnoreCase) == true;
            if (!isStatewide && !isSpecialVacancy)
                continue;

            foreach (var anchor in element.QuerySelectorAll("li a"))
            {
                var text = anchor.TextContent.Trim();
                var match = _selectors.ElectionLinkText.Match(text);
                if (!match.Success)
                    continue; // e.g. link to the county-administered elections page

                if (!DateParsing.TryParseAny(match.Groups["date"].Value, _dateFormats, out var date))
                    continue;

                var name = match.Groups["name"].Value.Trim().TrimEnd(',');
                var url = ResolveUrl(anchor.GetAttribute("href"));
                elections.Add(ToElection(text, name, date, url, isStatewide, _config.UpcomingElectionsUrl));
            }
        }

        ScrapeGuard.RequireAny(elections, () =>
            $"No elections parsed from {_config.UpcomingElectionsUrl} " +
            $"(sections '{_selectors.StatewideSectionTitle}' / '{_selectors.SpecialVacancySectionTitle}', " +
            $"pattern '{_selectors.ElectionLinkText}'). The page markup may have changed.");

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

    internal static Election ToElection(
        string linkText, string name, DateOnly date, string? url, bool isStatewide, string fallbackSourceUrl) => new()
    {
        ElectionId = url is null ? name : url.TrimEnd('/').Split('/')[^1],
        Name = linkText,
        ElectionDate = date,
        ElectionType = InferElectionType(name),
        Jurisdiction = isStatewide ? "state" : JurisdictionFromName(name),
        SourceUrl = url ?? fallbackSourceUrl,
    };

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
