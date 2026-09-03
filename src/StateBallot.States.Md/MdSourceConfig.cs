namespace StateBallot.States.Md;

/// <summary>All source URLs for the MD SBE elections site in one place.</summary>
public sealed class MdSourceConfig
{
    public string BaseUrl { get; init; } = "https://elections.maryland.gov";

    /// <summary>Yearly landing page listing "Primary Election Day" / "General Election Day".</summary>
    public string ElectionsPageUrl(int year) => $"{BaseUrl}/elections/{year}/index.html";

    /// <summary>
    /// Statewide candidate list CSV (federal/state/judicial races only - see
    /// MdCollector remarks on the local/county "all_counties" export this
    /// deliberately doesn't cover yet).
    /// </summary>
    /// <param name="kind">"primary" or "general" (case-insensitive).</param>
    public string StatewideCandidateListUrl(int year, string kind)
    {
        var (folder, prefix) = string.Equals(kind, "primary", StringComparison.OrdinalIgnoreCase)
            ? ("primary_candidates", "GP")
            : ("general_candidates", "GG");
        return $"{BaseUrl}/elections/{year}/{folder}/{year}_{prefix}_statewide_candidatelist.csv";
    }
}
