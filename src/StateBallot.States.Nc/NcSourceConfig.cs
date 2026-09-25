namespace StateBallot.States.Nc;

/// <summary>The single source URL for the NC SBE candidate filing export.</summary>
public sealed class NcSourceConfig
{
    public string BaseUrl { get; init; } = "https://s3.amazonaws.com/dl.ncsbe.gov";

    /// <summary>
    /// One CSV covering every contest (federal/state/judicial/county/municipal) and
    /// every county, for both the primary and general elections in the given year -
    /// unlike most other states, there's no separate elections-catalog endpoint or
    /// per-election/per-county URL; the whole cycle is one file.
    /// </summary>
    public string CandidateListingUrl(int year) =>
        $"{BaseUrl}/Elections/{year}/Candidate%20Filing/Candidate_Listing_{year}.csv";
}
