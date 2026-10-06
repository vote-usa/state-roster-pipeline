namespace StateBallot.States.Ne;

/// <summary>All source URLs for the NE SOS elections site in one place. No bot-blocking observed - plain GETs.</summary>
public sealed class NeSourceConfig
{
    public string BaseUrl { get; init; } = "https://sos.nebraska.gov";

    /// <summary>
    /// The yearly elections page's own plain text carries "Primary Election: <date>"
    /// / "General Election: <date>" - the only place either date is published;
    /// neither workbook sheet below carries a date column of its own.
    /// </summary>
    public string ElectionsPageUrl { get; init; } = "https://sos.nebraska.gov/elections";

    /// <summary>
    /// The statewide filing workbook. Sheet 0 (default) is the current candidate
    /// roster (federal/state/legislative/judicial-adjacent-board/special-district
    /// races); sheet 1 is judicial retention questions (see NeRetentionMapper).
    /// A third sheet (petition-candidate filing status, including rejected/
    /// missed-deadline attempts) exists in the same workbook but isn't attempted -
    /// candidates who actually qualified by petition already appear in sheet 0
    /// with their real party value ("By Petition"), so sheet 2 adds only noise
    /// (failed attempts) for this pipeline's purposes.
    /// </summary>
    public string StatewideCandidateFilingListUrl(int year) =>
        $"{BaseUrl}/sites/default/files/doc/elections/{year}/Statewide_Candidate_Filing_List.xlsx";
}
