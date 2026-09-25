namespace StateBallot.States.Wy;

/// <summary>All source URLs for the WY SOS elections site in one place.</summary>
public sealed class WySourceConfig
{
    public string BaseUrl { get; init; } = "https://sos.wyo.gov";

    /// <summary>
    /// Yearly elections info page. Its embedded schema.org JSON-LD carries the
    /// actual Primary/General election dates - the candidate CSVs below only
    /// ever say "20NN PRIMARY ELECTION"/"20NN GENERAL ELECTION" as a text label,
    /// never a date.
    /// </summary>
    public string ElectionInfoPageUrl(int year) => $"{BaseUrl}/Elections/{year}ElectionInformation.aspx";

    /// <param name="kind">"Primary" or "General" (case-insensitive).</param>
    public string CandidateListUrl(int year, string kind) =>
        $"{BaseUrl}/Elections/Docs/{year}/{year}_WY_{Capitalize(kind)}_Election_Candidates.csv";

    private static string Capitalize(string kind) =>
        kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant();
}
