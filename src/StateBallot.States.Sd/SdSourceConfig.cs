namespace StateBallot.States.Sd;

/// <summary>All source URLs for the SD SOS Voter Information Portal (VIP) in one place.</summary>
public sealed class SdSourceConfig
{
    public string PortalBaseUrl { get; init; } = "https://vip.sdsos.gov";

    /// <summary>
    /// The candidate list page for one election. VIP exposes no discoverable
    /// index of its own current election ids either (no dropdown; the one
    /// live link to it - see <see cref="UpcomingElectionsPageUrl"/> - only
    /// ever points at whichever election is presently upcoming, same
    /// limitation NM's portal has), so these are hand-maintained in
    /// data/input/sd/election_ids.json rather than derived from the year.
    /// </summary>
    public string CandidateListUrl(string electionId) =>
        $"{PortalBaseUrl}/candidatelist.aspx?eid={electionId}";

    /// <summary>
    /// The evergreen "2026 Election Information" landing page. Unlike NM's
    /// equivalent problem, this one's own election-calendar page keeps both
    /// the primary's and the general's real date in plain text even after
    /// the primary has passed - the SOS site doesn't remove past-election
    /// info the way NM's does - so dates are still scraped fresh here (not
    /// hand-maintained) via SdCollector following whichever
    /// "*-candidate-calendar.aspx" link this page currently has.
    /// </summary>
    public string UpcomingElectionsPageUrl { get; init; } =
        "https://sdsos.gov/elections-voting/upcoming-elections/general-information/default.aspx";
}
