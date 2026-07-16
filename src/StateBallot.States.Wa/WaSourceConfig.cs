namespace StateBallot.States.Wa;

/// <summary>
/// All source URLs and URL templates in one place so that next year's site
/// changes only require edits here (or overrides on the CLI).
/// </summary>
public sealed class WaSourceConfig
{
    /// <summary>WebForms page whose dropdowns enumerate elections and county codes.</summary>
    public string CandidateListUrl { get; init; } = "https://voter.votewa.gov/CandidateList.aspx";

    /// <summary>JSON API behind the VoteWA online voters' guide.</summary>
    public string VoterGuideApiBase { get; init; } = "https://voter.votewa.gov/elections";

    public string VoterGuideUrl(string electionId, string countyCode = "") =>
        $"{VoterGuideApiBase}/voterguide.ashx?e={electionId}&la=en&c={countyCode}&p=";

    public string MeasureDetailUrl(string raceId, string electionId) =>
        $"{VoterGuideApiBase}/measure.ashx?m={raceId}&e={electionId}&la=en&c=";

    /// <summary>SoS page listing proposed/certified statewide ballot measures.</summary>
    public string StatewideMeasuresUrl { get; init; } = "https://www.sos.wa.gov/so/node/12667";

    /// <summary>SoS directory of county elections offices.</summary>
    public string CountyElectionsOfficesUrl { get; init; } =
        "https://www.sos.wa.gov/elections/voters/voter-registration/county-elections-offices";
}
