using StateBallot.Core;

namespace StateBallot.States.Hi;

/// <summary>
/// Named accessors over Hawaii's source links (data/input/hi/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class HiSourceConfig : SourceConfigBase
{
    public string ElectionsHomeUrl => Links.Url("elections-home");
    public string CandidateFilingPageUrl(string electionId) => Links.Url("candidate-filing-page", electionId: electionId);

    /// <summary>
    /// Any already-known-valid election id, used only to load the filing page once so its
    /// "ddlElection" dropdown, which lists every year's report, can be read to find the
    /// target year's real id. HI's ids are opaque and don't follow from the year.
    /// </summary>
    public string BootstrapElectionId => Links.Parameter("bootstrap-election-id");

    public const string ExportToCsvButtonName = "ctl00$cphFooter$rdgSearch$ctl00$ctl02$ctl00$ExportToCsvButton";
}
