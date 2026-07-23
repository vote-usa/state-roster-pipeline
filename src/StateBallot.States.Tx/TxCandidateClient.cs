using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Tx;

/// <summary>Client for the CivixApps findQualifiedCandidates endpoint.</summary>
public sealed class TxCandidateClient
{
    private readonly HttpFetcher _fetcher;
    private readonly TxSourceConfig _config;

    public TxCandidateClient(HttpFetcher fetcher, TxSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<List<TexasCandidate>> FetchCandidatesAsync(int electionYear, int electionId)
    {
        var request = new TxCandidateSearchRequest { ElectionYear = electionYear, ElectionId = electionId };
        var json = await _fetcher.PostJsonAsync(_config.CandidatesUrl, request);
        return JsonSerializer.Deserialize<List<TexasCandidate>>(json)
               ?? throw new InvalidOperationException($"Empty candidates response for election {electionId}");
    }
}
