namespace StateBallot.States.Ca;

/// <summary>
/// All source URLs and URL templates in one place so that next year's site
/// changes only require edits here. Year-bearing URLs are built from the
/// target year argument; nothing is hardcoded to a specific election year.
/// </summary>
public sealed class CaSourceConfig
{
    /// <summary>SoS page listing statewide, special vacancy, and county-administered elections.</summary>
    public string UpcomingElectionsUrl { get; init; } =
        "https://www.sos.ca.gov/elections/upcoming-elections";

    /// <summary>SoS page listing statewide measures qualified for the ballot.</summary>
    public string QualifiedMeasuresUrl { get; init; } =
        "https://www.sos.ca.gov/elections/ballot-measures/qualified-ballot-measures";

    /// <summary>SoS page listing per-county local (county-administered) elections.</summary>
    public string CountyAdministeredElectionsUrl { get; init; } =
        "https://www.sos.ca.gov/elections/upcoming-elections/county-administered-elections";

    /// <summary>SoS directory of the 58 county elections offices.</summary>
    public string CountyElectionsOfficesUrl { get; init; } =
        "https://www.sos.ca.gov/elections/voting-resources/county-elections-offices";

    /// <summary>CDN root for statewide election documents (certified candidate lists etc.).</summary>
    public string StatewideElectionsCdnBase { get; init; } =
        "https://elections.cdn.sos.ca.gov/statewide-elections";

    /// <summary>
    /// Certified list of candidates for a statewide election, e.g.
    /// .../statewide-elections/2026-general/cert-list-candidates.pdf.
    /// <paramref name="kind"/> is "primary" or "general".
    /// </summary>
    public string CertifiedListUrl(int year, string kind) =>
        $"{StatewideElectionsCdnBase}/{year}-{kind}/cert-list-candidates.pdf";
}
