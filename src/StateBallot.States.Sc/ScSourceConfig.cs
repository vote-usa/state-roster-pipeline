using StateBallot.Core;

namespace StateBallot.States.Sc;

/// <summary>
/// Named accessors over South Carolina's source links (data/input/sc/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class ScSourceConfig : SourceConfigBase
{
    public string ElectionsByYearUrl(int year) => Links.Url("elections", year: year);
    public string CandidateSearchUrl => Links.Url("candidate-search");
}
