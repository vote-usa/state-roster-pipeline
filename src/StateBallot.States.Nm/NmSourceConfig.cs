namespace StateBallot.States.Nm;

/// <summary>All source URLs for the NM SOS candidate portal in one place.</summary>
public sealed class NmSourceConfig
{
    public string PortalBaseUrl { get; init; } = "https://candidateportal.servis.sos.state.nm.us";

    /// <summary>
    /// The candidate list page for one election. The portal exposes no
    /// discoverable index of its own current election ids - no election
    /// dropdown, no other election's id linked anywhere on the page, and the
    /// one nominally "current" link on sos.nm.gov's own site is a stale link
    /// to a 2021 election. The id must be re-derived by a human each cycle
    /// (visit <see cref="PortalBaseUrl"/> and check candidate pages'
    /// <c>&lt;title&gt;</c>, or web-search "candidateportal.servis.sos.state.nm.us
    /// CandidateList eid &lt;year&gt;") and stored in
    /// data/input/nm/election_ids.json - see NmCollector.
    /// </summary>
    public string CandidateListUrl(string electionId) =>
        $"{PortalBaseUrl}/CandidateList.aspx?eid={electionId}&cty=99";

    /// <summary>
    /// The evergreen "upcoming statewide elections" page. Only ever states
    /// the *next* election's date in plain text (e.g. "2026 General Election:
    /// Tuesday, November 3, 2026") - once an election passes, its date
    /// disappears from this page entirely (confirmed: the SOS site also
    /// removes its own per-election date/results pages once they're no
    /// longer current, unlike every other state onboarded so far) - so this
    /// only ever works for whichever election is presently upcoming.
    /// </summary>
    public string UpcomingElectionsPageUrl { get; init; } =
        "https://www.sos.nm.gov/voting-and-elections/view-all-elections/";
}
