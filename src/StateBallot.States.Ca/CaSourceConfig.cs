using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>
/// Named accessors over California's source links (data/input/ca/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class CaSourceConfig : SourceConfigBase
{
    public string UpcomingElectionsUrl => Links.Url("upcoming-elections");
    public string QualifiedMeasuresUrl => Links.Url("qualified-measures");
    public string CountyAdministeredElectionsUrl => Links.Url("county-elections");
    public string CountyElectionsOfficesUrl => Links.Url("county-directory");

    /// <param name="kind">"primary" or "general".</param>
    public string CertifiedListUrl(int year, string kind) => Links.Url("certified-list", kind, year: year);
}
