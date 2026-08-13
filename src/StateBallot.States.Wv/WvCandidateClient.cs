using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Wv;

/// <summary>Client for the WV SOS candidate-web-api, including its page/size pagination.</summary>
public sealed class WvCandidateClient
{
    private readonly HttpFetcher _fetcher;
    private readonly WvSourceConfig _config;

    public WvCandidateClient(HttpFetcher fetcher, WvSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    private async Task<List<WestVirginiaCandidate>> FetchPageAsync(int electionYear, string? electionType, int page, int size)
    {
        var request = new WestVirginiaCandidateSearchRequest
        {
            ElectionYear = electionYear,
            ElectionType = electionType,
            Page = page,
            Size = size,
        };

        var json = await _fetcher.PostJsonAsync(_config.CandidatesUrl, request);
        var response = JsonSerializer.Deserialize<WestVirginiaCandidateResponse>(json);
        return response?.Data?.Candidates ?? new List<WestVirginiaCandidate>();
    }

    /// <summary>Fetches every page for the given year/type (null type = all elections).</summary>
    public async Task<List<WestVirginiaCandidate>> FetchAllAsync(int electionYear, string? electionType = null, int pageSize = 1000)
    {
        var all = new List<WestVirginiaCandidate>();
        var page = 0;

        while (true)
        {
            var candidates = await FetchPageAsync(electionYear, electionType, page, pageSize);
            if (candidates.Count == 0)
                break;

            all.AddRange(candidates);
            if (candidates.Count < pageSize)
                break;

            page++;
        }

        return all;
    }
}
