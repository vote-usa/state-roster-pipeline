using StateBallot.Core;

namespace StateBallot.States.Wy;

/// <summary>
/// Named accessors over Wyoming's source links (data/input/wy/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class WySourceConfig : SourceConfigBase
{
    public string ElectionInfoPageUrl(int year) => Links.Url("election-info-page", year: year);

    /// <param name="kind">"Primary" or "General" (case-insensitive).</param>
    public string CandidateListUrl(int year, string kind) => Links.Url("candidate-list", kind, year: year);
}
