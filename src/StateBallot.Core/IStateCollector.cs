namespace StateBallot.Core;

/// <summary>
/// One implementation per state. A collector fetches the state's upcoming
/// elections, candidates, measures, county directory, and per-county ballots,
/// and fills <see cref="CollectResult.Sources"/>. Mark the class with
/// <see cref="StateCodeAttribute"/> and expose a public constructor
/// (HttpFetcher, int year, string stateDataDir) for assembly discovery.
/// </summary>
public interface IStateCollector
{
    /// <summary>Two-letter state code, e.g. "WA".</summary>
    string StateCode { get; }

    Task<CollectResult> CollectAsync();
}
