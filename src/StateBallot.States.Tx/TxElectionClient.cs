using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Tx;

/// <summary>Client for the CivixApps getElectionsByYear endpoint.</summary>
public sealed class TxElectionClient
{
    private readonly HttpFetcher _fetcher;
    private readonly TxSourceConfig _config;

    public TxElectionClient(HttpFetcher fetcher, TxSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<List<TexasElection>> FetchElectionsByYearAsync(int year)
    {
        var json = await _fetcher.GetStringAsync(_config.ElectionsUrl(year));
        return JsonSerializer.Deserialize<List<TexasElection>>(json)
               ?? throw new InvalidOperationException($"Empty elections response for year {year}");
    }
}
