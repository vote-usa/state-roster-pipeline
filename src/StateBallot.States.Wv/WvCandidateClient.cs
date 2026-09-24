using System.Globalization;
using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Wv;

/// <summary>Client for the WV SOS candidate-web-api, including its page/size pagination.</summary>
public sealed class WvCandidateClient
{
    public const string PageRole = "candidates-page";

    private readonly WvSourceConfig _config;

    public WvCandidateClient(WvSourceConfig config) => _config = config;

    /// <summary>
    /// Captures every page for the given year/type (null type = all elections). Each page is
    /// parsed only to learn whether another page follows.
    /// </summary>
    public async Task CaptureAllAsync(HttpFetcher fetcher, int electionYear, string? electionType = null, int pageSize = 1000)
    {
        for (var page = 0; ; page++)
        {
            var request = new WestVirginiaCandidateSearchRequest
            {
                ElectionYear = electionYear,
                ElectionType = electionType,
                Page = page,
                Size = pageSize,
            };

            var json = await fetcher.PostJsonAsync(
                _config.CandidatesUrl, request, FetchTag.Of(PageRole, ("page", page.ToString(CultureInfo.InvariantCulture))));
            var count = ParsePage(json).Count;
            if (count == 0 || count < pageSize)
                return;
        }
    }

    public static List<WestVirginiaCandidate> ParsePage(string json)
    {
        var response = JsonSerializer.Deserialize<WestVirginiaCandidateResponse>(json);
        return response?.Data?.Candidates ?? new List<WestVirginiaCandidate>();
    }
}
