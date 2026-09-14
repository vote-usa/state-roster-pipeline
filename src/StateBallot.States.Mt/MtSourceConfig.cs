namespace StateBallot.States.Mt;

/// <summary>All source URLs for the MT SOS candidate filing portal in one place.</summary>
public sealed class MtSourceConfig
{
    public string PortalBaseUrl { get; init; } = "https://candidatefiling.mt.gov/candidatefiling";

    /// <summary>
    /// The bare candidate-list page, with no election id at all. Unlike NM's
    /// and SD's equivalent portals, this one needs no hand-maintained lookup:
    /// requesting it with no <c>e=</c> redirects (302) to whatever election is
    /// presently current, and *that* page's own <c>ddlElection</c> dropdown
    /// lists every election - including both the primary and the general, each
    /// with its own id, name, and real date embedded right in the option text
    /// (confirmed live: "FEDERAL PRIMARY 2026 (06/02/2026) (Primary)") - so a
    /// single fetch (HttpFetcher follows the redirect transparently) discovers
    /// everything this collector needs. See MtCollector.
    /// </summary>
    public string DefaultCandidateListUrl => $"{PortalBaseUrl}/CandidateList.aspx";

    public string CandidateListUrl(string electionId) => $"{PortalBaseUrl}/CandidateList.aspx?e={electionId}";
}
