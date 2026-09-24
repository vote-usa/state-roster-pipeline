using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Tx;

/// <summary>Client for the CivixApps getElectionsByYear endpoint.</summary>
public sealed class TxElectionClient
{
    public const string Role = "elections";

    private readonly TxSourceConfig _config;

    public TxElectionClient(TxSourceConfig config) => _config = config;

    public async Task<List<TexasElection>> CaptureElectionsByYearAsync(HttpFetcher fetcher, int year) =>
        Parse(await fetcher.GetStringAsync(_config.ElectionsUrl(year), FetchTag.Of(Role)), year);

    public static List<TexasElection> Parse(string json, int year) =>
        JsonSerializer.Deserialize<List<TexasElection>>(json)
        ?? throw new InvalidOperationException($"Empty elections response for year {year}");
}
