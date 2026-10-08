using StateBallot.Core;

namespace StateBallot.States.Sd;

/// <summary>
/// Named accessors over South Dakota's source links (data/input/sd/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class SdSourceConfig : SourceConfigBase
{
    /// <summary>
    /// The candidate list page for one election. VIP exposes no discoverable index of its
    /// own election ids either, so they are hand-maintained source parameters (see <see cref="ElectionId"/>).
    /// </summary>
    public string CandidateListUrl(string electionId) => Links.Url("candidate-list-page", electionId: electionId);

    /// <summary>
    /// The evergreen election information landing page. Its election-calendar page keeps both
    /// the primary's and the general's date even after the primary, so dates are scraped fresh.
    /// </summary>
    public string UpcomingElectionsPageUrl => Links.Url("upcoming-elections");

    /// <summary>The hand-maintained VIP election id for "Primary" or "General", or null when none is set.</summary>
    public string? ElectionId(string electionType) => Links.FindParameter("election-id", electionType);
}
