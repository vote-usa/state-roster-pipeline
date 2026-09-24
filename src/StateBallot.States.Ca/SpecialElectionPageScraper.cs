using System.Globalization;
using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ca;

/// <summary>
/// Finds the "Certified List of Candidates" PDF for a specific election date on
/// a special-election detail page (e.g. /elections/upcoming-elections/2026-cd14).
/// Those pages have one dated h2 per round ("Special General Election, August 18, 2026",
/// "Special Primary Election, June 16, 2026"), each followed by document links.
/// </summary>
public static class SpecialElectionPageScraper
{
    public const string Role = "special-election-page";

    public static async Task<string?> CaptureCertifiedListUrlAsync(HttpFetcher fetcher, Election election) =>
        FindCertifiedListUrl(
            await fetcher.GetStringAsync(election.SourceUrl, FetchTag.Of(Role, CaCollector.ElectionKeys(election))),
            election.SourceUrl,
            election.ElectionDate);

    /// <summary>Returns the certified-list PDF URL for the round held on <paramref name="electionDate"/>, or null if not posted.</summary>
    public static string? FindCertifiedListUrl(string html, string pageUrl, DateOnly electionDate)
    {
        var doc = new HtmlParser().ParseDocument(html);

        DateOnly? currentSectionDate = null;
        foreach (var element in doc.QuerySelectorAll("h2, a[href]"))
        {
            if (element.LocalName == "h2")
            {
                var match = CaSelectors.SpecialElectionSectionDate.Match(element.TextContent);
                if (match.Success && DateOnly.TryParseExact(match.Groups["date"].Value, "MMMM d, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    currentSectionDate = date;
                continue;
            }

            if (currentSectionDate == electionDate &&
                CaSelectors.CertifiedListLinkText.IsMatch(element.TextContent) &&
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
