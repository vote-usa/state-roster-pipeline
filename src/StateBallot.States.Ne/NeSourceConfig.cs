using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>
/// Named accessors over Nebraska's source links (data/input/ne/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class NeSourceConfig : SourceConfigBase
{
    public string ElectionsPageUrl => Links.Url("elections-page");

    public string StatewideCandidateFilingListUrl(int year) => Links.Url("candidate-filing-list", year: year);
}
