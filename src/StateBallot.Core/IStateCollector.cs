namespace StateBallot.Core;

/// <summary>
/// One implementation per state. A collector fetches the state's upcoming
/// elections, candidates, measures, county directory, and per-county ballots,
/// and fills in the provenance manifest (SourceGroups/NextRun) on the result.
/// </summary>
public interface IStateCollector
{
    /// <summary>Two-letter state code, e.g. "WA".</summary>
    string StateCode { get; }

    Task<CollectResult> CollectAsync();
}
