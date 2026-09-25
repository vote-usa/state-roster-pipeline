namespace StateBallot.States.Sc;

/// <summary>All source URLs for the SC Votes candidate portal (vrems.scvotes.sc.gov) in one place.</summary>
public sealed class ScSourceConfig
{
    public string BaseUrl { get; init; } = "https://vrems.scvotes.sc.gov";

    /// <summary>
    /// Lists every election of one kind ("General" - despite the name, this
    /// kind covers both the statewide primary and general election, plus any
    /// same-cycle special elections - "Special", or "Local") for a year, each
    /// with its own id, name, and real election date. No separate date
    /// source is needed - unlike every prior state, this one JSON call gives
    /// dates directly.
    /// </summary>
    public string ElectionsByYearUrl(int year) =>
        $"{BaseUrl}/Candidate/GetElections?electionType=General&year={year}";

    /// <summary>
    /// The candidate search POST target. A bare ElectionId field with no
    /// other filters set returns every candidate for that election
    /// un-paginated, confirmed live - the UI's own help text says as much
    /// ("click search without selecting any search options"). Confirmed to
    /// need no session/cookie/antiforgery-token setup at all - a fully
    /// stateless call, unlike the SelectElection page that leads here in a
    /// browser (which does enforce one, but that page/flow is never visited
    /// by this collector).
    /// </summary>
    public string CandidateSearchUrl => $"{BaseUrl}/Candidate/CandidateSearch/";
}
