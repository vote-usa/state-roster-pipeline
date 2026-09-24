using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ca;

/// <summary>
/// Parses the SoS "County Elections Offices" page: one h2 per county (linking
/// to the county elections site) followed by a paragraph with the official's
/// name, street address, phone numbers, hours, website, and e-mail separated
/// by line breaks. The authoritative county list comes from the FIPS data file,
/// never from code.
/// </summary>
public sealed class CountyDirectoryScraper
{
    public const string Role = "county-directory";

    private readonly CaSourceConfig _config;

    public CountyDirectoryScraper(CaSourceConfig config) => _config = config;

    public async Task CaptureAsync(HttpFetcher fetcher) =>
        await fetcher.GetStringAsync(_config.CountyElectionsOfficesUrl, FetchTag.Of(Role));

    /// <param name="fipsFilePath">JSON file mapping county name to FIPS code; also defines the expected county set.</param>
    public List<CountyDirectoryRow> Parse(string html, string fipsFilePath)
    {
        if (!File.Exists(fipsFilePath))
            throw new InvalidOperationException(
                $"County FIPS data file not found at {fipsFilePath}; it defines California's expected county list.");
        var fips = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fipsFilePath))
                   ?? throw new InvalidOperationException($"County FIPS data file {fipsFilePath} is empty.");

        var doc = new HtmlParser().ParseDocument(html);

        var byCounty = new SortedDictionary<string, CountyDirectoryRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in doc.QuerySelectorAll(CaSelectors.CountySectionHeading))
        {
            var countyName = heading.TextContent.Trim();
            if (!fips.ContainsKey(countyName) || byCounty.ContainsKey(countyName))
                continue; // sidebar/nav headings, or a duplicate section

            // The office paragraph is the <br>-richest paragraph before the next
            // county heading (some sections start with a stray bare-link paragraph).
            var officeParagraph = FollowingParagraphs(heading)
                .OrderByDescending(p => p.QuerySelectorAll("br").Length)
                .FirstOrDefault();

            var lines = officeParagraph is null
                ? new List<string>()
                : ParagraphLines(officeParagraph);

            var websiteUrl = heading.QuerySelector("a[href^='http']")?.GetAttribute("href")
                ?? officeParagraph?.QuerySelectorAll("a[href^='http']")
                    .Select(a => a.GetAttribute("href"))
                    .FirstOrDefault();

            byCounty[countyName] = new CountyDirectoryRow
            {
                CountyName = countyName,
                CountyFips = fips[countyName],
                ElectionsOfficeUrl = websiteUrl,
                Address = ExtractAddress(lines),
                Phone = lines.Select(l => CaSelectors.PhoneLine.Match(l))
                    .FirstOrDefault(m => m.Success)?.Value.Trim(),
            };
        }

        var missing = fips.Keys.Where(c => !byCounty.ContainsKey(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"County elections office directory at {_config.CountyElectionsOfficesUrl} is missing " +
                $"{missing.Count} of {fips.Count} counties: {string.Join(", ", missing)}. The page markup may have changed.");

        return byCounty.Values.ToList();
    }

    private static IEnumerable<IElement> FollowingParagraphs(IElement heading)
    {
        for (var sibling = heading.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.LocalName == heading.LocalName)
                yield break;
            if (sibling.LocalName == "p")
                yield return sibling;
        }
    }

    /// <summary>Splits a paragraph on &lt;br&gt; boundaries into trimmed text lines.</summary>
    private static List<string> ParagraphLines(IElement paragraph)
    {
        // Replace <br> nodes with newline markers, then read the text content.
        var clone = (IElement)paragraph.Clone();
        foreach (var br in clone.QuerySelectorAll("br").ToList())
            br.ReplaceWith(clone.Owner!.CreateTextNode("\n"));

        return clone.TextContent
            .Replace('\u00a0', ' ')
            .Split('\n')
            .Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Street address = the lines between the official's name (line 0) and the
    /// first phone / mailing-address / hours line.
    /// </summary>
    private static string? ExtractAddress(List<string> lines)
    {
        var addressLines = lines
            .Skip(1)
            .TakeWhile(l => !CaSelectors.PhoneLine.IsMatch(l)
                            && !l.StartsWith("Mailing Address", StringComparison.OrdinalIgnoreCase)
                            && !l.StartsWith("Hours", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return addressLines.Count > 0 ? string.Join(", ", addressLines) : null;
    }
}
