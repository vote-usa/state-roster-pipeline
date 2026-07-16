using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>
/// Parses the SoS "County Elections Offices" directory (Drupal table) into one
/// row per county, and merges FIPS codes from a data file so the county list
/// itself never lives in code.
/// </summary>
public sealed class CountyDirectoryScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly WaSourceConfig _config;

    public CountyDirectoryScraper(HttpFetcher fetcher, WaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    /// <param name="expectedCounties">County names from the VoteWA dropdown; used to validate coverage.</param>
    /// <param name="fipsFilePath">JSON file mapping county name to FIPS code.</param>
    public async Task<List<CountyDirectoryRow>> FetchAsync(ICollection<string> expectedCounties, string fipsFilePath)
    {
        var fips = File.Exists(fipsFilePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fipsFilePath)) ?? new()
            : new Dictionary<string, string>();

        var html = await _fetcher.GetStringAsync(_config.CountyElectionsOfficesUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var rows = doc.QuerySelectorAll(Selectors.CountyOfficeRows);
        if (rows.Length == 0)
            throw new InvalidOperationException(
                $"No county office rows found at {_config.CountyElectionsOfficesUrl} using selector '{Selectors.CountyOfficeRows}'.");

        // The table can contain multiple offices per county; keep the first (primary) office.
        var byCounty = new SortedDictionary<string, CountyDirectoryRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var countyName = row.QuerySelector(Selectors.CountyOfficeCountyCell)?.TextContent.Trim();
            if (string.IsNullOrEmpty(countyName) || !expectedCounties.Contains(countyName))
                continue; // skips "Statewide" / "State Elections Office" rows
            if (byCounty.ContainsKey(countyName))
                continue;

            var addressCell = row.QuerySelector(Selectors.CountyOfficeAddressCell);
            string? address = null;
            if (addressCell?.QuerySelector("p.address") is { } p)
            {
                var parts = p.QuerySelectorAll("span")
                    .Select(s => s.TextContent.Trim())
                    .Where(t => t.Length > 0 && t != "United States")
                    .ToList();
                address = parts.Count > 0 ? string.Join(", ", parts) : null;
            }
            address ??= VoterGuideClient.StripHtml(addressCell?.InnerHtml);

            var contactCell = row.QuerySelector(Selectors.CountyOfficeContactCell);
            var websiteUrl = contactCell?.QuerySelectorAll(Selectors.CountyOfficeWebsiteLink)
                .Select(a => a.GetAttribute("href"))
                .FirstOrDefault(h => h is not null && !h.Contains("email-protection") && !h.StartsWith("tel:"));

            string? phone = null;
            var contactText = contactCell?.TextContent ?? "";
            var phoneMatch = Selectors.PhoneLine.Match(contactText);
            if (phoneMatch.Success)
                phone = phoneMatch.Groups["phone"].Value.Trim();
            else
            {
                var telMatch = Regex.Match(
                    contactCell?.QuerySelector("a[href^='tel:']")?.GetAttribute("href") ?? "", @"tel:(.+)");
                if (telMatch.Success)
                    phone = telMatch.Groups[1].Value.Trim();
            }

            byCounty[countyName] = new CountyDirectoryRow
            {
                CountyName = countyName,
                CountyFips = fips.TryGetValue(countyName, out var f) ? f : null,
                ElectionsOfficeUrl = websiteUrl,
                Address = address,
                Phone = phone,
            };
        }

        var missing = expectedCounties.Where(c => !byCounty.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"County elections office directory at {_config.CountyElectionsOfficesUrl} is missing counties: {string.Join(", ", missing)}.");

        return byCounty.Values.ToList();
    }
}
