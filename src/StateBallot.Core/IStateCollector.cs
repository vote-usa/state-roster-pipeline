using StateBallot.Core.Raw;

namespace StateBallot.Core;

/// <summary>
/// One implementation per state, in two stages. <see cref="CaptureAsync"/> fetches every
/// payload the state needs, tagging each with a <see cref="FetchTag"/>, and parses only what
/// it needs to decide what to fetch next. <see cref="Normalize"/> builds the result from the
/// saved capture alone and never touches the network, so it can be re-run against the same
/// evidence. Mark the class with <see cref="StateCodeAttribute"/> and expose a public
/// constructor (int year, string stateDataDir[, string? inputDataRoot]) for discovery.
/// </summary>
public interface IStateCollector
{
    /// <summary>Two-letter state code, e.g. "WA".</summary>
    string StateCode { get; }

    /// <param name="asOf">"Today" for upcoming-election filtering: the capture's start date.</param>
    Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf);

    /// <summary>Builds the result from the capture and the input files. Fills <see cref="CollectResult.Sources"/>.</summary>
    CollectResult Normalize(CaptureReader capture);
}
