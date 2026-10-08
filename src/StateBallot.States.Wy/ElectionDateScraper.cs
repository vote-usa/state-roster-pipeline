using System.Text.Json;
using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Wy;

/// <summary>
/// Parses the WY SOS elections info page's embedded schema.org JSON-LD for
/// "Event" entries named "20NN Wyoming Primary/General Election" - the only
/// place either election's actual date is published; the candidate CSVs
/// themselves only ever carry a text label ("2026 PRIMARY ELECTION"), never a
/// date. The events are nested arbitrarily deep inside the organization's
/// department list, so this walks the whole JSON-LD tree rather than assuming
/// a fixed path.
/// </summary>
public sealed class ElectionDateScraper
{
    public const string Role = "election-info-page";

    private readonly WySourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/wy/date_formats.json.</param>
    public ElectionDateScraper(WySourceConfig config, string[] dateFormats)
    {
        _config = config;
        _dateFormats = dateFormats;
    }

    public async Task<List<Election>> CaptureAsync(HttpFetcher fetcher, int year) =>
        Parse(await fetcher.GetStringAsync(_config.ElectionInfoPageUrl(year), FetchTag.Of(Role)), year);

    public List<Election> Parse(string html, int year)
    {
        var url = _config.ElectionInfoPageUrl(year);
        var doc = new HtmlParser().ParseDocument(html);

        var elections = new List<Election>();
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            using var json = JsonDocument.Parse(script.TextContent);
            foreach (var (name, startDate) in FindEvents(json.RootElement))
            {
                var match = WySelectors.ElectionEventName.Match(name);
                if (!match.Success)
                    continue;
                if (!DateParsing.TryParseAny(startDate, _dateFormats, out var date))
                    continue;

                elections.Add(WyCandidateMapper.ToElection(match.Groups["type"].Value, date, url));
            }
        }

        ScrapeGuard.RequireAny(elections, () =>
            $"No election Event entries parsed from {url} using pattern '{WySelectors.ElectionEventName}'. The page's JSON-LD may have changed.");

        return elections;
    }

    private static IEnumerable<(string Name, string StartDate)> FindEvents(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("@type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString() == "Event" &&
                    element.TryGetProperty("name", out var name) &&
                    element.TryGetProperty("startDate", out var startDate))
                {
                    yield return (name.GetString() ?? "", startDate.GetString() ?? "");
                }
                foreach (var property in element.EnumerateObject())
                    foreach (var found in FindEvents(property.Value))
                        yield return found;
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var found in FindEvents(item))
                        yield return found;
                break;
        }
    }
}
