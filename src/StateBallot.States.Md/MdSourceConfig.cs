using StateBallot.Core;

namespace StateBallot.States.Md;

/// <summary>
/// Named accessors over Maryland's source links (data/input/md/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class MdSourceConfig : SourceConfigBase
{
    /// <summary>Yearly landing page listing "Primary Election Day" / "General Election Day".</summary>
    public string ElectionsPageUrl(int year) => Links.Url("elections-page", year: year);

    /// <summary>
    /// Statewide candidate list CSV (federal/state/judicial races only - see MdCollector
    /// remarks on the local/county "all_counties" export this doesn't cover yet).
    /// </summary>
    /// <param name="kind">"primary" or "general" (case-insensitive); anything else is treated as general.</param>
    public string StatewideCandidateListUrl(int year, string kind) =>
        Links.Url("candidate-list", PrimaryOrGeneral(kind), year: year);
}
