namespace StateBallot.States.Vt;

/// <summary>All source URLs for the VT SOS elections site in one place.</summary>
public sealed class VtSourceConfig
{
    public string FilesBaseUrl { get; init; } =
        "https://outside.vermont.gov/dept/sos/Elections_Division/election_info_resources/candidates";

    /// <summary>
    /// The evergreen candidates page. Unlike the XLSX files below, this page's
    /// own URL is never year-parameterized - it always shows whatever cycle is
    /// current, including its "Primary Election - Tuesday, August 11, 2026 |
    /// General Election - Tuesday, November 3, 2026" plain-text date line (the
    /// only place either date is published; the candidate XLSX files never
    /// carry a date). See VtElectionDateScraper for how a year mismatch is
    /// detected.
    /// </summary>
    public string CandidatesPageUrl { get; init; } =
        "https://sos.vermont.gov/elections/election-info-resources/candidates";

    /// <param name="kind">"Primary" or "General" (case-insensitive).</param>
    public string CandidateListUrl(int year, string kind) =>
        string.Equals(kind, "Primary", StringComparison.OrdinalIgnoreCase)
            ? $"{FilesBaseUrl}/{year}_statewide_primary_qualified_candidates.xlsx"
            : $"{FilesBaseUrl}/{year}_general_election_qualified_candidates.xlsx";
}
