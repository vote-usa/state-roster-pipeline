using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>
/// Named accessors over Washington's source links (data/input/wa/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class WaSourceConfig : SourceConfigBase
{
    public string CandidateListUrl => Links.Url("election-list");

    /// <summary>VoteWA voters' guide JSON: statewide with an empty county code, one county otherwise.</summary>
    public string VoterGuideUrl(string electionId, string countyCode = "") =>
        Links.Url("voter-guide", electionId: electionId, countyCode: countyCode);

    public string StatewideMeasuresUrl => Links.Url("statewide-measures");
    public string CountyElectionsOfficesUrl => Links.Url("county-directory");
}
