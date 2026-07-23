using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>
/// Finds the "Certified List of Candidates" PDF for a specific election date on
/// a special-election detail page (e.g. /elections/upcoming-elections/2026-cd14).
/// Those pages have one dated h2 per round ("Special General Election, August 18, 2026",
/// "Special Primary Election, June 16, 2026"), each followed by document links.
/// </summary>
public sealed class SpecialElectionPageScraper
{
    private static readonly Regex SectionDate =
        new(@"(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    private static readonly Regex CertifiedListLinkText =
        new(@"Certified List of Candidates", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpFetcher _fetcher;

    public SpecialElectionPageScraper(HttpFetcher fetcher) => _fetcher = fetcher;

    /// <summary>Returns the certified-list PDF URL for the round held on <paramref name="electionDate"/>, or null if not posted.</summary>
    public async Task<string?> FindCertifiedListUrlAsync(string pageUrl, DateOnly electionDate)
    {
        var html = await _fetcher.GetStringAsync(pageUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        DateOnly? currentSectionDate = null;
        foreach (var element in doc.QuerySelectorAll("h2, a[href]"))
        {
            if (element.LocalName == "h2")
            {
                var match = SectionDate.Match(element.TextContent);
                if (match.Success && DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    currentSectionDate = date;
                continue;
            }

            if (currentSectionDate == electionDate &&
                CertifiedListLinkText.IsMatch(element.TextContent) &&
                element.GetAttribute("href") is { } href &&
                href.Contains(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : new Uri(new Uri(pageUrl), href).ToString();
            }
        }

        return null;
    }
}
