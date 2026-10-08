using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Tx;

/// <summary>Client for the CivixApps findQualifiedCandidates endpoint.</summary>
public sealed class TxCandidateClient
{
    public const string Role = "candidates";

    private readonly TxSourceConfig _config;

    public TxCandidateClient(TxSourceConfig config) => _config = config;

    public async Task CaptureCandidatesAsync(HttpFetcher fetcher, int electionYear, string electionId)
    {
        var request = new TxCandidateSearchRequest { ElectionYear = electionYear, ElectionId = int.Parse(electionId) };
        await fetcher.PostJsonAsync(_config.CandidatesUrl, request, FetchTag.Of(Role, ("election", electionId)));
    }

    public static List<TexasCandidate> Parse(string json, string electionId) =>
        JsonSerializer.Deserialize<List<TexasCandidate>>(json)
        ?? throw new InvalidOperationException($"Empty candidates response for election {electionId}");
}
