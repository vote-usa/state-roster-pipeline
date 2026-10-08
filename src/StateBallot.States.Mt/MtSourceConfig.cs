using StateBallot.Core;

namespace StateBallot.States.Mt;

/// <summary>
/// Named accessors over Montana's source links (data/input/mt/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class MtSourceConfig : SourceConfigBase
{
    /// <summary>The bare candidate list URL; redirects to the current election, and its dropdown lists every election.</summary>
    public string DefaultCandidateListUrl => Links.Url("election-index");

    public string CandidateListUrl(string electionId) => Links.Url("candidate-list-page", electionId: electionId);
}
