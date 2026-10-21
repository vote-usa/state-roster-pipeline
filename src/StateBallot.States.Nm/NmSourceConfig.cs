using StateBallot.Core;

namespace StateBallot.States.Nm;

/// <summary>
/// Named accessors over New Mexico's source links (data/input/nm/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class NmSourceConfig : SourceConfigBase
{
    /// <summary>
    /// The candidate list page for one election. The portal exposes no discoverable index
    /// of its own election ids, so they are hand-maintained source parameters (see <see cref="ElectionId"/>).
    /// </summary>
    public string CandidateListUrl(string electionId) => Links.Url("candidate-list-page", electionId: electionId);

    /// <summary>
    /// The evergreen "upcoming statewide elections" page. It only ever states the *next*
    /// election's date; once an election passes, its date disappears from this page.
    /// </summary>
    public string UpcomingElectionsPageUrl => Links.Url("upcoming-elections");

    /// <summary>The hand-maintained portal election id for "Primary" or "General", or null when none is set.</summary>
    public string? ElectionId(string electionType) => Links.FindParameter("election-id", electionType);
}
