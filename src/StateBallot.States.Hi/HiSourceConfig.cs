namespace StateBallot.States.Hi;

/// <summary>All source URLs for the HI Office of Elections sites in one place.</summary>
public sealed class HiSourceConfig
{
    public string ElectionsHomeUrl { get; init; } = "https://elections.hawaii.gov/";

    public string OlvrBaseUrl { get; init; } = "https://olvr.hawaii.gov";

    public string CandidateFilingPageUrl(string electionId) => $"{OlvrBaseUrl}/Controls/CandidateFiling.aspx?elid={electionId}";

    /// <summary>
    /// Any already-known-valid election id, used only to load the page once so
    /// its "ddlElection" dropdown - which lists every year's report, old and
    /// new - can be read to find the *target* year's real id (HI's ids are
    /// opaque and don't follow a formula from the year, unlike every other
    /// state's URLs so far). New id values get appended to this same dropdown
    /// over time, so a still-recent id should keep working as an entry point
    /// for years beyond the one it happens to name.
    /// </summary>
    public string BootstrapElectionId { get; init; } = "94";

    public const string ExportToCsvButtonName = "ctl00$cphFooter$rdgSearch$ctl00$ctl02$ctl00$ExportToCsvButton";
}
