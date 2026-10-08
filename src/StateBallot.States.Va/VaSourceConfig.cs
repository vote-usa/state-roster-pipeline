using StateBallot.Core;

namespace StateBallot.States.Va;

/// <summary>
/// Named accessors over Virginia's source links (data/input/va/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class VaSourceConfig : SourceConfigBase
{
    /// <summary>Lists each election's own page, which links that election's candidate XLSX.</summary>
    public string CandidateListIndexUrl => Links.Url("candidate-list-index");
}
