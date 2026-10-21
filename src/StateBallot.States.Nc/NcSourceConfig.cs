using StateBallot.Core;

namespace StateBallot.States.Nc;

/// <summary>
/// Named accessors over North Carolina's source links (data/input/nc/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class NcSourceConfig : SourceConfigBase
{
    public string CandidateListingUrl(int year) => Links.Url("candidate-listing", year: year);
}
